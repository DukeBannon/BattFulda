namespace BattalionFulda;

internal static class StackingRules
{
    public const int Capacity = 4;
    // Provisional footprint, not a historical formation-strength rating.
    public static int Footprint(ScenarioUnit unit) =>
        unit.Category.Equals("command", StringComparison.OrdinalIgnoreCase) ||
        unit.Category.Equals("headquarters", StringComparison.OrdinalIgnoreCase) ||
        unit.Category.Equals("anti_tank", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
}

internal sealed class UnitMovementExecution
{
    private readonly MovementModel movement;
    private readonly double turnDurationSeconds;
    private const double BypassDelaySeconds = 45;

    public UnitMovementExecution(
        PlannedMoveOrder order, MovementModel movement, double turnDurationSeconds)
    {
        Order = order;
        this.movement = movement;
        this.turnDurationSeconds = turnDurationSeconds;
        NextCellIndex = Math.Min(1, order.Route.Count);
        StepProgress = order.CarriedStepCost;
        order.ExecutionState = MoveOrderExecutionState.Executing;
        order.LastTurnCost = 0;
        order.ExecutionNote = null;
    }

    public PlannedMoveOrder Order { get; }
    public int NextCellIndex { get; private set; }
    public double StepProgress { get; private set; }
    public bool WaitingForTraffic { get; private set; }
    public double TrafficHoldSeconds { get; private set; }
    public ScenarioUnit? BlockingUnit { get; private set; }
    public bool ReroutedForTraffic { get; private set; }
    public bool IsFinalized { get; private set; }
    public bool RouteComplete => IsFinalized || Order.Unit.IsDestroyed ||
                                 NextCellIndex >= Order.Route.Count;
    public Point Current => new(Order.Unit.X, Order.Unit.Y);
    public Point Next => RouteComplete ? Current : Order.Route[NextCellIndex];
    public double CostRatePerGameSecond => Order.Unit.MovePoints * 10.0 /
        Math.Max(1, turnDurationSeconds);

    public int CurrentStepCost => RouteComplete ? 0 :
        movement.StepCost(Order.Unit, Current, Next, Order.Posture) ?? int.MaxValue;

    public float VisualProgress => RouteComplete || CurrentStepCost == int.MaxValue
        ? 0f
        : (float)Math.Clamp(StepProgress / CurrentStepCost, 0,
            WaitingForTraffic || StepProgress >= CurrentStepCost ? 0.82 : 1.0);

    public void Accrue(double gameSeconds)
    {
        if (RouteComplete || CurrentStepCost == int.MaxValue) return;
        double before = StepProgress;
        StepProgress = Math.Min(CurrentStepCost, StepProgress + CostRatePerGameSecond * gameSeconds);
        Order.LastTurnCost += StepProgress - before;
        WaitingForTraffic = false;
    }

    public bool ReadyToEnterNextCell => !RouteComplete && StepProgress >= CurrentStepCost;

    public void CompleteStep()
    {
        int stepCost = CurrentStepCost;
        if (stepCost == int.MaxValue) return;
        StepProgress = Math.Max(0, StepProgress - stepCost);
        Order.Unit.X = Next.X;
        Order.Unit.Y = Next.Y;
        NextCellIndex++;
        if (Order.Waypoints.Count > 0 && Order.Waypoints[0] == Current)
            Order.Waypoints.RemoveAt(0);
        WaitingForTraffic = false;
        TrafficHoldSeconds = 0;
        BlockingUnit = null;
        Order.ExecutionNote = ReroutedForTraffic ? "TRAFFIC BYPASS" : null;
    }

    public void HoldForTraffic(ScenarioUnit? blocker, double gameSeconds)
    {
        StepProgress = Math.Min(StepProgress, CurrentStepCost);
        WaitingForTraffic = true;
        TrafficHoldSeconds += gameSeconds;
        BlockingUnit = blocker;
        Order.ExecutionNote = blocker is null
            ? "YIELDING TO TRAFFIC"
            : blocker.Faction.Equals(Order.Unit.Faction, StringComparison.OrdinalIgnoreCase)
                ? $"HELD BY {blocker.Name.ToUpperInvariant()}"
                : "HELD BY OPPOSING UNIT";
    }

