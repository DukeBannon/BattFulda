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
Assert(centerOccupants == 2,
    "Two friendly platoons must be allowed to coexist in one hex.");
Assert(new[] { firstTrafficOrder, secondTrafficOrder }.Count(order =>
           order.ExecutionState == MoveOrderExecutionState.Complete) == 2 &&
       new[] { firstTrafficOrder, secondTrafficOrder }.Count(order =>
           order.ExecutionState == MoveOrderExecutionState.Partial) == 0,
    "Both orders must complete when the friendly stack fits.");

(Point BypassStart, Point Blocked, Point BypassGoal) bypass = FindBypassTestCells(
    data, trafficMovement, planner, firstTrafficUnit, secondTrafficUnit);
firstTrafficUnit.X = bypass.BypassStart.X;
firstTrafficUnit.Y = bypass.BypassStart.Y;
secondTrafficUnit.X = bypass.Blocked.X;
secondTrafficUnit.Y = bypass.Blocked.Y;
ScenarioUnit extraBlocker = data.Units.First(unit => unit.Faction == firstTrafficUnit.Faction &&
    unit.Id != firstTrafficUnit.Id && unit.Id != secondTrafficUnit.Id && StackingRules.Footprint(unit) == 2);
Point extraSavedPosition = new(extraBlocker.X, extraBlocker.Y);
extraBlocker.X = bypass.Blocked.X;
extraBlocker.Y = bypass.Blocked.Y;
PlannedMoveOrder bypassOrder = CreateOrder(firstTrafficUnit, OrderPosture.Quick,
    new RouteResult(true, [bypass.BypassStart, bypass.Blocked, bypass.BypassGoal], 2));
var bypassTurn = new WegoMovementExecution(data, [bypassOrder]);
bypassTurn.Advance(bypassTurn.TurnDurationSeconds);
Assert(bypassTurn.Units[0].ReroutedForTraffic,
    "A unit held by stationary friendly traffic must find a legal bypass when one exists.");
Assert(firstTrafficUnit.X != bypass.Blocked.X || firstTrafficUnit.Y != bypass.Blocked.Y,
    "A traffic bypass must not enter the occupied hex.");
extraBlocker.X = extraSavedPosition.X;
extraBlocker.Y = extraSavedPosition.Y;

firstTrafficUnit.X = bypass.BypassStart.X;
firstTrafficUnit.Y = bypass.BypassStart.Y;
var passageOrder = CreateOrder(firstTrafficUnit, OrderPosture.Quick,
    new RouteResult(true, [bypass.BypassStart, bypass.Blocked, bypass.BypassGoal], 2));
var passageTurn = new WegoMovementExecution(data, [passageOrder]);
passageTurn.Advance(passageTurn.TurnDurationSeconds);
Assert(passageTurn.Units[0].RouteComplete && !passageTurn.Units[0].ReroutedForTraffic,
    "A platoon must pass through one friendly platoon without a bypass.");
ScenarioUnit hiddenBlocker = data.Units.First(unit => unit.Faction != firstTrafficUnit.Faction);
Point hiddenSavedPosition = new(hiddenBlocker.X, hiddenBlocker.Y);
hiddenBlocker.X = bypass.Blocked.X;
hiddenBlocker.Y = bypass.Blocked.Y;
firstTrafficUnit.X = bypass.BypassStart.X;
firstTrafficUnit.Y = bypass.BypassStart.Y;
Assert(planner.FindRoute(firstTrafficUnit, bypass.BypassStart, bypass.Blocked, OrderPosture.Quick).Success,
    "Geometric planning must not disclose hidden enemy occupancy.");
Assert(!planner.FindRoute(firstTrafficUnit, bypass.BypassStart, bypass.Blocked,
    OrderPosture.Quick, new HashSet<Point> { bypass.Blocked }).Success,
    "Known enemy destinations must be excluded when explicitly supplied.");
var encounterOrder = CreateOrder(firstTrafficUnit, OrderPosture.Quick,
    new RouteResult(true, [bypass.BypassStart, bypass.Blocked], 1));
