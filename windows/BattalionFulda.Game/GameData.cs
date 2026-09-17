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

internal sealed class ScenarioUnit(
    int id,
    string key,
    string name,
    string formation,
    string parentFormation,
    string type,
    string faction,
    string country,
    int x,
    int y,
    int strength,
    int morale,
    int suppression,
    int readiness,
    bool amphibious,
    string mobility,
    int movePoints,
    string category,
    int hardAttack,
    int softAttack,
    int defense,
    int rangeCells)
{
    public int Id { get; } = id;
    public string Key { get; } = key;
    public string Name { get; } = name;
    public string Formation { get; } = formation;
    public string ParentFormation { get; } = parentFormation;
    public string Type { get; } = type;
    public string Faction { get; } = faction;
    public string Country { get; } = country;
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public int Strength { get; set; } = strength;
    public int Morale { get; set; } = morale;
    public int Suppression { get; set; } = suppression;
    public int Readiness { get; set; } = readiness;
    public bool Amphibious { get; } = amphibious;
    public string Mobility { get; } = mobility;
    public int MovePoints { get; } = movePoints;
    public string Category { get; } = category;
    public int HardAttack { get; } = hardAttack;
    public int SoftAttack { get; } = softAttack;
    public int Defense { get; } = defense;
    public int RangeCells { get; } = rangeCells;
    public bool IsDestroyed => Strength <= 0;
}

internal sealed record UnitTypeCombatProfile(
    string Category,
    int HardAttack,
    int SoftAttack,
    int Defense,
    int RangeCells);

internal readonly record struct HexEdgeKey(int AX, int AY, int BX, int BY)
{
    public static HexEdgeKey Create(Point first, Point second) =>
        first.X < second.X || (first.X == second.X && first.Y <= second.Y)
            ? new HexEdgeKey(first.X, first.Y, second.X, second.Y)
            : new HexEdgeKey(second.X, second.Y, first.X, first.Y);
}

internal sealed record MapEdge(
    Point From,
    Point To,
    int RoadClass,
    int RiverClass,
    string CrossingType);

internal sealed record MovementCostProfile(
    bool Passable,
    int Quick,
    int Tactical,
    int Hunt)
{
    public int For(OrderPosture posture) => posture switch
    {
        OrderPosture.Quick => Quick,
        OrderPosture.Hunt => Hunt,
        _ => Tactical
    };
}

internal sealed record WaterCrossingCost(
    bool RequiresAmphibious,
    int Quick,
    int Tactical,
    int Hunt)
{
    public int For(OrderPosture posture) => posture switch
    {
        OrderPosture.Quick => Quick,
        OrderPosture.Hunt => Hunt,
        _ => Tactical
    };
}

internal sealed record MapVertex(double Easting, double Northing);

internal sealed record MapFeaturePath(
    int Id,
    string Type,
    int FeatureClass,
    IReadOnlyList<MapVertex> Vertices);

internal sealed class GameData
{
    public required int ScenarioYear { get; init; }
    public required DateTime ScenarioStart { get; init; }
    public required int TurnMinutes { get; init; }
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
    public required IReadOnlyDictionary<HexEdgeKey, MapEdge> Edges { get; init; }
    public required IReadOnlyDictionary<HexEdgeKey, MapEdge> Crossings { get; init; }
    public required IReadOnlyDictionary<(string Mobility, string Terrain), MovementCostProfile>
        TerrainMovementCosts { get; init; }
    public required IReadOnlyDictionary<(string Mobility, int RoadClass), MovementCostProfile>
        RoadMovementCosts { get; init; }
    public required IReadOnlyDictionary<(string Mobility, int RiverClass, string CrossingType), WaterCrossingCost>
        WaterCrossingCosts { get; init; }
    public required IReadOnlyDictionary<string, int> MovementParameters { get; init; }

