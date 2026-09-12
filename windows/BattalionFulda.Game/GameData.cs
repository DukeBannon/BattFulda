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

internal sealed record MapVertex(double Easting, double Northing);

internal sealed record MapFeaturePath(
    int Id,
    string Type,
    int FeatureClass,
    IReadOnlyList<MapVertex> Vertices);

internal sealed class GameData
{
    public required int MapWidth { get; init; }
    public required int MapHeight { get; init; }
    public required int CellSizeMeters { get; init; }
    public required double OriginEasting { get; init; }
    public required double OriginNorthing { get; init; }
    public required double ExtentWidthMeters { get; init; }
    public required double ExtentHeightMeters { get; init; }
    public required MapCell[,] Cells { get; init; }
    public required IReadOnlyList<ScenarioUnit> Units { get; init; }
    public required IReadOnlyList<MapFeaturePath> Features { get; init; }

    public static GameData Load()
    {
        string root = FindRepositoryRoot();
        string mapsPath = Path.Combine(root, "data", "database", "exports", "maps.csv");
        string[] map = ReadCsv(mapsPath).Skip(1).First(row =>
            string.Equals(row[1], "point_alpha_corridor", StringComparison.OrdinalIgnoreCase));
        int width = ParseInt(map[3]);
        int height = ParseInt(map[4]);
        MapCell[,] cells = LoadMap(
            Path.Combine(root, "data", "maps", "point_alpha_cells.csv"), width, height);
        IReadOnlyList<ScenarioUnit> units = LoadUnits(Path.Combine(
            root, "data", "database", "exports", "scenario_units.csv"));
        IReadOnlyList<MapFeaturePath> features = LoadFeatures(Path.Combine(
            root, "data", "maps", "point_alpha_features.csv"));

        return new GameData
        {
            MapWidth = width,
            MapHeight = height,
            CellSizeMeters = ParseInt(map[5]),
            OriginEasting = ParseDouble(map[7]),
            OriginNorthing = ParseDouble(map[8]),
            ExtentWidthMeters = ParseDouble(map[9]),
            ExtentHeightMeters = ParseDouble(map[10]),
            Cells = cells,
            Units = units,
            Features = features
        };
    }

    private static MapCell[,] LoadMap(string path, int width, int height)
    {
        var cells = new MapCell[width, height];
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            int x = ParseInt(row[1]);
            int y = ParseInt(row[2]);
            if (x is < 0 || x >= width || y is < 0 || y >= height)
                continue;

            cells[x, y] = new MapCell(
                x, y, ParseInt(row[3]), row[4], ParseInt(row[5]), ParseInt(row[6]),
                ParseInt(row[7]), ParseInt(row[8]), ParseInt(row[9]),
                ParseInt(row[10]) != 0);
        }

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            cells[x, y] ??= new MapCell(x, y, 300, "clear", 0, 0, 0, 0, 0, false);

        return cells;
    }

    private static IReadOnlyList<MapFeaturePath> LoadFeatures(string path)
    {
        var paths = new Dictionary<int, (string Type, int Class, List<MapVertex> Vertices)>();
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            int id = ParseInt(row[1]);
            if (!paths.TryGetValue(id, out var pathData))
            {
                pathData = (row[2], ParseInt(row[3]), []);
                paths.Add(id, pathData);
            }
            pathData.Vertices.Add(new MapVertex(ParseDouble(row[5]), ParseDouble(row[6])));
        }
        return paths.OrderBy(pair => pair.Key)
            .Select(pair => new MapFeaturePath(
                pair.Key, pair.Value.Type, pair.Value.Class, pair.Value.Vertices))
            .ToArray();
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

    private static double ParseDouble(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

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
