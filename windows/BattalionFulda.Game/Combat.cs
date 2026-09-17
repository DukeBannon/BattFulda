namespace BattalionFulda;

internal enum LineOfSightQuality
{
    Clear,
    Obscured,
    Blocked
}

internal sealed record LineOfSightResult(
    LineOfSightQuality Quality,
    int Range,
    IReadOnlyList<Point> Cells,
    Point? BlockingCell = null,
    string? Reason = null);

internal sealed class LineOfSightModel(GameData data)
{
    private const double EyeHeightMeters = 3.0;

    public LineOfSightQuality[,] CalculateVisibility(Point observer)
    {
        var visibility = new LineOfSightQuality[data.MapWidth, data.MapHeight];
        for (int x = 0; x < data.MapWidth; x++)
        for (int y = 0; y < data.MapHeight; y++)
            visibility[x, y] = Trace(observer, new Point(x, y)).Quality;
        return visibility;
    }

    public LineOfSightResult Trace(Point observer, Point target)
    {
        IReadOnlyList<Point> line = HexLine(observer, target);
        int range = Math.Max(0, line.Count - 1);
        if (range == 0)
            return new LineOfSightResult(LineOfSightQuality.Clear, 0, line);

        MapCell source = data.Cells[observer.X, observer.Y];
        MapCell destination = data.Cells[target.X, target.Y];
        double sourceHeight = source.Elevation + EyeHeightMeters;
        double targetHeight = destination.Elevation + EyeHeightMeters;
        bool obscured = IsConcealing(destination);

        for (int index = 1; index < line.Count - 1; index++)
        {
            Point point = line[index];
            MapCell cell = data.Cells[point.X, point.Y];
            // Canopy height alone must not permit looking through a forest.
            // A wooded destination is visible at its edge; woods before it block.
            if (cell.Terrain.Equals("woods", StringComparison.OrdinalIgnoreCase))
                return new LineOfSightResult(
                    LineOfSightQuality.Blocked, range, line, point, "BLOCKED BY WOODS");
            // Dense built-up cells have no modeled street-level firing corridors.
            // See into the first urban destination, never through intervening town cells.
            if (cell.Terrain.Equals("urban", StringComparison.OrdinalIgnoreCase))
                return new LineOfSightResult(
                    LineOfSightQuality.Blocked, range, line, point, "BLOCKED BY BUILDINGS");
            double fraction = index / (double)range;
            double sightHeight = sourceHeight + (targetHeight - sourceHeight) * fraction;
            double obstacleHeight = cell.Elevation + ObstacleHeight(cell);
            if (obstacleHeight > sightHeight + 1.0)
            {
                string reason = cell.Terrain.Equals("woods", StringComparison.OrdinalIgnoreCase)
                    ? "BLOCKED BY WOODS"
                    : cell.Terrain.Equals("urban", StringComparison.OrdinalIgnoreCase)
                        ? "BLOCKED BY BUILDINGS"
                        : "BLOCKED BY ELEVATION";
                return new LineOfSightResult(
                    LineOfSightQuality.Blocked, range, line, point, reason);
            }

            if (IsConcealing(cell)) obscured = true;
        }

        return new LineOfSightResult(
            obscured ? LineOfSightQuality.Obscured : LineOfSightQuality.Clear,
            range, line, null, obscured ? "PARTIALLY OBSCURED" : "CLEAR LINE OF SIGHT");
    }

    public static int HexDistance(Point first, Point second) => HexLine(first, second).Count - 1;

    internal static IReadOnlyList<Point> HexLine(Point first, Point second)
    {
        bool reverse = first.X > second.X || first.X == second.X && first.Y > second.Y;
        Point start = reverse ? second : first;
        Point end = reverse ? first : second;
        Cube a = ToCube(start);
        Cube b = ToCube(end);
        int distance = CubeDistance(a, b);
        var cells = new List<Point>(distance + 1);
        for (int step = 0; step <= distance; step++)
        {
            double fraction = distance == 0 ? 0 : step / (double)distance;
            Cube cube = RoundCube(new Cube(
                a.X + (b.X - a.X) * fraction,
                a.Y + (b.Y - a.Y) * fraction,
                a.Z + (b.Z - a.Z) * fraction));
            Point cell = FromCube(cube);
            if (cells.Count == 0 || cells[^1] != cell) cells.Add(cell);
        }
        if (reverse) cells.Reverse();
        return cells;
    }

