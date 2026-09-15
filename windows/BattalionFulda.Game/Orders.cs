namespace BattalionFulda;

internal enum OrderPosture
{
    Quick,
    Tactical,
    Hunt
}

internal sealed class PlannedMoveOrder
{
    public required ScenarioUnit Unit { get; init; }
    public required OrderPosture Posture { get; init; }
    public List<Point> Waypoints { get; } = [];
    public List<Point> Route { get; } = [];
    public bool Confirmed { get; set; }
    public int TotalCost { get; set; }
    public bool IsReachable { get; set; } = true;
    public string? FailureReason { get; set; }
    public MoveOrderExecutionState ExecutionState { get; set; } = MoveOrderExecutionState.Plotting;
    public double CarriedStepCost { get; set; }
    public double LastTurnCost { get; set; }
    public string? ExecutionNote { get; set; }

    public string Status => !IsReachable ? "BLOCKED" : ExecutionState switch
    {
        MoveOrderExecutionState.Executing => "EXECUTING",
        MoveOrderExecutionState.Partial => "PARTIAL",
        MoveOrderExecutionState.Complete => "COMPLETE",
        _ => Confirmed ? "PLANNED" : "PLOTTING"
    };
}

internal enum MoveOrderExecutionState
{
    Plotting,
    Planned,
    Executing,
    Partial,
    Complete
}

internal sealed record RouteResult(
    bool Success,
    IReadOnlyList<Point> Cells,
    int Cost,
    string? FailureReason = null);

internal sealed class MovementModel(GameData data)
{
    public int MinimumStepCost => data.MovementParameters.GetValueOrDefault("minimum_step_cost", 4);

    public int? StepCost(ScenarioUnit unit, Point from, Point to, OrderPosture posture)
    {
        MapCell destination = data.Cells[to.X, to.Y];
        string mobility = unit.Mobility.ToLowerInvariant();
        if (!data.TerrainMovementCosts.TryGetValue(
                (mobility, destination.Terrain.ToLowerInvariant()), out MovementCostProfile? terrain) ||
            !terrain.Passable)
            return null;

        if (data.Units.Any(other =>
                other.Id != unit.Id && other.X == to.X && other.Y == to.Y &&
                !other.Faction.Equals(unit.Faction, StringComparison.OrdinalIgnoreCase)))
            return null;

        int cost = terrain.For(posture);
        HexEdgeKey key = HexEdgeKey.Create(from, to);
        data.Crossings.TryGetValue(key, out MapEdge? crossing);
        bool followsRoad = false;
        if (data.Edges.TryGetValue(key, out MapEdge? featureEdge) && featureEdge.RoadClass > 0 &&
            (crossing is null || crossing.CrossingType.Equals(
                "bridge", StringComparison.OrdinalIgnoreCase)) &&
            data.RoadMovementCosts.TryGetValue(
                (mobility, featureEdge.RoadClass), out MovementCostProfile? road))
        {
            cost = Math.Min(cost, road.For(posture));
            followsRoad = true;
        }

        MapCell source = data.Cells[from.X, from.Y];
        if (!followsRoad && (source.RoadClass > 0 || destination.RoadClass > 0))
            cost += data.MovementParameters.GetValueOrDefault("road_access_penalty", 0);

        if (crossing is not null)
        {
            string crossingType = crossing.CrossingType.ToLowerInvariant();
            if (!data.WaterCrossingCosts.TryGetValue(
                    (mobility, crossing.RiverClass, crossingType), out WaterCrossingCost? water))
                return null;
            if (water.RequiresAmphibious && !unit.Amphibious)
                return null;
            cost += water.For(posture);
        }

        int elevationChange = destination.Elevation - source.Elevation;
        int elevationStep = Math.Max(1,
            data.MovementParameters.GetValueOrDefault("elevation_step_m", 25));
        int steps = Math.Abs(elevationChange) / elevationStep;
        int slopeCost = elevationChange > 0
            ? data.MovementParameters.GetValueOrDefault("uphill_cost_per_step", 2)
            : data.MovementParameters.GetValueOrDefault("downhill_cost_per_step", 1);
        return cost + steps * slopeCost;
    }
}

internal sealed class AStarRoutePlanner(GameData data)
{
    private readonly MovementModel movement = new(data);

    public RouteResult FindRoute(
        ScenarioUnit unit, Point start, Point goal, OrderPosture posture,
        IReadOnlySet<Point>? blockedCells = null)
    {
        if (start == goal) return new RouteResult(true, [start], 0);
        if (blockedCells?.Contains(goal) == true)
            return new RouteResult(false, [start], 0, "DESTINATION OCCUPIED");

        var frontier = new PriorityQueue<Point, int>();
        var previous = new Dictionary<Point, Point>();
        var costs = new Dictionary<Point, int> { [start] = 0 };
        frontier.Enqueue(start, 0);

        while (frontier.TryDequeue(out Point current, out _))
        {
            if (current == goal) break;
            foreach (Point neighbor in Neighbors(current))
            {
                if (blockedCells?.Contains(neighbor) == true) continue;
                int? step = movement.StepCost(unit, current, neighbor, posture);
                if (step is null) continue;
                int newCost = costs[current] + step.Value;
                if (costs.TryGetValue(neighbor, out int known) && newCost >= known) continue;
                costs[neighbor] = newCost;
                previous[neighbor] = current;
                int priority = newCost + HexDistance(neighbor, goal) * movement.MinimumStepCost;
                frontier.Enqueue(neighbor, priority);
            }
        }

        if (!costs.TryGetValue(goal, out int total))
            return new RouteResult(false, [start], 0,
                "NO LEGAL ROUTE — TERRAIN OR WATER BARRIER");

        var route = new List<Point> { goal };
        while (route[^1] != start) route.Add(previous[route[^1]]);
        route.Reverse();
        return new RouteResult(true, route, total);
    }

    private IEnumerable<Point> Neighbors(Point cell)
    {
        int up = (cell.X & 1) == 0 ? -1 : 0;
        int down = (cell.X & 1) == 0 ? 0 : 1;
        (int X, int Y)[] offsets =
        [
            (0, -1), (1, up), (1, down), (0, 1), (-1, down), (-1, up)
        ];
        foreach ((int dx, int dy) in offsets)
        {
            int x = cell.X + dx;
            int y = cell.Y + dy;
            if (x >= 0 && x < data.MapWidth && y >= 0 && y < data.MapHeight)
                yield return new Point(x, y);
        }
    }

    private static int HexDistance(Point first, Point second)
    {
        (int X, int Y, int Z) a = ToCube(first);
        (int X, int Y, int Z) b = ToCube(second);
        return Math.Max(Math.Abs(a.X - b.X),
            Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
    }

    private static (int X, int Y, int Z) ToCube(Point point)
    {
        int x = point.X;
        int z = point.Y - (point.X - (point.X & 1)) / 2;
        return (x, -x - z, z);
    }
}
