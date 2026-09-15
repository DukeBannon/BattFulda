namespace BattalionFulda;

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
    public bool RouteComplete => IsFinalized || NextCellIndex >= Order.Route.Count;
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
            : $"HELD BY {blocker.Name.ToUpperInvariant()}";
    }

    public bool TryTrafficBypass(
        AStarRoutePlanner planner, IReadOnlySet<Point> occupiedCells)
    {
        if (!WaitingForTraffic || TrafficHoldSeconds < BypassDelaySeconds || RouteComplete)
            return false;

        Point destination = Order.Route[^1];
        RouteResult bypass = planner.FindRoute(
            Order.Unit, Current, destination, Order.Posture, occupiedCells);
        if (!bypass.Success || bypass.Cells.Count < 2 || bypass.Cells[1] == Next)
            return false;

        Order.Route.Clear();
        Order.Route.AddRange(bypass.Cells);
        Order.Waypoints.Clear();
        Order.Waypoints.Add(destination);
        Order.TotalCost = bypass.Cost;
        Order.CarriedStepCost = 0;
        StepProgress = 0;
        NextCellIndex = 1;
        WaitingForTraffic = false;
        TrafficHoldSeconds = 0;
        BlockingUnit = null;
        ReroutedForTraffic = true;
        Order.ExecutionNote = "REROUTED AROUND TRAFFIC";
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
        units = orders.Where(order => order.Confirmed && order.Route.Count > 1)
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

        var winners = candidates
            .GroupBy(unit => unit.Next)
            .Select(group => group.OrderBy(unit => unit.Order.Unit.Id).First())
            .ToHashSet();

        foreach (UnitMovementExecution candidate in candidates)
        {
            ScenarioUnit? occupant = data.Units.FirstOrDefault(unit =>
                !ReferenceEquals(unit, candidate.Order.Unit) &&
                unit.X == candidate.Next.X && unit.Y == candidate.Next.Y);
            if (winners.Contains(candidate) && occupant is null)
                candidate.CompleteStep();
            else
            {
                ScenarioUnit? contender = occupant ?? candidates.FirstOrDefault(other =>
                    !ReferenceEquals(other, candidate) && other.Next == candidate.Next)?.Order.Unit;
                candidate.HoldForTraffic(contender, quantumSeconds);
            }
        }

        foreach (UnitMovementExecution candidate in candidates.Where(unit => unit.WaitingForTraffic))
        {
            HashSet<Point> occupied = data.Units
                .Where(unit => !ReferenceEquals(unit, candidate.Order.Unit))
                .Select(unit => new Point(unit.X, unit.Y))
                .ToHashSet();
            candidate.TryTrafficBypass(routePlanner, occupied);
        }
    }
}