    private static bool IsConcealing(MapCell cell) =>
        cell.Terrain.Equals("woods", StringComparison.OrdinalIgnoreCase) ||
        cell.Terrain.Equals("urban", StringComparison.OrdinalIgnoreCase) ||
        cell.Terrain.Equals("cultivated", StringComparison.OrdinalIgnoreCase);

    private static double ObstacleHeight(MapCell cell) =>
        cell.Terrain.ToLowerInvariant() switch
        {
            "woods" => 18.0,
            "urban" => 12.0,
            "cultivated" => 1.5,
            _ => 0.0
        };

    private readonly record struct Cube(double X, double Y, double Z);

    private static Cube ToCube(Point point)
    {
        int x = point.X;
        int z = point.Y - (point.X - (point.X & 1)) / 2;
        return new Cube(x, -x - z, z);
    }

    private static Point FromCube(Cube cube)
    {
        int x = (int)cube.X;
        int z = (int)cube.Z;
        return new Point(x, z + (x - (x & 1)) / 2);
    }

    private static int CubeDistance(Cube first, Cube second) => (int)Math.Max(
        Math.Abs(first.X - second.X),
        Math.Max(Math.Abs(first.Y - second.Y), Math.Abs(first.Z - second.Z)));

    private static Cube RoundCube(Cube cube)
    {
        int x = (int)Math.Round(cube.X);
        int y = (int)Math.Round(cube.Y);
        int z = (int)Math.Round(cube.Z);
        double dx = Math.Abs(x - cube.X);
        double dy = Math.Abs(y - cube.Y);
        double dz = Math.Abs(z - cube.Z);
        if (dx > dy && dx > dz) x = -y - z;
        else if (dy > dz) y = -x - z;
        else z = -x - y;
        return new Cube(x, y, z);
    }
}

internal enum FireOrderState
{
    Planned,
    Executing,
    Complete,
    Invalid
}

internal sealed class PlannedFireOrder
{
    public required ScenarioUnit Unit { get; init; }
    public required ScenarioUnit Target { get; init; }
    public required LineOfSightResult LineOfSight { get; set; }
    public FireOrderState State { get; set; } = FireOrderState.Planned;
    public CombatResult? Result { get; set; }
    public string Status => State switch
    {
        FireOrderState.Executing => "EXECUTING",
        FireOrderState.Complete => "COMPLETE",
        FireOrderState.Invalid => "NO SHOT",
        _ => "PLANNED"
    };
}

internal sealed record FireValidation(
    bool CanFire,
    LineOfSightResult LineOfSight,
    string Message);

internal sealed record CombatResult(
    PlannedFireOrder Order,
    bool Fired,
    int StrengthLoss,
    int SuppressionAdded,
    string Message);

internal sealed class DirectFireModel(GameData data)
{
    private readonly LineOfSightModel lineOfSight = new(data);

    public FireValidation Validate(ScenarioUnit attacker, ScenarioUnit target)
    {
        LineOfSightResult sight = lineOfSight.Trace(
            new Point(attacker.X, attacker.Y), new Point(target.X, target.Y));
        if (attacker.IsDestroyed || target.IsDestroyed)
            return new FireValidation(false, sight, "UNIT DESTROYED");
        if (attacker.Faction.Equals(target.Faction, StringComparison.OrdinalIgnoreCase))
            return new FireValidation(false, sight, "FRIENDLY TARGET");
        if (attacker.Category is "artillery" or "rocket_artillery")
            return new FireValidation(false, sight, "INDIRECT FIRE NOT YET AVAILABLE");
        int attack = IsArmored(target) ? attacker.HardAttack : attacker.SoftAttack;
        if (attack <= 0)
            return new FireValidation(false, sight, "NO EFFECTIVE DIRECT-FIRE WEAPON");
        if (sight.Range > attacker.RangeCells)
            return new FireValidation(false, sight,
                $"OUT OF RANGE — {sight.Range} / {attacker.RangeCells} HEXES");
        if (sight.Quality == LineOfSightQuality.Blocked)
            return new FireValidation(false, sight, sight.Reason ?? "LINE OF SIGHT BLOCKED");
        return new FireValidation(true, sight,
            $"{sight.Quality.ToString().ToUpperInvariant()} — RANGE {sight.Range} HEXES");
    }