var encounterTurn = new WegoMovementExecution(data, [encounterOrder]);
encounterTurn.Advance(encounterTurn.TurnDurationSeconds);
Assert(firstTrafficUnit.X == bypass.BypassStart.X && firstTrafficUnit.Y == bypass.BypassStart.Y &&
    encounterOrder.ExecutionNote == "HELD BY OPPOSING UNIT",
    "An encountered enemy must block entry with anonymous feedback.");
hiddenBlocker.X = hiddenSavedPosition.X;
hiddenBlocker.Y = hiddenSavedPosition.Y;

firstTrafficUnit.X = bypass.BypassStart.X;
firstTrafficUnit.Y = bypass.BypassStart.Y;
RouteResult continuation = new[] { new Point(5, 5), new Point(80, 65) }
    .Select(goal => planner.FindRoute(firstTrafficUnit, bypass.BypassGoal, goal, OrderPosture.Quick))
    .First(route => route.Success && route.Cells.Count > 3);
PlannedMoveOrder preservedOrder = CreateOrder(firstTrafficUnit, OrderPosture.Quick,
    new RouteResult(true,
        new[] { bypass.BypassStart, bypass.Blocked, bypass.BypassGoal }
            .Concat(continuation.Cells.Skip(1)).ToArray(), 1));
preservedOrder.Waypoints.Clear();
preservedOrder.Waypoints.Add(bypass.BypassGoal);
preservedOrder.Waypoints.Add(continuation.Cells[^1]);
Point[] originalWaypoints = preservedOrder.Waypoints.ToArray();
var localBypass = new UnitMovementExecution(preservedOrder, trafficMovement, data.TurnMinutes * 60);
localBypass.HoldForTraffic(secondTrafficUnit, 46);
Assert(localBypass.TryTrafficBypass(planner, new HashSet<Point> { bypass.Blocked }),
    "A local bypass should rejoin the original route after stationary traffic.");
Assert(preservedOrder.Waypoints.SequenceEqual(originalWaypoints) &&
       preservedOrder.Route.TakeLast(continuation.Cells.Count - 1)
           .SequenceEqual(continuation.Cells.Skip(1)),
    "Traffic bypass must preserve intermediate waypoints and the original route beyond the rejoin.");
PlannedMoveOrder blockedWaypointOrder = CreateOrder(firstTrafficUnit, OrderPosture.Quick,
    new RouteResult(true, [bypass.BypassStart, bypass.Blocked, bypass.BypassGoal], 1));
blockedWaypointOrder.Waypoints.Insert(0, bypass.Blocked);
var blockedWaypointExecution = new UnitMovementExecution(
    blockedWaypointOrder, trafficMovement, data.TurnMinutes * 60);
blockedWaypointExecution.HoldForTraffic(secondTrafficUnit, 46);
Assert(!blockedWaypointExecution.TryTrafficBypass(planner, new HashSet<Point> { bypass.Blocked }) &&
       blockedWaypointOrder.Waypoints[0] == bypass.Blocked,
    "An occupied player waypoint must remain a hold, not be skipped by a bypass.");

var los = new LineOfSightModel(data);
for (int x = 0; x < data.MapWidth; x++)
for (int y = 0; y < data.MapHeight; y++)
{
    Point source = new(x, y);
    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(data.MapWidth - 1, x + 1); nx++)
    for (int ny = Math.Max(0, y - 1); ny <= Math.Min(data.MapHeight - 1, y + 1); ny++)
    {
        Point neighbor = new(nx, ny);
        if (LineOfSightModel.HexDistance(source, neighbor) != 1) continue;
        Assert(los.Trace(source, neighbor).Quality != LineOfSightQuality.Blocked,
            "Adjacent hexes must be visible regardless of elevation or concealing terrain.");
    }
}
Point observerCell = new(10, 10);
Point targetCell = new(10, 13);
Point[] controlledCells = [observerCell, new Point(10, 11), new Point(10, 12), targetCell];
MapCell[] originalCells = controlledCells.Select(point => data.Cells[point.X, point.Y]).ToArray();
for (int index = 0; index < controlledCells.Length; index++)
{
    Point point = controlledCells[index];
    data.Cells[point.X, point.Y] = new MapCell(
        point.X, point.Y, 300, "clear", 0, 0, 0, 0, 0, false);
}
Assert(los.Trace(observerCell, targetCell).Quality == LineOfSightQuality.Clear,
    "Level clear terrain must provide a clear line of sight.");