    public bool TryTrafficBypass(
        AStarRoutePlanner planner, IReadOnlySet<Point> occupiedCells)
    {
        if (!WaitingForTraffic || TrafficHoldSeconds < BypassDelaySeconds || RouteComplete)
            return false;

        // Rejoin locally, never discard the player's waypoints or re-plan to
        // the final destination. A blocked waypoint remains a traffic hold.
        int lastRejoin = Math.Min(Order.Route.Count - 1, NextCellIndex + 4);
        if (Order.Waypoints.Count > 0)
        {
            int waypointIndex = Order.Route.IndexOf(Order.Waypoints[0], NextCellIndex);
            if (waypointIndex >= 0) lastRejoin = Math.Min(lastRejoin, waypointIndex);
        }
        RouteResult? bypass = null;
        int rejoinIndex = -1;
        for (int index = NextCellIndex + 1; index <= lastRejoin; index++)
        {
            Point rejoin = Order.Route[index];
            if (occupiedCells.Contains(rejoin)) continue;
            RouteResult candidate = planner.FindRoute(
                Order.Unit, Current, rejoin, Order.Posture, occupiedCells);
            int originalSteps = index - NextCellIndex + 1;
            if (!candidate.Success || candidate.Cells.Count < 2 || candidate.Cells[1] == Next ||
                candidate.Cells.Count - 1 > originalSteps + 3 ||
                candidate.Cells.Any(cell => LineOfSightModel.HexDistance(Current, cell) > 4))
                continue;
            bypass = candidate;
            rejoinIndex = index;
            break;
        }
        if (bypass is null) return false;

        Point[] preservedTail = Order.Route.Skip(rejoinIndex + 1).ToArray();
        Order.Route.Clear();
        Order.Route.AddRange(bypass.Cells);
        Order.Route.AddRange(preservedTail);
        Order.CarriedStepCost = 0;
        StepProgress = 0;
        NextCellIndex = 1;
        Order.TotalCost = CalculateRemainingCost();
        WaitingForTraffic = false;
        TrafficHoldSeconds = 0;
        BlockingUnit = null;
        ReroutedForTraffic = true;
        Order.ExecutionNote = "LOCAL TRAFFIC BYPASS — WAYPOINTS PRESERVED";
        return true;
    }

    public void FinalizeTurn()
    {
        Point current = Current;
        if (RouteComplete)
        {
            Order.Route.Clear();
            Order.Route.Add(current);
            Order.Waypoints.Clear();
            Order.TotalCost = 0;
            Order.CarriedStepCost = 0;
            Order.ExecutionState = MoveOrderExecutionState.Complete;
            if (Order.Unit.IsDestroyed) Order.ExecutionNote = "DESTROYED";
            IsFinalized = true;
            return;
        }

        List<Point> remaining = Order.Route.Skip(Math.Max(0, NextCellIndex - 1)).ToList();
        Order.Route.Clear();
        Order.Route.AddRange(remaining);
        Order.Waypoints.RemoveAll(waypoint =>
            waypoint == current || !Order.Route.Contains(waypoint));
        Order.CarriedStepCost = StepProgress;
        Order.TotalCost = CalculateRemainingCost();
        Order.ExecutionState = MoveOrderExecutionState.Partial;
        IsFinalized = true;
    }

    private int CalculateRemainingCost()
    {
        if (Order.Route.Count < 2) return 0;
        int total = 0;
        for (int index = 1; index < Order.Route.Count; index++)
        {
            int? cost = movement.StepCost(
                Order.Unit, Order.Route[index - 1], Order.Route[index], Order.Posture);
            if (cost is null) return 0;
            total += cost.Value;
        }
        return Math.Max(0, (int)Math.Ceiling(total - StepProgress));
    }
}

internal sealed class WegoMovementExecution
{
    private const double ResolutionQuantumSeconds = 2.0;
    private readonly GameData data;
    private readonly List<UnitMovementExecution> units;
    private readonly AStarRoutePlanner routePlanner;