    public CombatResult Resolve(PlannedFireOrder order, int turnNumber)
    {
        FireValidation validation = Validate(order.Unit, order.Target);
        order.LineOfSight = validation.LineOfSight;
        if (!validation.CanFire)
            return new CombatResult(order, false, 0, 0, validation.Message);

        ScenarioUnit attacker = order.Unit;
        ScenarioUnit target = order.Target;
        int attack = IsArmored(target) ? attacker.HardAttack : attacker.SoftAttack;
        int condition = Math.Max(0, attacker.Readiness / 25 - attacker.Suppression / 2);
        int rangePenalty = validation.LineOfSight.Range <= 1 ? 0 :
            (validation.LineOfSight.Range - 1) * 3 / Math.Max(1, attacker.RangeCells);
        int concealment = validation.LineOfSight.Quality == LineOfSightQuality.Obscured ? 2 : 0;
        int terrainDefense = data.Cells[target.X, target.Y].Terrain.ToLowerInvariant() switch
        {
            "urban" => 3,
            "woods" => 2,
            "cultivated" => 1,
            _ => 0
        };
        int roll = DeterministicRoll(attacker.Id, target.Id, turnNumber);
        int score = attack + condition + roll - target.Defense - rangePenalty -
                    concealment - terrainDefense;
        int strengthLoss = score >= 4 ? 2 : score >= 0 ? 1 : 0;
        int suppression = Math.Clamp(1 + attack / 4 + (score >= 0 ? 1 : 0), 1, 4);
        string message = strengthLoss > 0
            ? $"{target.Name.ToUpperInvariant()} — {strengthLoss} LOSS, +{suppression} SUPPRESSION"
            : $"{target.Name.ToUpperInvariant()} SUPPRESSED +{suppression}";
        return new CombatResult(order, true, strengthLoss, suppression, message);
    }

    public static void Apply(CombatResult result)
    {
        if (!result.Fired) return;
        ScenarioUnit target = result.Order.Target;
        target.Strength = Math.Max(0, target.Strength - result.StrengthLoss);
        target.Suppression = Math.Clamp(target.Suppression + result.SuppressionAdded, 0, 10);
        target.Readiness = Math.Max(0,
            target.Readiness - result.SuppressionAdded * 3 - result.StrengthLoss * 5);
        target.Morale = Math.Max(0,
            target.Morale - result.SuppressionAdded * 2 - result.StrengthLoss * 4);
    }

    internal static bool IsArmored(ScenarioUnit unit) =>
        unit.Mobility is "tracked" or "wheeled";

    private static int DeterministicRoll(int attackerId, int targetId, int turnNumber)
    {
        uint value = unchecked((uint)(attackerId * 73856093 ^ targetId * 19349663 ^
                                      turnNumber * 83492791));
        value ^= value >> 13;
        value *= 1274126177;
        return (int)(value % 5) - 2;
    }
}

internal sealed class WegoCombatExecution
{
    private readonly DirectFireModel combat;
    private readonly int turnNumber;
    private readonly double fireTimeSeconds;
    private readonly Func<ScenarioUnit, ScenarioUnit, bool>? canTarget;
    private bool resolved;

    public WegoCombatExecution(
        GameData data, IEnumerable<PlannedFireOrder> orders, int turnNumber,
        Func<ScenarioUnit, ScenarioUnit, bool>? canTarget = null)
    {
        combat = new DirectFireModel(data);
        this.turnNumber = turnNumber;
        this.canTarget = canTarget;
        Orders = orders.OrderBy(order => order.Unit.Id).ToArray();
        foreach (PlannedFireOrder order in Orders) order.State = FireOrderState.Executing;
        fireTimeSeconds = data.TurnMinutes * 60.0 * 0.2;
    }

    public IReadOnlyList<PlannedFireOrder> Orders { get; }
    public IReadOnlyList<CombatResult> Results { get; private set; } = [];
    public bool IsResolved => resolved;

    public void AdvanceTo(double elapsedGameSeconds)
    {
        if (resolved || elapsedGameSeconds < fireTimeSeconds) return;
        CombatResult[] results = Orders.Select(order =>
            canTarget is not null && !canTarget(order.Unit, order.Target)
                ? new CombatResult(order, false, 0, 0, "TARGET CONTACT LOST — FIRE HELD")
                : combat.Resolve(order, turnNumber)).ToArray();
        foreach (CombatResult result in results)
        {
            DirectFireModel.Apply(result);
            result.Order.Result = result;
            result.Order.State = result.Fired ? FireOrderState.Complete : FireOrderState.Invalid;
        }
        Results = results;
        resolved = true;
    }
}
