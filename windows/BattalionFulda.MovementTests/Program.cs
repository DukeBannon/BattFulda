using BattalionFulda;
using System.Drawing;

GameData data = GameData.Load();
Assert(data.ScenarioStart == new DateTime(1985, 6, 6, 6, 0, 0),
    "The scenario clock must begin at 0600 on 6 June 1985.");
Assert(data.TurnMinutes == 15,
    "The W2.3 scenario must resolve in 15-minute turns.");
var movement = new MovementModel(data);
var planner = new AStarRoutePlanner(data);
ScenarioUnit routeDiagnosticHq = data.Units.First(unit => unit.Key == "team_alpha_hq");
RouteResult hqRoadRoute = planner.FindRoute(routeDiagnosticHq,
    new Point(22, 51), new Point(28, 48), OrderPosture.Tactical);
int hqRoadEdges = hqRoadRoute.Cells.Zip(hqRoadRoute.Cells.Skip(1)).Count(pair =>
    data.Edges.TryGetValue(HexEdgeKey.Create(pair.First, pair.Second), out MapEdge? edge) &&
    edge.RoadClass > 0);
Assert(hqRoadRoute.Success && hqRoadEdges >= hqRoadRoute.Cells.Count - 2,
    "The HQ route from waypoint 22,51 to 28,48 must enter and then remain on the secondary road.");
ScenarioUnit tank = data.Units.First(unit => unit.Key == "team_alpha_tank");
ScenarioUnit amphibious = data.Units.First(unit => unit.Key == "team_alpha_hq");

MapEdge stream = data.Crossings.Values.First(edge =>
    edge.RiverClass == 1 && edge.CrossingType == "none");
Assert(movement.StepCost(tank, stream.From, stream.To, OrderPosture.Tactical) is not null,
    "Streams must be fordable by ordinary ground units.");

MapEdge majorRiver = data.Crossings.Values.First(edge =>
    edge.RiverClass == 3 && edge.CrossingType == "none");
Assert(movement.StepCost(tank, majorRiver.From, majorRiver.To, OrderPosture.Tactical) is null,
    "A non-amphibious tank must not cross an unbridged major river.");
Assert(movement.StepCost(amphibious, majorRiver.From, majorRiver.To, OrderPosture.Tactical) is not null,
    "An amphibious unit must be able to cross an unbridged major river.");

MapEdge bridge = data.Crossings.Values.First(edge => edge.CrossingType == "bridge");
Assert(movement.StepCost(tank, bridge.From, bridge.To, OrderPosture.Tactical) is not null,
    "A bridge must permit a non-amphibious unit to cross water.");

MapCell water = data.Cells.Cast<MapCell>().First(cell => cell.Terrain == "water");
Point? waterNeighbor = Neighbors(water.X, water.Y).FirstOrDefault(point =>
    point.X >= 0 && point.X < data.MapWidth && point.Y >= 0 && point.Y < data.MapHeight);
Assert(waterNeighbor is Point adjacent &&
       movement.StepCost(tank, adjacent, new Point(water.X, water.Y), OrderPosture.Tactical) is null,
    "Open-water terrain must be impassable to ordinary ground movement.");

var detour = data.Crossings.Values
    .Where(edge => edge.RiverClass >= 2 && edge.CrossingType == "none")
    .Select(edge => (Edge: edge, Route: planner.FindRoute(
        tank, edge.From, edge.To, OrderPosture.Tactical)))
    .FirstOrDefault(candidate => candidate.Route.Success);
Assert(detour.Route is not null && detour.Route.Success,
    "A* must find a legal route around at least one blocked river crossing.");
RouteResult detourRoute = detour.Route ?? throw new InvalidOperationException(
    "A* did not produce a detour route.");
Assert(!UsesEdge(detourRoute.Cells, detour.Edge.From, detour.Edge.To),
    "A* must not use an illegal major-river crossing.");

Point originalTankPosition = new(tank.X, tank.Y);
RouteResult longRoute = new[]
    {
        new Point(5, 5), new Point(data.MapWidth - 6, 5),
        new Point(5, data.MapHeight - 6),
        new Point(data.MapWidth - 6, data.MapHeight - 6)
    }
    .Select(goal => planner.FindRoute(tank, originalTankPosition, goal, OrderPosture.Tactical))
    .Where(candidate => candidate.Success)
    .OrderByDescending(candidate => candidate.Cost)
    .First();