Point urbanCell = controlledCells[1];
data.Cells[urbanCell.X, urbanCell.Y] = data.Cells[urbanCell.X, urbanCell.Y] with { Terrain = "urban" };
Assert(los.Trace(observerCell, urbanCell).Quality == LineOfSightQuality.Obscured,
    "The first urban hex must be visible but obscured.");
data.Cells[observerCell.X, observerCell.Y] = data.Cells[observerCell.X, observerCell.Y] with { Elevation = 500 };
data.Cells[controlledCells[2].X, controlledCells[2].Y] =
    data.Cells[controlledCells[2].X, controlledCells[2].Y] with { Terrain = "urban" };
LineOfSightQuality[,] townVisibility = los.CalculateVisibility(observerCell);
Assert(townVisibility[urbanCell.X, urbanCell.Y] == LineOfSightQuality.Obscured &&
    townVisibility[controlledCells[2].X, controlledCells[2].Y] == LineOfSightQuality.Blocked &&
    townVisibility[targetCell.X, targetCell.Y] == LineOfSightQuality.Blocked &&
    los.Trace(observerCell, targetCell).Reason == "BLOCKED BY BUILDINGS" &&
    los.Trace(targetCell, observerCell).Quality == LineOfSightQuality.Blocked,
    "Even an elevated observer must not see through intervening urban cells in either direction.");
foreach (Point point in controlledCells)
    data.Cells[point.X, point.Y] = data.Cells[point.X, point.Y] with { Terrain = "clear", Elevation = 300 };

Point woodsCell = controlledCells[1];
data.Cells[woodsCell.X, woodsCell.Y] = new MapCell(
    woodsCell.X, woodsCell.Y, 300, "woods", 0, 0, 0, 0, 0, false);
LineOfSightResult blockedForward = los.Trace(observerCell, targetCell);
LineOfSightResult blockedReverse = los.Trace(targetCell, observerCell);
Assert(blockedForward.Quality == LineOfSightQuality.Blocked &&
       blockedReverse.Quality == LineOfSightQuality.Blocked &&
       blockedForward.BlockingCell == blockedReverse.BlockingCell,
    "Woods blocking must be symmetric in both LOS directions.");
Assert(los.Trace(observerCell, woodsCell).Quality == LineOfSightQuality.Obscured,
    "The first wooded hex must be visible but obscured.");
data.Cells[observerCell.X, observerCell.Y] = data.Cells[observerCell.X, observerCell.Y] with { Elevation = 500 };
data.Cells[controlledCells[2].X, controlledCells[2].Y] =
    data.Cells[controlledCells[2].X, controlledCells[2].Y] with { Terrain = "woods" };
LineOfSightQuality[,] forestVisibility = los.CalculateVisibility(observerCell);
Assert(forestVisibility[woodsCell.X, woodsCell.Y] == LineOfSightQuality.Obscured &&
       forestVisibility[controlledCells[2].X, controlledCells[2].Y] == LineOfSightQuality.Blocked &&
       forestVisibility[targetCell.X, targetCell.Y] == LineOfSightQuality.Blocked &&
       los.Trace(targetCell, observerCell).Quality == LineOfSightQuality.Blocked,
    "A high observer must not see deeper into or through woods, in either direction.");
data.Cells[observerCell.X, observerCell.Y] = data.Cells[observerCell.X, observerCell.Y] with { Elevation = 300 };
data.Cells[controlledCells[2].X, controlledCells[2].Y] =
    data.Cells[controlledCells[2].X, controlledCells[2].Y] with { Terrain = "clear" };

