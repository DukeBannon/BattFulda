namespace BattalionFulda;

internal enum VisibilityDifficulty { Open, Assisted, Standard, Veteran, Expert }
internal enum ContactQuality { Uncertain, Identified }

// A snapshot, never a reference to the live enemy unit. Hidden units cannot
// silently move their last-known marker or disclose casualties through it.
internal sealed record ContactReport(
    int ContactId, Point ReportedCell, ContactQuality Quality,
    string? Name, string? Category, double LastObservedSeconds, bool IsCurrent);

internal sealed record SpottingRules(
    int ClearRange, int ObscuredRange, int MovementCueRange,
    int FiringCueRange, double MemorySeconds)
{
    public static SpottingRules For(VisibilityDifficulty difficulty) => difficulty switch
    {
        VisibilityDifficulty.Assisted => new(32, 6, 6, 10, 900),
        VisibilityDifficulty.Veteran => new(20, 2, 3, 5, 300),
        VisibilityDifficulty.Expert => new(16, 1, 2, 4, 180),
        _ => new(24, 3, 4, 6, 600)
    };
}

internal sealed class SideContactModel(GameData data, string faction,
    VisibilityDifficulty difficulty = VisibilityDifficulty.Standard)
{
    private readonly LineOfSightModel los = new(data);
    private readonly Dictionary<int, ContactReport> reports = [];
    private readonly Dictionary<int, int> contactIds = [];
    private int nextContactId = 1;
    private double previousTime = -1;

    public IReadOnlyList<ContactReport> Reports => reports.Values
        .OrderBy(report => report.ContactId).ToArray();

    public HashSet<Point> KnownEnemyCells => reports.Values
        .Where(report => report.IsCurrent && report.Quality == ContactQuality.Identified)
        .Select(report => report.ReportedCell).ToHashSet();

    public bool CanTarget(ScenarioUnit unit) => reports.TryGetValue(unit.Id, out ContactReport? report) &&
        report.IsCurrent && report.Quality == ContactQuality.Identified &&
        report.ReportedCell == new Point(unit.X, unit.Y) && !unit.IsDestroyed;

    public void Update(double seconds, IReadOnlySet<int> moving, IReadOnlySet<int> firing)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds < previousTime)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        previousTime = seconds;
        SpottingRules rules = SpottingRules.For(difficulty);
        ScenarioUnit[] observers = data.Units.Where(unit => !unit.IsDestroyed &&
            unit.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase)).ToArray();

        foreach (ScenarioUnit enemy in data.Units.Where(unit =>
                     !unit.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase)))
        {
            Point actual = new(enemy.X, enemy.Y);
            bool identified = difficulty == VisibilityDifficulty.Open && !enemy.IsDestroyed;
            bool cue = false;
            foreach (ScenarioUnit observer in observers)
            {
                if (enemy.IsDestroyed) break;
                LineOfSightResult sight = los.Trace(new Point(observer.X, observer.Y), actual);
                identified |= sight.Quality != LineOfSightQuality.Blocked &&
                    sight.Range <= (sight.Quality == LineOfSightQuality.Clear
                        ? rules.ClearRange : rules.ObscuredRange);
                // Sound/movement cues do not require optical LOS and never
                // disclose identity or a reliably exact position.
                cue |= firing.Contains(enemy.Id) && sight.Range <= rules.FiringCueRange ||
                       moving.Contains(enemy.Id) && sight.Range <= rules.MovementCueRange;
            }

            if (identified || cue)
            {
                if (!contactIds.TryGetValue(enemy.Id, out int contactId))
                    contactIds[enemy.Id] = contactId = nextContactId++;
                Point reported = identified ? actual : ApproximateCell(actual);
                reports[enemy.Id] = new ContactReport(contactId, reported,
                    identified ? ContactQuality.Identified : ContactQuality.Uncertain,
                    identified ? enemy.Name : null, identified ? enemy.Category : null,
                    seconds, true);
            }
            else if (reports.TryGetValue(enemy.Id, out ContactReport? prior))
            {
                if (difficulty == VisibilityDifficulty.Open ||
                    seconds - prior.LastObservedSeconds > rules.MemorySeconds)
                    reports.Remove(enemy.Id);
                else
                    reports[enemy.Id] = prior with { IsCurrent = false };
            }
        }
    }

    private Point ApproximateCell(Point actual) => new(
        Math.Min(data.MapWidth - 1, actual.X / 3 * 3 + 1),
        Math.Min(data.MapHeight - 1, actual.Y / 3 * 3 + 1));
}