    public static GameData Load()
    {
        string root = FindRepositoryRoot();
        string[] scenario = ReadCsv(Path.Combine(root, "data", "database", "exports",
            "scenarios.csv")).Skip(1).First(row =>
                string.Equals(row[1], "a2_command_post", StringComparison.OrdinalIgnoreCase));
        string mapsPath = Path.Combine(root, "data", "database", "exports", "maps.csv");
        string[] map = ReadCsv(mapsPath).Skip(1).First(row =>
            string.Equals(row[1], "point_alpha_corridor", StringComparison.OrdinalIgnoreCase));
        int width = ParseInt(map[3]);
        int height = ParseInt(map[4]);
        MapCell[,] cells = LoadMap(
            Path.Combine(root, "data", "maps", "point_alpha_cells.csv"), width, height);
        IReadOnlyDictionary<string, UnitTypeCombatProfile> combatProfiles =
            LoadCombatProfiles(Path.Combine(root, "data", "database", "exports",
                "unit_types.csv"));
        IReadOnlyList<ScenarioUnit> units = LoadUnits(Path.Combine(
            root, "data", "database", "exports", "scenario_units.csv"), combatProfiles);
        IReadOnlyList<MapFeaturePath> features = LoadFeatures(Path.Combine(
            root, "data", "maps", "point_alpha_features.csv"));
        IReadOnlyDictionary<HexEdgeKey, MapEdge> edges = LoadEdges(Path.Combine(
            root, "data", "database", "exports", "map_edges.csv"));
        IReadOnlyDictionary<HexEdgeKey, MapEdge> crossings = LoadCrossings(Path.Combine(
            root, "data", "database", "exports", "map_crossings.csv"));
        IReadOnlyDictionary<(string, string), MovementCostProfile> terrainCosts =
            LoadTerrainMovementCosts(Path.Combine(root, "data", "database", "exports",
                "terrain_movement_costs.csv"));
        IReadOnlyDictionary<(string, int), MovementCostProfile> roadCosts =
            LoadRoadMovementCosts(Path.Combine(root, "data", "database", "exports",
                "road_movement_costs.csv"));
        IReadOnlyDictionary<(string, int, string), WaterCrossingCost> crossingCosts =
            LoadWaterCrossingCosts(Path.Combine(root, "data", "database", "exports",
                "water_crossing_costs.csv"));
        IReadOnlyDictionary<string, int> movementParameters = LoadMovementParameters(
            Path.Combine(root, "data", "database", "exports", "movement_parameters.csv"));

        return new GameData
        {
            ScenarioYear = ParseInt(scenario[4]),
            ScenarioStart = DateTime.Parse(scenario[5], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal),
            TurnMinutes = ParseInt(scenario[6]),
            MapWidth = width,
            MapHeight = height,
            CellSizeMeters = ParseInt(map[5]),
            OriginEasting = ParseDouble(map[7]),
            OriginNorthing = ParseDouble(map[8]),
            ExtentWidthMeters = ParseDouble(map[9]),
            ExtentHeightMeters = ParseDouble(map[10]),
            Cells = cells,
            Units = units,
            Features = features,
            Edges = edges,
            Crossings = crossings,
            TerrainMovementCosts = terrainCosts,
            RoadMovementCosts = roadCosts,
            WaterCrossingCosts = crossingCosts,
            MovementParameters = movementParameters
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

    private static IReadOnlyList<ScenarioUnit> LoadUnits(
        string path, IReadOnlyDictionary<string, UnitTypeCombatProfile> combatProfiles)
    {
        var units = new List<ScenarioUnit>();
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            if (!string.Equals(row[0], "a2_command_post", StringComparison.OrdinalIgnoreCase))
                continue;

            UnitTypeCombatProfile combat = combatProfiles[row[6]];
            units.Add(new ScenarioUnit(
                ParseInt(row[1]), row[2], row[3], row[4], row[5], row[7], row[8], row[9],
                ParseInt(row[10]), ParseInt(row[11]), ParseInt(row[12]), ParseInt(row[13]),
                ParseInt(row[14]), ParseInt(row[15]), ParseInt(row[16]) != 0,
                row[17].ToLowerInvariant(), ParseInt(row[18]), combat.Category,
                combat.HardAttack, combat.SoftAttack, combat.Defense, combat.RangeCells));
        }

        return units;
    }

    private static IReadOnlyDictionary<string, UnitTypeCombatProfile> LoadCombatProfiles(
        string path) => ReadCsv(path).Skip(1).ToDictionary(
            row => row[1],
            row => new UnitTypeCombatProfile(row[5].ToLowerInvariant(), ParseInt(row[11]),
                ParseInt(row[12]), ParseInt(row[13]), ParseInt(row[14])),
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<HexEdgeKey, MapEdge> LoadEdges(string path)
    {
        var edges = new Dictionary<HexEdgeKey, MapEdge>();
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            if (!string.Equals(row[0], "point_alpha_corridor", StringComparison.OrdinalIgnoreCase))
                continue;
            Point from = new(ParseInt(row[1]), ParseInt(row[2]));
            Point to = new(ParseInt(row[4]), ParseInt(row[5]));
            edges[HexEdgeKey.Create(from, to)] = new MapEdge(
                from, to, ParseInt(row[6]), ParseInt(row[7]), row[8].ToLowerInvariant());
        }
        return edges;
    }

    private static IReadOnlyDictionary<HexEdgeKey, MapEdge> LoadCrossings(string path)
    {
        var edges = new Dictionary<HexEdgeKey, MapEdge>();
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            if (!string.Equals(row[0], "point_alpha_corridor", StringComparison.OrdinalIgnoreCase))
                continue;
            Point first = new(ParseInt(row[1]), ParseInt(row[2]));
            Point second = new(ParseInt(row[3]), ParseInt(row[4]));
            edges[HexEdgeKey.Create(first, second)] = new MapEdge(
                first, second, 0, ParseInt(row[5]), row[6]);
        }
        return edges;
    }