data.Cells[woodsCell.X, woodsCell.Y] = new MapCell(
    woodsCell.X, woodsCell.Y, 410, "clear", 0, 0, 0, 0, 0, false);
Assert(los.Trace(observerCell, targetCell).Reason == "BLOCKED BY ELEVATION",
    "An intervening ridge must block line of sight.");

foreach ((Point point, MapCell original) in controlledCells.Zip(originalCells))
    data.Cells[point.X, point.Y] = new MapCell(
        point.X, point.Y, 300, "clear", 0, 0, 0, 0, 0, false);
data.Cells[targetCell.X, targetCell.Y] = new MapCell(
    targetCell.X, targetCell.Y, 300, "cultivated", 0, 0, 0, 0, 0, false);
Assert(los.Trace(observerCell, targetCell).Quality == LineOfSightQuality.Obscured,
    "Cultivated target terrain must obscure rather than block line of sight.");
LineOfSightQuality[,] visibility = los.CalculateVisibility(observerCell);
Assert(visibility[observerCell.X, observerCell.Y] == LineOfSightQuality.Clear &&
       visibility[targetCell.X, targetCell.Y] == LineOfSightQuality.Obscured,
    "Visibility overlay must include the observer and obscured visible hexes.");
for (int x = 0; x < data.MapWidth; x++)
for (int y = 0; y < data.MapHeight; y++)
    Assert(visibility[x, y] == los.Trace(observerCell, new Point(x, y)).Quality,
        "Every overlay hex must agree with combat LOS, without a weapon-range cutoff.");
data.Cells[targetCell.X, targetCell.Y] = new MapCell(
    targetCell.X, targetCell.Y, 300, "clear", 0, 0, 0, 0, 0, false);

ScenarioUnit enemyTank = data.Units.First(unit =>
    !unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase) &&
    unit.Category == "tank");
Point savedTankPosition = new(tank.X, tank.Y);
Point savedEnemyPosition = new(enemyTank.X, enemyTank.Y);
int savedEnemyStrength = enemyTank.Strength;
int savedEnemySuppression = enemyTank.Suppression;
int savedEnemyReadiness = enemyTank.Readiness;
int savedEnemyMorale = enemyTank.Morale;
tank.X = observerCell.X;
tank.Y = observerCell.Y;
enemyTank.X = targetCell.X;
enemyTank.Y = targetCell.Y;
var directFire = new DirectFireModel(data);
FireValidation legalShot = directFire.Validate(tank, enemyTank);
Assert(legalShot.CanFire && legalShot.LineOfSight.Range == 3,
    "A clear enemy target inside weapon range must accept a direct-fire order.");
var fireOrder = new PlannedFireOrder
{
    Unit = tank,
    Target = enemyTank,
    LineOfSight = legalShot.LineOfSight
};
var combatTurn = new WegoCombatExecution(data, [fireOrder], 1);
combatTurn.AdvanceTo(data.TurnMinutes * 60 * 0.2 - 1);
Assert(!combatTurn.IsResolved,
    "WEGO fire must wait for its scheduled resolution time.");
combatTurn.AdvanceTo(data.TurnMinutes * 60 * 0.2);
Assert(combatTurn.IsResolved && fireOrder.State == FireOrderState.Complete &&
       enemyTank.Suppression > savedEnemySuppression && enemyTank.Strength <= savedEnemyStrength,
    "A legal WEGO shot must resolve and apply suppression and possible losses.");

// Isolate the friendly observers to exercise side knowledge, not live-unit data.
var friendlyPositions = data.Units.Where(unit => unit.Faction == tank.Faction)
    .Select(unit => (Unit: unit, Position: new Point(unit.X, unit.Y))).ToArray();