Assert(longRoute.Cost > tank.MovePoints * 10,
    "The execution test route must exceed one turn of movement.");
PlannedMoveOrder longOrder = CreateOrder(tank, OrderPosture.Tactical, longRoute);
var turn = new WegoMovementExecution(data, [longOrder]);
turn.Advance(turn.TurnDurationSeconds);
Assert(turn.IsComplete, "A WEGO movement turn must finish at its time boundary.");
turn.FinalizeTurn();
Point partialPosition = new(tank.X, tank.Y);
Assert(partialPosition != originalTankPosition,
    "A unit must move during WEGO execution when it has a legal route.");
Assert(partialPosition != longRoute.Cells[^1] &&
       longOrder.ExecutionState == MoveOrderExecutionState.Partial,
    "A route longer than the movement allowance must remain partial.");
Assert(longOrder.Route[0] == partialPosition && longOrder.CarriedStepCost >= 0,
    "A partial route must be retained from the unit's new location.");

var resumedTurn = new WegoMovementExecution(data, [longOrder]);
Assert(Math.Abs(resumedTurn.Units[0].StepProgress - longOrder.CarriedStepCost) < 0.001,
    "Partial progress toward the next hex must carry into the following turn.");

ScenarioUnit firstTrafficUnit = data.Units.First(unit => unit.Key == "team_alpha_cav_1");
ScenarioUnit secondTrafficUnit = data.Units.First(unit => unit.Key == "team_alpha_cav_2");
MovementModel trafficMovement = new(data);
(Point Center, Point First, Point Second) traffic = FindTrafficTestCells(
    data, trafficMovement, firstTrafficUnit, secondTrafficUnit);
firstTrafficUnit.X = traffic.First.X;
firstTrafficUnit.Y = traffic.First.Y;
secondTrafficUnit.X = traffic.Second.X;
secondTrafficUnit.Y = traffic.Second.Y;
PlannedMoveOrder firstTrafficOrder = CreateOrder(firstTrafficUnit, OrderPosture.Quick,
    new RouteResult(true, [traffic.First, traffic.Center], 1));
PlannedMoveOrder secondTrafficOrder = CreateOrder(secondTrafficUnit, OrderPosture.Quick,
    new RouteResult(true, [traffic.Second, traffic.Center], 1));
var trafficTurn = new WegoMovementExecution(data, [firstTrafficOrder, secondTrafficOrder]);
trafficTurn.Advance(trafficTurn.TurnDurationSeconds);
trafficTurn.FinalizeTurn();
int centerOccupants = new[] { firstTrafficUnit, secondTrafficUnit }.Count(unit =>
    unit.X == traffic.Center.X && unit.Y == traffic.Center.Y);
Assert(centerOccupants == 1,
    "Simultaneous movement must not stack two friendly units in the same hex.");
Assert(new[] { firstTrafficOrder, secondTrafficOrder }.Count(order =>
           order.ExecutionState == MoveOrderExecutionState.Complete) == 1 &&
       new[] { firstTrafficOrder, secondTrafficOrder }.Count(order =>
           order.ExecutionState == MoveOrderExecutionState.Partial) == 1,
    "Traffic contention must complete one order and preserve the waiting order for the next turn.");

(Point BypassStart, Point Blocked, Point BypassGoal) bypass = FindBypassTestCells(
    data, trafficMovement, planner, firstTrafficUnit, secondTrafficUnit);
firstTrafficUnit.X = bypass.BypassStart.X;
firstTrafficUnit.Y = bypass.BypassStart.Y;
secondTrafficUnit.X = bypass.Blocked.X;
secondTrafficUnit.Y = bypass.Blocked.Y;
PlannedMoveOrder bypassOrder = CreateOrder(firstTrafficUnit, OrderPosture.Quick,
    new RouteResult(true, [bypass.BypassStart, bypass.Blocked, bypass.BypassGoal], 2));
var bypassTurn = new WegoMovementExecution(data, [bypassOrder]);
bypassTurn.Advance(bypassTurn.TurnDurationSeconds);
Assert(bypassTurn.Units[0].ReroutedForTraffic,
    "A unit held by stationary friendly traffic must find a legal bypass when one exists.");