    public WegoMovementExecution(GameData data, IEnumerable<PlannedMoveOrder> orders)
    {
        this.data = data;
        routePlanner = new AStarRoutePlanner(data);
        TurnDurationSeconds = data.TurnMinutes * 60.0;
        var movement = new MovementModel(data);
        units = orders.Where(order => !order.Unit.IsDestroyed && order.Confirmed && order.Route.Count > 1)
            .OrderBy(order => order.Unit.Id)
            .Select(order => new UnitMovementExecution(order, movement, TurnDurationSeconds))
            .ToList();
    }

    public double TurnDurationSeconds { get; }
    public IReadOnlyList<UnitMovementExecution> Units => units;
    public double ElapsedGameSeconds { get; private set; }
    public bool IsComplete => ElapsedGameSeconds >= TurnDurationSeconds ||
                              units.All(unit => unit.RouteComplete);
    public int MovingCount => units.Count(unit => !unit.RouteComplete);

    public UnitMovementExecution? ForUnit(ScenarioUnit unit) =>
        units.FirstOrDefault(execution => ReferenceEquals(execution.Order.Unit, unit));

    public void Advance(double gameSeconds)
    {
        double remaining = Math.Min(gameSeconds, TurnDurationSeconds - ElapsedGameSeconds);
        while (remaining > 0 && !units.All(unit => unit.RouteComplete))
        {
            double quantum = Math.Min(ResolutionQuantumSeconds, remaining);
            foreach (UnitMovementExecution unit in units) unit.Accrue(quantum);
            ResolveCellEntries(quantum);
            ElapsedGameSeconds += quantum;
            remaining -= quantum;
        }

        if (remaining > 0) ElapsedGameSeconds += remaining;
    }

    public void FinalizeTurn()
    {
        foreach (UnitMovementExecution unit in units) unit.FinalizeTurn();
    }

    private void ResolveCellEntries(double quantumSeconds)
    {
        UnitMovementExecution[] candidates = units
            .Where(unit => unit.ReadyToEnterNextCell)
            .ToArray();
        if (candidates.Length == 0) return;

        foreach (UnitMovementExecution candidate in candidates.OrderBy(unit => unit.Order.Unit.Id))
        {
            ScenarioUnit[] occupants = data.Units.Where(unit =>
                !unit.IsDestroyed && !ReferenceEquals(unit, candidate.Order.Unit) &&
                unit.X == candidate.Next.X && unit.Y == candidate.Next.Y).ToArray();
            ScenarioUnit? enemy = occupants.FirstOrDefault(unit =>
                !unit.Faction.Equals(candidate.Order.Unit.Faction, StringComparison.OrdinalIgnoreCase));
            if (enemy is null && occupants.Sum(StackingRules.Footprint) +
                StackingRules.Footprint(candidate.Order.Unit) <= StackingRules.Capacity)
                candidate.CompleteStep();
            else
            {
                candidate.HoldForTraffic(enemy ?? occupants.FirstOrDefault(), quantumSeconds);
                if (enemy is null) candidate.Order.ExecutionNote = "TRAFFIC HOLD — FRIENDLY HEX AT CAPACITY";
            }
        }

        foreach (UnitMovementExecution candidate in candidates.Where(unit => unit.WaitingForTraffic))
        {
            HashSet<Point> occupied = data.Units
                .Where(unit => !unit.IsDestroyed && !ReferenceEquals(unit, candidate.Order.Unit) &&
                    unit.Faction.Equals(candidate.Order.Unit.Faction, StringComparison.OrdinalIgnoreCase))
                .GroupBy(unit => new Point(unit.X, unit.Y))
                .Where(group => group.Sum(StackingRules.Footprint) +
                    StackingRules.Footprint(candidate.Order.Unit) > StackingRules.Capacity)
                .Select(group => group.Key)
                .ToHashSet();
            // Only the directly encountered enemy is known to this traffic resolver.
            // Never use unseen enemy positions to choose a bypass.
            if (candidate.BlockingUnit is ScenarioUnit blocker &&
                !blocker.Faction.Equals(candidate.Order.Unit.Faction, StringComparison.OrdinalIgnoreCase))
                occupied.Add(candidate.Next);
            candidate.TryTrafficBypass(routePlanner, occupied);
        }
    }
}