foreach (var entry in friendlyPositions)
{
    entry.Unit.X = observerCell.X;
    entry.Unit.Y = observerCell.Y;
}
enemyTank.Strength = savedEnemyStrength;
var contacts = new SideContactModel(data, tank.Faction);
var silent = new HashSet<int>();
contacts.Update(0, silent, silent);
ContactReport known = contacts.Reports.Single(report => report.Name == enemyTank.Name);
Assert(contacts.CanTarget(enemyTank), "Current visual identification must allow targeting.");
Assert(known.IsCurrent && known.Quality == ContactQuality.Identified &&
       known.ReportedCell == targetCell,
    "An identified contact must reflect an actual visual observation.");
data.Cells[woodsCell.X, woodsCell.Y] = data.Cells[woodsCell.X, woodsCell.Y] with { Terrain = "woods" };
enemyTank.Y = controlledCells[2].Y;
contacts.Update(10, silent, silent);
ContactReport stale = contacts.Reports.Single(report => report.ContactId == known.ContactId);
Assert(!contacts.CanTarget(enemyTank), "A stale sighting must not authorize targeted fire.");
Assert(!stale.IsCurrent && stale.ReportedCell == targetCell && stale.LastObservedSeconds == 0,
    "Hidden movement must not update a last-known contact.");
contacts.Update(20, new HashSet<int> { enemyTank.Id }, silent);
ContactReport cue = contacts.Reports.Single(report => report.ContactId == known.ContactId);
Assert(cue.IsCurrent && cue.Quality == ContactQuality.Uncertain &&
       cue.Name is null && cue.Category is null &&
       cue.ReportedCell != new Point(enemyTank.X, enemyTank.Y),
    "A movement cue through woods must reveal only an approximate anonymous report.");
Assert(!contacts.CanTarget(enemyTank), "An uncertain cue must not authorize targeted fire.");
var isolatedSide = new SideContactModel(data, "Warsaw Pact");
Assert(isolatedSide.Reports.Count == 0,
    "One side must never inherit the other side's contact reports.");
contacts.Update(30, silent, new HashSet<int> { enemyTank.Id });
Assert(contacts.Reports.Single(report => report.ContactId == known.ContactId).LastObservedSeconds == 30,
    "A nearby firing event must refresh an anonymous cue without optical LOS.");
int strengthBeforeHold = enemyTank.Strength;
var heldFire = new WegoCombatExecution(data, [fireOrder], 2, (_, target) => contacts.CanTarget(target));
heldFire.AdvanceTo(data.TurnMinutes * 60);
Assert(!heldFire.Results.Single().Fired && enemyTank.Strength == strengthBeforeHold &&
       fireOrder.State == FireOrderState.Invalid,
    "WEGO must hold fire when the target is only an uncertain contact.");
contacts.Update(631, silent, silent);
Assert(contacts.Reports.All(report => report.ContactId != known.ContactId),
    "Unobserved contacts must expire after the retention window.");
var openContacts = new SideContactModel(data, tank.Faction, VisibilityDifficulty.Open);
openContacts.Update(0, silent, silent);
Assert(openContacts.Reports.Any(report => report.Name == enemyTank.Name && report.IsCurrent),
    "Open difficulty must reveal enemies even without optical LOS.");
foreach (var entry in friendlyPositions)
{
    entry.Unit.X = entry.Position.X;
    entry.Unit.Y = entry.Position.Y;
}

tank.X = savedTankPosition.X;
tank.Y = savedTankPosition.Y;
enemyTank.X = savedEnemyPosition.X;
enemyTank.Y = savedEnemyPosition.Y;
enemyTank.Strength = savedEnemyStrength;
enemyTank.Suppression = savedEnemySuppression;
enemyTank.Readiness = savedEnemyReadiness;
enemyTank.Morale = savedEnemyMorale;
for (int index = 0; index < controlledCells.Length; index++)
{
    Point point = controlledCells[index];
    data.Cells[point.X, point.Y] = originalCells[index];
}

Console.WriteLine($"Movement tests passed: {data.Crossings.Count} crossing edges, " +
                  $"legal detour cost {detourRoute.Cost / 10f:0.0}; " +
                  "WEGO partial, contention, traffic-bypass, LOS, direct-fire, and contact-model checks passed.");
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