    private static IReadOnlyDictionary<(string, string), MovementCostProfile>
        LoadTerrainMovementCosts(string path)
    {
        var costs = new Dictionary<(string, string), MovementCostProfile>();
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            bool passable = ParseInt(row[2]) != 0;
            costs[(row[0].ToLowerInvariant(), row[1].ToLowerInvariant())] =
                new MovementCostProfile(passable,
                    passable ? ParseInt(row[3]) : 0,
                    passable ? ParseInt(row[4]) : 0,
                    passable ? ParseInt(row[5]) : 0);
        }
        return costs;
    }

    private static IReadOnlyDictionary<(string, int), MovementCostProfile>
        LoadRoadMovementCosts(string path)
    {
        var costs = new Dictionary<(string, int), MovementCostProfile>();
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            costs[(row[0].ToLowerInvariant(), ParseInt(row[1]))] =
                new MovementCostProfile(true, ParseInt(row[2]), ParseInt(row[3]), ParseInt(row[4]));
        }
        return costs;
    }

    private static IReadOnlyDictionary<(string, int, string), WaterCrossingCost>
        LoadWaterCrossingCosts(string path)
    {
        var costs = new Dictionary<(string, int, string), WaterCrossingCost>();
        foreach (string[] row in ReadCsv(path).Skip(1))
        {
            costs[(row[0].ToLowerInvariant(), ParseInt(row[1]), row[2].ToLowerInvariant())] =
                new WaterCrossingCost(ParseInt(row[3]) != 0,
                    ParseInt(row[4]), ParseInt(row[5]), ParseInt(row[6]));
        }
        return costs;
    }

    private static IReadOnlyDictionary<string, int> LoadMovementParameters(string path) =>
        ReadCsv(path).Skip(1).ToDictionary(
            row => row[0], row => ParseInt(row[1]), StringComparer.OrdinalIgnoreCase);

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
