using System.Globalization;
using System.Text;

namespace BattalionFulda;

internal sealed record MapCell(
    int X,
    int Y,
    int Elevation,
    string Terrain,
    int RoadClass,
    int RoadLinks,
    int RiverClass,
    int RiverLinks,
    int SettlementLevel,
    bool HasBridge);

internal sealed record ScenarioUnit(
    int Id,
    string Key,
    string Name,
    string Formation,
    string ParentFormation,
    string Type,
    string Faction,
    string Country,
    int X,
    int Y,
    int Strength,
    int Morale,
    int Suppression,
    int Readiness,
    bool Amphibious);

internal sealed class GameData
{
    public const int MapWidth = 40;
    public const int MapHeight = 40;

    public required MapCell[,] Cells { get; init; }
    public required IReadOnlyList<ScenarioUnit> Units { get; init; }

    public static GameData Load()
    {
        string root = FindRepositoryRoot();
        MapCell[,] cells = LoadMap(Path.Combine(root, "data", "maps", "point_alpha_cells.csv"));
        IReadOnlyList<ScenarioUnit> units = LoadUnits(Path.Combine(
            root, "data", "database", "exports", "scenario_units.csv"));

        return new GameData { Cells = cells, Units = units };
    }

    private static MapCell[,] LoadMap(string path)
    {
        var cells = new MapCell[MapWidth, MapHeight];
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            int x = ParseInt(row[1]);
            int y = ParseInt(row[2]);
            if (x is < 0 or >= MapWidth || y is < 0 or >= MapHeight)
                continue;

            cells[x, y] = new MapCell(
                x, y, ParseInt(row[3]), row[4], ParseInt(row[5]), ParseInt(row[6]),
                ParseInt(row[7]), ParseInt(row[8]), ParseInt(row[9]),
                ParseInt(row[10]) != 0);
        }

        for (int y = 0; y < MapHeight; y++)
        for (int x = 0; x < MapWidth; x++)
            cells[x, y] ??= new MapCell(x, y, 300, "clear", 0, 0, 0, 0, 0, false);

        return cells;
    }

    private static IReadOnlyList<ScenarioUnit> LoadUnits(string path)
    {
        var units = new List<ScenarioUnit>();
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            if (!string.Equals(row[0], "a2_command_post", StringComparison.OrdinalIgnoreCase))
                continue;

            units.Add(new ScenarioUnit(
                ParseInt(row[1]), row[2], row[3], row[4], row[5], row[7], row[8], row[9],
                ParseInt(row[10]), ParseInt(row[11]), ParseInt(row[12]), ParseInt(row[13]),
                ParseInt(row[14]), ParseInt(row[15]), ParseInt(row[16]) != 0));
        }

        return units;
    }

    private static IEnumerable<string[]> ReadCsv(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            var fields = new List<string>();
            var value = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (ch == ',' && !quoted)
                {
                    fields.Add(value.ToString());
                    value.Clear();
                }
                else
                {
                    value.Append(ch);
                }
            }

            fields.Add(value.ToString());
            yield return fields.ToArray();
        }
    }

    private static int ParseInt(string value) =>
        int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "data", "maps", "point_alpha_cells.csv")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Battalion: Fulda data directory. Run the game from inside the repository.");
    }
}