Assert(firstTrafficUnit.X != bypass.Blocked.X || firstTrafficUnit.Y != bypass.Blocked.Y,
    "A traffic bypass must not enter the occupied hex.");

Console.WriteLine($"Movement tests passed: {data.Crossings.Count} crossing edges, " +
                  $"legal detour cost {detourRoute.Cost / 10f:0.0}; " +
                  "WEGO partial, contention, and traffic-bypass checks passed.");
return;

static PlannedMoveOrder CreateOrder(
    ScenarioUnit unit, OrderPosture posture, RouteResult route)
{
    var order = new PlannedMoveOrder
    {
        Unit = unit,
        Posture = posture,
        Confirmed = true,
        TotalCost = route.Cost,
        ExecutionState = MoveOrderExecutionState.Planned
    };
    order.Route.AddRange(route.Cells);
    order.Waypoints.Add(route.Cells[^1]);
    return order;
}

static (Point Center, Point First, Point Second) FindTrafficTestCells(
    GameData data, MovementModel movement, ScenarioUnit firstUnit, ScenarioUnit secondUnit)
{
    HashSet<Point> occupied = data.Units
        .Where(unit => unit != firstUnit && unit != secondUnit)
        .Select(unit => new Point(unit.X, unit.Y))
        .ToHashSet();
    for (int y = 2; y < data.MapHeight - 2; y++)
    for (int x = 2; x < data.MapWidth - 2; x++)
    {
        Point center = new(x, y);
        if (occupied.Contains(center)) continue;
        Point[] approaches = Neighbors(x, y)
            .Where(point => !occupied.Contains(point))
            .Where(point => movement.StepCost(firstUnit, point, center, OrderPosture.Quick) is not null)
            .Where(point => movement.StepCost(secondUnit, point, center, OrderPosture.Quick) is not null)
            .Take(2).ToArray();
        if (approaches.Length == 2) return (center, approaches[0], approaches[1]);
    }
    throw new InvalidOperationException("No legal traffic test hex was found.");
}

static (Point Start, Point Blocked, Point Goal) FindBypassTestCells(
    GameData data, MovementModel movement, AStarRoutePlanner planner,
    ScenarioUnit mover, ScenarioUnit blocker)
{
    HashSet<Point> occupied = data.Units
        .Where(unit => unit != mover && unit != blocker)
        .Select(unit => new Point(unit.X, unit.Y))
        .ToHashSet();
    for (int y = 3; y < data.MapHeight - 3; y++)
    for (int x = 3; x < data.MapWidth - 3; x++)
    {
        Point blocked = new(x, y);
        if (occupied.Contains(blocked)) continue;
        Point[] neighbors = Neighbors(x, y).Where(point => !occupied.Contains(point)).ToArray();
        foreach (Point start in neighbors)
        foreach (Point goal in neighbors.Where(point => point != start))
        {
            if (movement.StepCost(mover, start, blocked, OrderPosture.Quick) is null ||
                movement.StepCost(mover, blocked, goal, OrderPosture.Quick) is null)
                continue;
            RouteResult alternate = planner.FindRoute(mover, start, goal,
                OrderPosture.Quick, new HashSet<Point> { blocked });
            if (alternate.Success && alternate.Cells.Count > 1)
                return (start, blocked, goal);
        }
    }
    throw new InvalidOperationException("No legal traffic bypass test route was found.");
}

static IEnumerable<Point> Neighbors(int x, int y)
{
    int up = (x & 1) == 0 ? -1 : 0;
    int down = (x & 1) == 0 ? 0 : 1;
    yield return new Point(x, y - 1);
    yield return new Point(x + 1, y + up);
    yield return new Point(x + 1, y + down);
    yield return new Point(x, y + 1);
    yield return new Point(x - 1, y + down);
    yield return new Point(x - 1, y + up);
}

static bool UsesEdge(IReadOnlyList<Point> route, Point first, Point second)
{
    HexEdgeKey key = HexEdgeKey.Create(first, second);
    return route.Zip(route.Skip(1)).Any(pair => HexEdgeKey.Create(pair.First, pair.Second) == key);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
