using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Diagnostics;

namespace BattalionFulda;

internal sealed class BattlefieldView : Control
{
    private const int HeaderHeight = 44;
    private const int FooterHeight = 62;
    private const int InspectorWidth = 238;
    private const int MiniMapWidth = 220;
    private const int MiniMapHeight = 150;
    private const int TerrainChunkPixels = 1024;
    private const int TerrainChunkBleed = 4;
    private const double PlaybackGameSecondsPerRealSecond = 90.0;

    private readonly GameData data;
    private readonly Font titleFont = new("Bahnschrift SemiCondensed", 18, FontStyle.Bold);
    private readonly Font menuFont = new("Segoe UI", 11, FontStyle.Regular);
    private readonly Font headingFont = new("Bahnschrift SemiCondensed", 13, FontStyle.Bold);
    private readonly Font bodyFont = new("Segoe UI", 10, FontStyle.Regular);
    private readonly Font bodyBoldFont = new("Segoe UI Semibold", 10, FontStyle.Bold);
    private readonly Font counterFont = new("Arial", 9, FontStyle.Bold);
    private readonly Bitmap? terrainAtlas = LoadTerrainAtlas();
    private readonly Dictionary<(int X, int Y), Bitmap> terrainChunks = [];
    private readonly Dictionary<(int X, int Y, int RoadClass), MapVertex> roadSnapCache = [];
    private readonly Dictionary<int, PlannedMoveOrder> plannedOrders = [];
    private readonly Dictionary<int, PlannedFireOrder> plannedFireOrders = [];
    private readonly ContextMenuStrip unitActionMenu = new();
    private readonly AStarRoutePlanner routePlanner;
    private readonly LineOfSightModel lineOfSight;
    private readonly DirectFireModel directFire;
    private float terrainLayerCellSize;
    private Bitmap? miniMapLayer;
    private readonly System.Windows.Forms.Timer edgeScrollTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer executionTimer = new() { Interval = 16 };
    private readonly Stopwatch executionClock = new();
    private float cellSize = 72f;
    private float cameraX = 4.5f;
    private float cameraY = 14.5f;
    private ScenarioUnit? selectedUnit;
    private Point? selectedCell = new Point(19, 48);
    private Point dragStart;
    private float dragCameraX;
    private float dragCameraY;
    private bool dragging;
    private bool dragMoved;
    private MouseButtons dragButton;
    private bool navigatingMiniMap;
    private PlannedMoveOrder? plottingOrder;
    private ScenarioUnit? targetingFireUnit;
    private ScenarioUnit? inspectingLosUnit;
    private LineOfSightQuality[,]? losVisibility;
    private WegoMovementExecution? movementExecution;
    private WegoCombatExecution? combatExecution;
    private TurnPhase turnPhase = TurnPhase.Planning;
    private bool executionPaused;
    private int turnNumber = 1;
    private DateTime scenarioTime;
    private int lastOpposingOrderCount;

    private float HexHeight => cellSize * 0.8660254f;
    private float HexColumnStep => cellSize * 0.75f;

    public BattlefieldView(GameData data)
    {
        this.data = data;
        scenarioTime = data.ScenarioStart;
        routePlanner = new AStarRoutePlanner(data);
        lineOfSight = new LineOfSightModel(data);
        directFire = new DirectFireModel(data);
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        edgeScrollTimer.Tick += (_, _) => EdgeScrollTick();
        edgeScrollTimer.Start();
        executionTimer.Tick += (_, _) => ExecutionTick();
        ConfigureUnitActionMenu();
    }

    public bool HandleKey(Keys key)
    {
        if (key == Keys.L)
        {
            ToggleLosInspection();
            return true;
        }
        if (inspectingLosUnit is not null && key == Keys.Escape)
        {
            inspectingLosUnit = null;
            Invalidate();
            return true;
        }
        if (inspectingLosUnit is not null && key == Keys.Enter) return true;
        if (turnPhase == TurnPhase.Executing && key == Keys.Space)
        {
            ToggleExecutionPause();
            return true;
        }
        if (turnPhase == TurnPhase.Review && key == Keys.Enter)
        {
            BeginNextTurn();
            return true;
        }

        switch (key)
        {
            case Keys.A or Keys.Left: MoveCursor(-1, 0); return true;
            case Keys.D or Keys.Right: MoveCursor(1, 0); return true;
            case Keys.W or Keys.Up: MoveCursor(0, -1); return true;
            case Keys.S or Keys.Down: MoveCursor(0, 1); return true;
            case Keys.Enter:
                if (targetingFireUnit is not null) ConfirmFireTargetAtCursor();
                else if (plottingOrder is not null) AddWaypointAtCursor();
                else SelectUnitAtCursor();
                return true;
            case Keys.Back:
                if (plottingOrder is not null) UndoWaypoint();
                return plottingOrder is not null;
            case Keys.Escape:
                if (targetingFireUnit is not null) CancelFireTargeting();
                else if (plottingOrder is not null) CancelPlotting();
                else unitActionMenu.Close();
                return true;
            case Keys.Add or Keys.Oemplus:
                ZoomAt(new Point(Width / 2, Height / 2), 1.12f);
                return true;
            case Keys.Subtract or Keys.OemMinus:
                ZoomAt(new Point(Width / 2, Height / 2), 0.89f);
                return true;
            default: return false;
        }
    }

    private void MoveCursor(int dx, int dy)
    {
        Point current = selectedCell ?? new Point(data.MapWidth / 2, data.MapHeight / 2);
        selectedCell = new Point(
            Math.Clamp(current.X + dx, 0, data.MapWidth - 1),
            Math.Clamp(current.Y + dy, 0, data.MapHeight - 1));
        EnsureCursorVisible();
        if (plottingOrder is not null) RebuildRoute(plottingOrder);
        Invalidate();
    }

    private void ConfigureUnitActionMenu()
    {
        unitActionMenu.ShowImageMargin = false;
        unitActionMenu.BackColor = Color.FromArgb(10, 38, 48);
        unitActionMenu.ForeColor = Color.FromArgb(226, 235, 226);
        unitActionMenu.Font = bodyFont;
        unitActionMenu.Renderer = new TacticalMenuRenderer();

        var move = new ToolStripMenuItem("MOVE");
        move.DropDownItems.Add("Quick", null, (_, _) => BeginMoveOrder(OrderPosture.Quick));
        move.DropDownItems.Add("Tactical", null, (_, _) => BeginMoveOrder(OrderPosture.Tactical));
        move.DropDownItems.Add("Hunt", null, (_, _) => BeginMoveOrder(OrderPosture.Hunt));
        move.DropDownOpening += (_, _) =>
        {
            foreach (ToolStripItem item in move.DropDownItems)
            {
                item.BackColor = Color.FromArgb(10, 38, 48);
                item.ForeColor = Color.FromArgb(226, 235, 226);
            }
        };
        unitActionMenu.Items.Add(move);
        unitActionMenu.Items.Add(new ToolStripSeparator());
        unitActionMenu.Items.Add("FIRE", null, (_, _) => BeginFireOrder());
        unitActionMenu.Items.Add("ASSAULT", null, (_, _) => ShowDeferredAction("ASSAULT — W2.6"));
        unitActionMenu.Items.Add("DEFEND", null, (_, _) => ShowDeferredAction("DEFEND — FUTURE"));
        unitActionMenu.Items.Add(new ToolStripSeparator());
        unitActionMenu.Items.Add("CANCEL ORDERS", null, (_, _) => ClearSelectedOrder());
        unitActionMenu.Items.Add("INSPECT LOS", null, (_, _) => ToggleLosInspection());
    }

    private void ShowDeferredAction(string message)
    {
        orderNotice = message;
        Invalidate();
    }

    private string? orderNotice;

    private void ShowUnitActionMenu(Point location)
    {
        if (selectedUnit is null || turnPhase != TurnPhase.Planning) return;
        bool friendly = selectedUnit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase);
        unitActionMenu.Items[0].Enabled = friendly;
        unitActionMenu.Items[2].Enabled = friendly;
        unitActionMenu.Items[3].Enabled = friendly;
        unitActionMenu.Items[4].Enabled = friendly;
        unitActionMenu.Items[6].Enabled = friendly &&
            (plannedOrders.ContainsKey(selectedUnit.Id) || plannedFireOrders.ContainsKey(selectedUnit.Id));
        orderNotice = friendly ? null : "ENEMY UNIT — INFORMATION ONLY";
        unitActionMenu.Show(this, location);
        Invalidate();
    }

    private void BeginMoveOrder(OrderPosture posture)
    {
        if (turnPhase != TurnPhase.Planning || selectedUnit is null ||
            !selectedUnit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase))
            return;

        plannedFireOrders.Remove(selectedUnit.Id);
        inspectingLosUnit = null;
        targetingFireUnit = null;
        plottingOrder = new PlannedMoveOrder { Unit = selectedUnit, Posture = posture };
        selectedCell = new Point(selectedUnit.X, selectedUnit.Y);
        orderNotice = null;
        RebuildRoute(plottingOrder);
        Invalidate();
    }

    private void AddWaypointAtCursor()
    {
        if (selectedCell is Point cell) AddWaypoint(cell);
    }

    private void AddWaypoint(Point cell)
    {
        if (plottingOrder is null) return;
        if (plottingOrder.Waypoints.Count > 0 && plottingOrder.Waypoints[^1] == cell)
            return;
        if (cell.X == plottingOrder.Unit.X && cell.Y == plottingOrder.Unit.Y &&
            plottingOrder.Waypoints.Count == 0)
            return;
        plottingOrder.Waypoints.Add(cell);
        selectedCell = cell;
        RebuildRoute(plottingOrder);
        Invalidate();
    }

    private void UndoWaypoint()
    {
        if (plottingOrder is null || plottingOrder.Waypoints.Count == 0) return;
        plottingOrder.Waypoints.RemoveAt(plottingOrder.Waypoints.Count - 1);
        Point end = plottingOrder.Waypoints.LastOrDefault(
            new Point(plottingOrder.Unit.X, plottingOrder.Unit.Y));
        selectedCell = end;
        RebuildRoute(plottingOrder);
        Invalidate();
    }

    private void ConfirmPlotting()
    {
        if (plottingOrder is null || plottingOrder.Waypoints.Count == 0) return;
        if (!plottingOrder.IsReachable)
        {
            orderNotice = plottingOrder.FailureReason;
            Invalidate();
            return;
        }
        plottingOrder.Confirmed = true;
        plottingOrder.ExecutionState = MoveOrderExecutionState.Planned;
        plannedOrders[plottingOrder.Unit.Id] = plottingOrder;
        orderNotice = $"{plottingOrder.Posture.ToString().ToUpperInvariant()} MOVE PLANNED";
        plottingOrder = null;
        Invalidate();
    }

    private void CancelPlotting()
    {
        plottingOrder = null;
        orderNotice = "ORDER CANCELLED";
        Invalidate();
    }

    private void BeginFireOrder()
    {
        if (turnPhase != TurnPhase.Planning || selectedUnit is null || selectedUnit.IsDestroyed ||
            !selectedUnit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase))
            return;
        if (selectedUnit.Category is "artillery" or "rocket_artillery")
        {
            orderNotice = "INDIRECT FIRE WILL ARRIVE IN A LATER MILESTONE";
            Invalidate();
            return;
        }

        plottingOrder = null;
        inspectingLosUnit = null;
        targetingFireUnit = selectedUnit;
        selectedCell = new Point(selectedUnit.X, selectedUnit.Y);
        orderNotice = $"SELECT ENEMY TARGET — RANGE {selectedUnit.RangeCells} HEXES";
        Invalidate();
    }

    private void ConfirmFireTargetAtCursor()
    {
        if (targetingFireUnit is null || selectedCell is not Point cell) return;
        ScenarioUnit? target = data.Units.LastOrDefault(unit => !unit.IsDestroyed &&
            unit.X == cell.X && unit.Y == cell.Y &&
            !unit.Faction.Equals(targetingFireUnit.Faction, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            orderNotice = "SELECT AN ENEMY UNIT";
            Invalidate();
            return;
        }

        FireValidation validation = directFire.Validate(targetingFireUnit, target);
        if (!validation.CanFire)
        {
            orderNotice = validation.Message;
            Invalidate();
            return;
        }

        plannedOrders.Remove(targetingFireUnit.Id);
        plannedFireOrders[targetingFireUnit.Id] = new PlannedFireOrder
        {
            Unit = targetingFireUnit,
            Target = target,
            LineOfSight = validation.LineOfSight
        };
        selectedUnit = targetingFireUnit;
        selectedCell = new Point(targetingFireUnit.X, targetingFireUnit.Y);
        orderNotice = $"FIRE PLANNED — {target.Name.ToUpperInvariant()}";
        targetingFireUnit = null;
        Invalidate();
    }

    private void CancelFireTargeting()
    {
        targetingFireUnit = null;
        orderNotice = "FIRE ORDER CANCELLED";
        Invalidate();
    }

    private void ToggleLosInspection()
    {
        if (inspectingLosUnit is not null)
        {
            inspectingLosUnit = null;
            Invalidate();
            return;
        }
        if (turnPhase == TurnPhase.Executing || selectedUnit is null || selectedUnit.IsDestroyed)
        {
            orderNotice = "SELECT A UNIT IN PLANNING OR REVIEW TO INSPECT LOS";
            Invalidate();
            return;
        }
        plottingOrder = null;
        targetingFireUnit = null;
        inspectingLosUnit = selectedUnit;
        selectedCell = new Point(selectedUnit.X, selectedUnit.Y);
        losVisibility = lineOfSight.CalculateVisibility(selectedCell.Value);
        orderNotice = null;
        Invalidate();
    }

    private void ClearSelectedOrder()
    {
        if (turnPhase != TurnPhase.Planning || selectedUnit is null) return;
        plannedOrders.Remove(selectedUnit.Id);
        plannedFireOrders.Remove(selectedUnit.Id);
        if (ReferenceEquals(plottingOrder?.Unit, selectedUnit)) plottingOrder = null;
        if (ReferenceEquals(targetingFireUnit, selectedUnit)) targetingFireUnit = null;
        orderNotice = "ORDERS CLEARED";
        Invalidate();
    }

    private void BeginExecution()
    {
        if (turnPhase != TurnPhase.Planning || plottingOrder is not null ||
            targetingFireUnit is not null) return;
        lastOpposingOrderCount = GenerateOpposingOrders();
        PlannedMoveOrder[] executable = plannedOrders.Values
            .Where(order => !order.Unit.IsDestroyed && order.Confirmed && order.Route.Count > 1)
            .ToArray();
        PlannedFireOrder[] fireOrders = plannedFireOrders.Values
            .Where(order => !order.Unit.IsDestroyed && !order.Target.IsDestroyed)
            .ToArray();
        if (executable.Length == 0 && fireOrders.Length == 0)
        {
            orderNotice = "NO PLANNED ORDERS";
            Invalidate();
            return;
        }

        unitActionMenu.Close();
        movementExecution = new WegoMovementExecution(data, executable);
        combatExecution = new WegoCombatExecution(data, fireOrders, turnNumber);
        turnPhase = TurnPhase.Executing;
        executionPaused = false;
        orderNotice = $"TURN {turnNumber} EXECUTING — {executable.Length} MOVE, " +
                      $"{fireOrders.Length} FIRE" +
                      (lastOpposingOrderCount > 0
                          ? $" ({lastOpposingOrderCount} PACT ORDERS ISSUED)"
                          : string.Empty);
        executionClock.Restart();
        executionTimer.Start();
        Invalidate();
    }

    private int GenerateOpposingOrders()
    {
        ScenarioUnit[] friendlyUnits = data.Units.Where(unit => !unit.IsDestroyed &&
            unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (friendlyUnits.Length == 0) return 0;

        int generated = 0;
        foreach (ScenarioUnit unit in data.Units.Where(unit =>
                     !unit.IsDestroyed &&
                     !unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase)))
        {
            ScenarioUnit? fireTarget = friendlyUnits
                .Where(candidate => !candidate.IsDestroyed)
                .Select(candidate => (Unit: candidate, Validation: directFire.Validate(unit, candidate)))
                .Where(candidate => candidate.Validation.CanFire)
                .OrderBy(candidate => candidate.Validation.LineOfSight.Range)
                .Select(candidate => candidate.Unit)
                .FirstOrDefault();
            if (fireTarget is not null)
            {
                FireValidation validation = directFire.Validate(unit, fireTarget);
                plannedOrders.Remove(unit.Id);
                plannedFireOrders[unit.Id] = new PlannedFireOrder
                {
                    Unit = unit,
                    Target = fireTarget,
                    LineOfSight = validation.LineOfSight
                };
                generated++;
                continue;
            }

            if (plannedOrders.TryGetValue(unit.Id, out PlannedMoveOrder? existing) &&
                existing.ExecutionState == MoveOrderExecutionState.Partial &&
                existing.Route.Count > 1)
                continue;

            ScenarioUnit objective = friendlyUnits.OrderBy(candidate =>
                HexRange(new Point(unit.X, unit.Y), new Point(candidate.X, candidate.Y))).First();
            Point start = new(unit.X, unit.Y);
            Point desired = new(
                Math.Clamp(unit.X + Math.Clamp(objective.X - unit.X, -12, 12),
                    0, data.MapWidth - 1),
                Math.Clamp(unit.Y + Math.Clamp(objective.Y - unit.Y, -12, 12),
                    0, data.MapHeight - 1));

            RouteResult? route = null;
            foreach (Point cell in NearbyCells(desired, 4))
            {
                if (data.Units.Any(other => other.Id != unit.Id &&
                    other.X == cell.X && other.Y == cell.Y))
                    continue;
                RouteResult candidate = routePlanner.FindRoute(
                    unit, start, cell, OrderPosture.Tactical);
                if (!candidate.Success || candidate.Cells.Count < 2) continue;
                route = candidate;
                break;
            }
            if (route is null) continue;

            var order = new PlannedMoveOrder
            {
                Unit = unit,
                Posture = OrderPosture.Tactical,
                Confirmed = true,
                TotalCost = route.Cost,
                ExecutionState = MoveOrderExecutionState.Planned,
                ExecutionNote = "OPPOSING FORCE ADVANCE"
            };
            order.Route.AddRange(route.Cells);
            order.Waypoints.Add(route.Cells[^1]);
            plannedOrders[unit.Id] = order;
            plannedFireOrders.Remove(unit.Id);
            generated++;
        }
        return generated;
    }

    private IEnumerable<Point> NearbyCells(Point center, int radius)
    {
        for (int distance = 0; distance <= radius; distance++)
        for (int y = center.Y - distance; y <= center.Y + distance; y++)
        for (int x = center.X - distance; x <= center.X + distance; x++)
        {
            if (Math.Max(Math.Abs(x - center.X), Math.Abs(y - center.Y)) != distance)
                continue;
            if (x >= 0 && x < data.MapWidth && y >= 0 && y < data.MapHeight)
                yield return new Point(x, y);
        }
    }

    private static int HexRange(Point first, Point second)
    {
        static (int X, int Y, int Z) Cube(Point point)
        {
            int x = point.X;
            int z = point.Y - (point.X - (point.X & 1)) / 2;
            return (x, -x - z, z);
        }
        var a = Cube(first);
        var b = Cube(second);
        return Math.Max(Math.Abs(a.X - b.X),
            Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
    }

    private void ExecutionTick()
    {
        if (turnPhase != TurnPhase.Executing || movementExecution is null) return;
        if (executionPaused)
        {
            executionClock.Restart();
            return;
        }

        double realSeconds = Math.Min(0.1, executionClock.Elapsed.TotalSeconds);
        executionClock.Restart();
        movementExecution.Advance(realSeconds * PlaybackGameSecondsPerRealSecond);
        combatExecution?.AdvanceTo(movementExecution.ElapsedGameSeconds);
        if (selectedUnit is not null)
            selectedCell = new Point(selectedUnit.X, selectedUnit.Y);

        bool combatDone = combatExecution is null || combatExecution.Orders.Count == 0 ||
                          combatExecution.IsResolved;
        if (movementExecution.IsComplete && combatDone) CompleteExecution();
        Invalidate();
    }

    private void CompleteExecution()
    {
        if (movementExecution is null) return;
        executionTimer.Stop();
        executionClock.Stop();
        movementExecution.FinalizeTurn();
        combatExecution?.AdvanceTo(movementExecution.TurnDurationSeconds);
        scenarioTime = scenarioTime.AddMinutes(data.TurnMinutes);
        turnPhase = TurnPhase.Review;
        executionPaused = false;
        int completed = plannedOrders.Values.Count(order =>
            order.ExecutionState == MoveOrderExecutionState.Complete);
        int partial = plannedOrders.Values.Count(order =>
            order.ExecutionState == MoveOrderExecutionState.Partial);
        int losses = combatExecution?.Results.Sum(result => result.StrengthLoss) ?? 0;
        int shots = combatExecution?.Results.Count(result => result.Fired) ?? 0;
        orderNotice = $"TURN {turnNumber} COMPLETE — {completed} ARRIVED, {partial} CONTINUING, " +
                      $"{shots} FIRED, {losses} LOSSES";
    }

    private void ToggleExecutionPause()
    {
        if (turnPhase != TurnPhase.Executing) return;
        executionPaused = !executionPaused;
        orderNotice = executionPaused ? "EXECUTION PAUSED" : $"TURN {turnNumber} EXECUTING";
        executionClock.Restart();
        Invalidate();
    }

    private void BeginNextTurn()
    {
        if (turnPhase != TurnPhase.Review) return;
        foreach (int unitId in plannedOrders
                     .Where(pair => pair.Value.ExecutionState == MoveOrderExecutionState.Complete)
                     .Select(pair => pair.Key).ToArray())
            plannedOrders.Remove(unitId);
        plannedFireOrders.Clear();
        foreach (int unitId in plannedOrders.Where(pair => pair.Value.Unit.IsDestroyed)
                     .Select(pair => pair.Key).ToArray())
            plannedOrders.Remove(unitId);
        movementExecution = null;
        combatExecution = null;
        turnNumber++;
        turnPhase = TurnPhase.Planning;
        orderNotice = $"TURN {turnNumber} PLANNING";
        Invalidate();
    }

    private void RebuildRoute(PlannedMoveOrder order)
    {
        order.Route.Clear();
        order.TotalCost = 0;
        order.IsReachable = true;
        order.FailureReason = null;
        Point start = new(order.Unit.X, order.Unit.Y);
        order.Route.Add(start);
        foreach (Point waypoint in order.Waypoints)
        {
            RouteResult segment = routePlanner.FindRoute(order.Unit, start, waypoint, order.Posture);
            if (!AppendRouteSegment(order, segment)) return;
            start = waypoint;
        }

        if (ReferenceEquals(order, plottingOrder) && selectedCell is Point preview &&
            preview != start)
        {
            RouteResult segment = routePlanner.FindRoute(order.Unit, start, preview, order.Posture);
            AppendRouteSegment(order, segment);
        }
    }

    private static bool AppendRouteSegment(PlannedMoveOrder order, RouteResult segment)
    {
        if (!segment.Success)
        {
            order.IsReachable = false;
            order.FailureReason = segment.FailureReason;
            return false;
        }
        if (segment.Cells.Count > 1) order.Route.AddRange(segment.Cells.Skip(1));
        order.TotalCost += segment.Cost;
        return true;
    }

    private void SelectUnitAtCursor()
    {
        if (selectedCell is not Point cell) return;
        selectedUnit = data.Units.LastOrDefault(unit => !unit.IsDestroyed &&
            unit.X == cell.X && unit.Y == cell.Y);
        Invalidate();
    }

    private void EnsureCursorVisible()
    {
        if (selectedCell is not Point cell) return;
        Rectangle map = MapBounds();
        RectangleF cursorBounds = CellBounds(map, cell.X, cell.Y);
        float horizontalMargin = Math.Min(cellSize, map.Width * 0.18f);
        float verticalMargin = Math.Min(HexHeight, map.Height * 0.18f);

        if (cursorBounds.Left < map.Left + horizontalMargin)
            cameraX -= (map.Left + horizontalMargin - cursorBounds.Left) / HexColumnStep;
        else if (cursorBounds.Right > map.Right - horizontalMargin)
            cameraX += (cursorBounds.Right - (map.Right - horizontalMargin)) / HexColumnStep;

        if (cursorBounds.Top < map.Top + verticalMargin)
            cameraY -= (map.Top + verticalMargin - cursorBounds.Top) / HexHeight;
        else if (cursorBounds.Bottom > map.Bottom - verticalMargin)
            cameraY += (cursorBounds.Bottom - (map.Bottom - verticalMargin)) / HexHeight;

        ClampCamera();
    }

    private void EdgeScrollTick()
    {
        Form? form = FindForm();
        if (form is null || !form.ContainsFocus || dragging || navigatingMiniMap ||
            Control.MouseButtons != MouseButtons.None)
            return;

        Rectangle map = MapBounds();
        Point pointer = PointToClient(Cursor.Position);
        Rectangle activationArea = Rectangle.Inflate(map, 32, 32);
        if (!activationArea.Contains(pointer) || FooterBounds().Contains(pointer) ||
            MiniMapBounds(map).Contains(pointer))
            return;

        float oldX = cameraX;
        float oldY = cameraY;
        cameraX += EdgeScrollDelta(pointer.X, map.Left, map.Right);
        float verticalDelta = EdgeScrollDelta(pointer.Y, map.Top, map.Bottom);
        int footerCommandRight = 20 + FooterCommands().Length * 116;
        bool enteringFooterCommands = verticalDelta > 0 &&
                                      pointer.X <= footerCommandRight + 24;
        if (!enteringFooterCommands) cameraY += verticalDelta;
        ClampCamera();

        if (Math.Abs(cameraX - oldX) > 0.0001f || Math.Abs(cameraY - oldY) > 0.0001f)
            Invalidate();
    }

    private static float EdgeScrollDelta(int position, int low, int high)
    {
        const int edgeZone = 34;
        const float minimumSpeed = 0.26f;
        const float maximumSpeed = 0.68f;

        if (position <= low + edgeZone)
        {
            float depth = Math.Clamp((low + edgeZone - position) / (float)edgeZone, 0f, 2f);
            return -Math.Min(maximumSpeed, minimumSpeed * depth + 0.08f);
        }

        if (position >= high - edgeZone)
        {
            float depth = Math.Clamp((position - (high - edgeZone)) / (float)edgeZone, 0f, 2f);
            return Math.Min(maximumSpeed, minimumSpeed * depth + 0.08f);
        }

        return 0f;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(7, 20, 29));

        Rectangle header = new(0, 0, Width, HeaderHeight);
        Rectangle inspector = new(0, HeaderHeight, InspectorWidth, Height - HeaderHeight - FooterHeight);
        Rectangle map = new(InspectorWidth, HeaderHeight, Width - InspectorWidth, Height - HeaderHeight - FooterHeight);
        Rectangle footer = new(0, Height - FooterHeight, Width, FooterHeight);

        DrawHeader(g, header);
        DrawInspector(g, inspector);
        DrawMap(g, map);
        DrawFooter(g, footer);
        DrawMiniMap(g, map);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampCamera();
        Invalidate();
    }

    private void DrawHeader(Graphics g, Rectangle bounds)
    {
        using var background = new SolidBrush(Color.FromArgb(157, 187, 204));
        using var ink = new SolidBrush(Color.FromArgb(7, 25, 37));
        g.FillRectangle(background, bounds);
        g.DrawString("BATTALION: FULDA", titleFont, ink, 14, 7);

        string[] menus = ["FILE", "ORDERS", "UNITS", "MAP", "INTEL", "OPTIONS", "HELP"];
        float x = 430;
        foreach (string menu in menus)
        {
            g.DrawString(menu, menuFont, ink, x, 12);
            x += g.MeasureString(menu, menuFont).Width + 30;
        }

        string phase = turnPhase switch
        {
            TurnPhase.Executing => executionPaused ? "PAUSED" : "EXECUTING",
            TurnPhase.Review => "REVIEW",
            _ => "PLANNING"
        };
        DateTime displayedTime = turnPhase == TurnPhase.Executing && movementExecution is not null
            ? scenarioTime.AddSeconds(movementExecution.ElapsedGameSeconds)
            : scenarioTime;
        string turn = $"TURN {turnNumber}  {phase}     " +
                      $"{displayedTime:dd MMM yyyy}     {displayedTime:HHmm}".ToUpperInvariant();
        SizeF turnSize = g.MeasureString(turn, bodyBoldFont);
        g.DrawString(turn, bodyBoldFont, ink, Width - turnSize.Width - 18, 14);
    }

    private void DrawInspector(Graphics g, Rectangle bounds)
    {
        using var panel = new SolidBrush(Color.FromArgb(8, 31, 44));
        using var border = new Pen(Color.FromArgb(116, 157, 178), 1);
        using var primary = new SolidBrush(Color.FromArgb(226, 235, 226));
        using var secondary = new SolidBrush(Color.FromArgb(157, 187, 204));
        using var accent = new SolidBrush(Color.FromArgb(222, 192, 53));
        g.FillRectangle(panel, bounds);
        g.DrawLine(border, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom);

        int x = 17;
        int y = bounds.Top + 18;
        string heading = selectedUnit?.Name ?? "TACTICAL DISPLAY";
        g.DrawString(heading.ToUpperInvariant(), headingFont, primary, x, y);
        y += 39;
        g.DrawLine(border, x, y, bounds.Right - 17, y);
        y += 20;

        if (selectedUnit is not null)
        {
            DrawCounter(g, new Rectangle(x, y, 92, 78), selectedUnit, true);
            y += 96;
            g.DrawString(selectedUnit.Type, bodyFont, primary,
                new RectangleF(x, y, bounds.Width - 34, 48));
            y += 52;
            g.DrawString(selectedUnit.Formation, bodyFont, secondary,
                new RectangleF(x, y, bounds.Width - 34, 42));
            y += 48;
            DrawStat(g, "STRENGTH", selectedUnit.Strength.ToString(), x, ref y, primary, secondary);
            DrawStat(g, "MORALE", MoraleText(selectedUnit.Morale), x, ref y, primary, secondary);
            DrawStat(g, "SUPPRESSION", selectedUnit.Suppression.ToString(), x, ref y, primary, secondary);
            DrawStat(g, "READINESS", selectedUnit.Readiness.ToString(), x, ref y, primary, secondary);
            DrawStat(g, "AMPHIBIOUS", selectedUnit.Amphibious ? "YES" : "NO", x, ref y, primary, secondary);
            DrawStat(g, "FIREPOWER", $"H {selectedUnit.HardAttack} / S {selectedUnit.SoftAttack}",
                x, ref y, primary, secondary);
            DrawStat(g, "DEFENCE / RANGE", $"{selectedUnit.Defense} / {selectedUnit.RangeCells}",
                x, ref y, primary, secondary);
            PlannedMoveOrder? order = plottingOrder?.Unit.Id == selectedUnit.Id
                ? plottingOrder
                : plannedOrders.GetValueOrDefault(selectedUnit.Id);
            if (order is not null)
            {
                y += 7;
                g.DrawLine(border, x, y, bounds.Right - 17, y);
                y += 13;
                g.DrawString("ORDERS", headingFont, accent, x, y);
                y += 31;
                DrawStat(g, "TYPE", $"{order.Posture.ToString().ToUpperInvariant()} MOVE",
                    x, ref y, primary, secondary);
                DrawStat(g, "STATUS", order.Status, x, ref y, primary, secondary);
                DrawStat(g, "WAYPOINTS", order.Waypoints.Count.ToString(),
                    x, ref y, primary, secondary);
                DrawStat(g, "ROUTE COST", order.IsReachable
                    ? $"{order.TotalCost / 10f:0.0}"
                    : "UNREACHABLE", x, ref y, primary, secondary);
                if (order.ExecutionState is MoveOrderExecutionState.Executing or
                    MoveOrderExecutionState.Partial)
                {
                    DrawStat(g, "TURN COST", $"{order.LastTurnCost / 10f:0.0}",
                        x, ref y, primary, secondary);
                    UnitMovementExecution? execution = movementExecution?.ForUnit(selectedUnit);
                    if (execution?.WaitingForTraffic == true)
                    {
                        DrawStat(g, "MOVEMENT", "TRAFFIC HOLD", x, ref y, primary, secondary);
                        if (!string.IsNullOrWhiteSpace(order.ExecutionNote))
                        {
                            g.DrawString(order.ExecutionNote, bodyFont, accent,
                                new RectangleF(x + 8, y - 2, bounds.Width - 42, 42));
                            y += 40;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(order.ExecutionNote))
                        DrawStat(g, "MOVEMENT", order.ExecutionNote == "TRAFFIC BYPASS"
                            ? "BYPASS USED" : "REROUTED", x, ref y, primary, secondary);
                }
            }
            else if (plannedFireOrders.TryGetValue(selectedUnit.Id,
                         out PlannedFireOrder? fireOrder))
            {
                y += 7;
                g.DrawLine(border, x, y, bounds.Right - 17, y);
                y += 13;
                g.DrawString("ORDERS", headingFont, accent, x, y);
                y += 31;
                DrawStat(g, "TYPE", "DIRECT FIRE", x, ref y, primary, secondary);
                DrawStat(g, "STATUS", fireOrder.Status, x, ref y, primary, secondary);
                DrawStat(g, "TARGET", CompactLabel(fireOrder.Target.Name, 18),
                    x, ref y, primary, secondary);
                DrawStat(g, "RANGE", $"{fireOrder.LineOfSight.Range} HEXES",
                    x, ref y, primary, secondary);
                DrawStat(g, "LOS", fireOrder.LineOfSight.Quality.ToString().ToUpperInvariant(),
                    x, ref y, primary, secondary);
                if (fireOrder.Result is CombatResult result)
                {
                    g.DrawString(result.Message, bodyFont, accent,
                        new RectangleF(x + 8, y, bounds.Width - 42, 48));
                    y += 48;
                }
            }
        }
        else
        {
            g.DrawString("SELECT A UNIT OR MAP CELL", bodyFont, secondary, x, y);
            y += 42;
        }

        MapCell? cell = SelectedMapCell();
        if (cell is not null)
        {
            g.DrawLine(border, x, y + 4, bounds.Right - 17, y + 4);
            y += 20;
            g.DrawString("GROUND", headingFont, accent, x, y);
            y += 33;
            DrawStat(g, "TERRAIN", cell.Terrain.ToUpperInvariant(), x, ref y, primary, secondary);
            DrawStat(g, "ELEVATION", $"{cell.Elevation} m", x, ref y, primary, secondary);
            DrawFeatureInfo(g, cell, x, ref y, primary, secondary);
            DrawStat(g, "LOCATION", $"{cell.X:D2}, {cell.Y:D2}", x, ref y, primary, secondary);
        }
    }

    private void DrawFeatureInfo(Graphics g, MapCell cell, int x, ref int y,
        Brush primary, Brush secondary)
    {
        g.DrawString("FEATURES", bodyFont, secondary, x, y);
        y += 23;

        var features = new List<string>(3);
        if (cell.RoadClass > 0)
        {
            features.Add(cell.RoadClass switch
            {
                3 => "PRIMARY ROAD",
                2 => "SECONDARY ROAD",
                _ => "LOCAL ROAD"
            });
        }
        if (cell.RiverClass > 0)
        {
            features.Add(cell.RiverClass switch
            {
                1 => "STREAM",
                2 => "MINOR RIVER",
                _ => "MAJOR RIVER"
            });
        }
        if (cell.HasBridge) features.Add("BRIDGE");
        if (features.Count == 0) features.Add("—");

        foreach (string feature in features)
        {
            g.DrawString(feature, bodyBoldFont, primary, x + 8, y);
            y += 24;
        }
    }

    private static void DrawStat(Graphics g, string label, string value, int x, ref int y,
        Brush primary, Brush secondary)
    {
        using var labelFont = new Font("Segoe UI", 9, FontStyle.Regular);
        using var valueFont = new Font("Segoe UI Semibold", 10, FontStyle.Bold);
        g.DrawString(label, labelFont, secondary, x, y);
        SizeF size = g.MeasureString(value, valueFont);
        g.DrawString(value, valueFont, primary, x + 202 - size.Width, y - 2);
        y += 27;
    }

    private static string CompactLabel(string value, int maximumLength)
    {
        string upper = value.ToUpperInvariant();
        return upper.Length <= maximumLength ? upper : upper[..(maximumLength - 1)] + "…";
    }

    private void DrawMap(Graphics g, Rectangle bounds)
    {
        using Region oldClip = g.Clip;
        g.SetClip(bounds);
        DrawTerrainChunks(g, bounds);
        if (inspectingLosUnit is not null && losVisibility is not null)
            DrawLosVisibility(g, bounds);

        foreach (PlannedMoveOrder order in plannedOrders.Values
                     .OrderBy(order => ReferenceEquals(order.Unit, selectedUnit)))
            DrawOrderRoute(g, bounds, order, false);
        if (plottingOrder is not null)
            DrawOrderRoute(g, bounds, plottingOrder, true);
        foreach (PlannedFireOrder order in plannedFireOrders.Values)
            DrawFireOrder(g, bounds, order);
        if (targetingFireUnit is not null && selectedCell is Point fireCell)
            DrawFirePreview(g, bounds, targetingFireUnit, fireCell);
        if (inspectingLosUnit is not null && selectedCell is Point losCell)
            DrawLosInspection(g, bounds, inspectingLosUnit, losCell);

        int firstX = Math.Max(0, (int)Math.Floor(cameraX) - 2);
        int firstY = Math.Max(0, (int)Math.Floor(cameraY) - 2);
        int lastX = Math.Min(data.MapWidth - 1,
            firstX + (int)Math.Ceiling(bounds.Width / HexColumnStep) + 4);
        int lastY = Math.Min(data.MapHeight - 1,
            firstY + (int)Math.Ceiling(bounds.Height / HexHeight) + 4);

        foreach (ScenarioUnit unit in data.Units.Where(unit => !unit.IsDestroyed))
        {
            if (unit.X < firstX - 1 || unit.X > lastX + 1 || unit.Y < firstY - 1 || unit.Y > lastY + 1)
                continue;
            PointF center = UnitScreenCenter(bounds, unit);
            int w = Math.Clamp((int)(cellSize * 0.72f), 32, 54);
            int h = Math.Clamp((int)(cellSize * 0.62f), 29, 48);
            var counterBounds = new Rectangle((int)(center.X - w / 2f),
                (int)(center.Y - h / 2f), w, h);
            DrawCounter(g, counterBounds, unit, ReferenceEquals(unit, selectedUnit));
            UnitMovementExecution? execution = movementExecution?.ForUnit(unit);
            if (execution?.WaitingForTraffic == true)
                DrawTrafficHoldMarker(g, counterBounds);
        }

        if (selectedCell is Point selected)
        {
            RectangleF cb = CellBounds(bounds, selected.X, selected.Y);
            Color selectionColor = plottingOrder is { IsReachable: false }
                ? Color.FromArgb(224, 66, 54)
                : targetingFireUnit is not null && !FirePreviewIsValid()
                    ? Color.FromArgb(224, 66, 54)
                : Color.FromArgb(240, 210, 42);
            using var selection = new Pen(selectionColor, 3);
            using GraphicsPath selectedHex = HexPath(RectangleF.Inflate(cb, -2, -2));
            g.DrawPath(selection, selectedHex);
        }

        g.Clip = oldClip;
    }

    private void DrawTrafficHoldMarker(Graphics g, Rectangle counterBounds)
    {
        var marker = new Rectangle(counterBounds.Right - 5, counterBounds.Top - 9, 20, 20);
        using var fill = new SolidBrush(Color.FromArgb(235, 218, 151, 32));
        using var outline = new Pen(Color.FromArgb(245, 250, 239, 203), 2);
        using var label = new SolidBrush(Color.FromArgb(25, 25, 20));
        using var markerFont = new Font("Segoe UI", 10, FontStyle.Bold);
        g.FillEllipse(fill, marker);
        g.DrawEllipse(outline, marker);
        g.DrawString("!", markerFont, label, marker.X + 6, marker.Y - 1);
    }

    private void DrawOrderRoute(Graphics g, Rectangle bounds, PlannedMoveOrder order, bool plotting)
    {
        if (order.Route.Count < 2) return;
        PointF[] points = RouteDisplayPoints(bounds, order.Route);
        bool nato = order.Unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase);
        bool selectedRoute = ReferenceEquals(order.Unit, selectedUnit);
        Color routeColor = !order.IsReachable
            ? Color.FromArgb(224, 66, 54)
            : plotting || selectedRoute
                ? Color.FromArgb(242, 211, 54)
                : nato
                    ? Color.FromArgb(210, 115, 205, 225)
                    : Color.FromArgb(220, 238, 117, 104);
        using var shadow = new Pen(Color.FromArgb(185, 5, 22, 28), 7)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.ArrowAnchor
        };
        using var route = new Pen(routeColor, plotting || selectedRoute ? 3.5f : 2.5f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.ArrowAnchor,
            DashStyle = plotting ? DashStyle.Solid : DashStyle.Dash
        };
        g.DrawLines(shadow, points);
        g.DrawLines(route, points);

        using var waypointFill = new SolidBrush(Color.FromArgb(230, 8, 31, 44));
        using var waypointBorder = new Pen(routeColor, 2);
        using var waypointText = new SolidBrush(Color.White);
        for (int i = 0; i < order.Waypoints.Count; i++)
        {
            int routeIndex = order.Route.LastIndexOf(order.Waypoints[i]);
            PointF center = routeIndex >= 0 ? points[routeIndex] :
                CellCenter(bounds, order.Waypoints[i]);
            float radius = Math.Clamp(cellSize * 0.14f, 7, 11);
            g.FillEllipse(waypointFill, center.X - radius, center.Y - radius,
                radius * 2, radius * 2);
            g.DrawEllipse(waypointBorder, center.X - radius, center.Y - radius,
                radius * 2, radius * 2);
            string label = (i + 1).ToString();
            SizeF labelSize = g.MeasureString(label, counterFont);
            g.DrawString(label, counterFont, waypointText,
                center.X - labelSize.Width / 2, center.Y - labelSize.Height / 2);
        }
    }

    private void DrawFireOrder(Graphics g, Rectangle bounds, PlannedFireOrder order)
    {
        if (order.Unit.IsDestroyed || order.Target.IsDestroyed && order.Result is null) return;
        PointF source = UnitScreenCenter(bounds, order.Unit);
        PointF target = UnitScreenCenter(bounds, order.Target);
        Color color = order.State == FireOrderState.Invalid
            ? Color.FromArgb(225, 82, 72)
            : order.Unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(235, 242, 211, 54)
                : Color.FromArgb(235, 238, 117, 104);
        using var shadow = new Pen(Color.FromArgb(190, 5, 20, 25), 6)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var fire = new Pen(color, order.State == FireOrderState.Complete ? 3.5f : 2.5f)
        {
            DashStyle = order.State == FireOrderState.Planned ? DashStyle.Dash : DashStyle.Solid,
            StartCap = LineCap.Round,
            EndCap = LineCap.ArrowAnchor
        };
        g.DrawLine(shadow, source, target);
        g.DrawLine(fire, source, target);

        if (order.Result is CombatResult result)
        {
            float radius = result.StrengthLoss > 0 ? 13 : 9;
            using var impact = new Pen(Color.FromArgb(245, 255, 224, 128), 3);
            g.DrawEllipse(impact, target.X - radius, target.Y - radius, radius * 2, radius * 2);
        }
    }

    private void DrawFirePreview(Graphics g, Rectangle bounds, ScenarioUnit attacker, Point targetCell)
    {
        LineOfSightResult sight = lineOfSight.Trace(
            new Point(attacker.X, attacker.Y), targetCell);
        ScenarioUnit? target = EnemyAt(targetCell, attacker.Faction);
        bool valid = target is not null && directFire.Validate(attacker, target).CanFire;
        Color color = valid
            ? sight.Quality == LineOfSightQuality.Clear
                ? Color.FromArgb(235, 242, 211, 54)
                : Color.FromArgb(235, 238, 166, 53)
            : Color.FromArgb(235, 224, 66, 54);
        PointF source = CellCenter(bounds, new Point(attacker.X, attacker.Y));
        PointF destination = CellCenter(bounds, targetCell);
        using var shadow = new Pen(Color.FromArgb(190, 5, 20, 25), 7);
        using var preview = new Pen(color, 3.5f)
        {
            DashStyle = DashStyle.Dash,
            EndCap = LineCap.ArrowAnchor
        };
        g.DrawLine(shadow, source, destination);
        g.DrawLine(preview, source, destination);
        if (sight.BlockingCell is Point blocker)
        {
            PointF blocked = CellCenter(bounds, blocker);
            using var marker = new Pen(Color.FromArgb(245, 224, 66, 54), 3);
            g.DrawLine(marker, blocked.X - 8, blocked.Y - 8, blocked.X + 8, blocked.Y + 8);
            g.DrawLine(marker, blocked.X + 8, blocked.Y - 8, blocked.X - 8, blocked.Y + 8);
        }
    }

    private void DrawLosVisibility(Graphics g, Rectangle bounds)
    {
        if (losVisibility is null) return;
        using var clearFill = new SolidBrush(Color.FromArgb(100, 80, 235, 135));
        using var obscuredFill = new SolidBrush(Color.FromArgb(100, 245, 185, 55));
        using var clearEdge = new Pen(Color.FromArgb(220, 120, 242, 160), 1.5f);
        using var obscuredEdge = new Pen(Color.FromArgb(220, 242, 190, 65), 1.5f);
        for (int x = 0; x < data.MapWidth; x++)
        for (int y = 0; y < data.MapHeight; y++)
        {
            LineOfSightQuality quality = losVisibility[x, y];
            if (quality == LineOfSightQuality.Blocked) continue;
            RectangleF cell = CellBounds(bounds, x, y);
            if (!cell.IntersectsWith(bounds)) continue;
            using GraphicsPath hex = HexPath(cell);
            g.FillPath(quality == LineOfSightQuality.Clear ? clearFill : obscuredFill, hex);
            g.DrawPath(quality == LineOfSightQuality.Clear ? clearEdge : obscuredEdge, hex);
        }
    }

    private void DrawLosInspection(Graphics g, Rectangle bounds, ScenarioUnit observer, Point targetCell)
    {
        LineOfSightResult sight = lineOfSight.Trace(new Point(observer.X, observer.Y), targetCell);
        Color color = sight.Quality switch
        {
            LineOfSightQuality.Clear => Color.FromArgb(120, 242, 160),
            LineOfSightQuality.Obscured => Color.FromArgb(242, 190, 65),
            _ => Color.FromArgb(242, 95, 80)
        };
        using var shadow = new Pen(Color.FromArgb(210, 5, 20, 25), 7);
        using var line = new Pen(color, 3) { EndCap = LineCap.ArrowAnchor };
        PointF start = CellCenter(bounds, new Point(observer.X, observer.Y));
        PointF end = CellCenter(bounds, targetCell);
        g.DrawLine(shadow, start, end);
        g.DrawLine(line, start, end);
        if (sight.BlockingCell is Point blocker)
        {
            PointF point = CellCenter(bounds, blocker);
            g.DrawLine(line, point.X - 9, point.Y - 9, point.X + 9, point.Y + 9);
            g.DrawLine(line, point.X + 9, point.Y - 9, point.X - 9, point.Y + 9);
        }
    }

    private string LosInspectionNotice()
    {
        if (inspectingLosUnit is null || selectedCell is not Point cell) return "INSPECT LOS";
        LineOfSightResult sight = lineOfSight.Trace(new Point(inspectingLosUnit.X, inspectingLosUnit.Y), cell);
        return $"GREEN / AMBER = VISIBLE  |  LOS: {sight.Reason ?? sight.Quality.ToString().ToUpperInvariant()} — " +
               $"{sight.Range} HEXES / {sight.Range * data.CellSizeMeters} M";
    }

    private ScenarioUnit? EnemyAt(Point cell, string friendlyFaction) =>
        data.Units.LastOrDefault(unit => !unit.IsDestroyed && unit.X == cell.X &&
            unit.Y == cell.Y &&
            !unit.Faction.Equals(friendlyFaction, StringComparison.OrdinalIgnoreCase));

    private bool FirePreviewIsValid()
    {
        if (targetingFireUnit is null || selectedCell is not Point cell) return false;
        ScenarioUnit? target = EnemyAt(cell, targetingFireUnit.Faction);
        return target is not null && directFire.Validate(targetingFireUnit, target).CanFire;
    }

    private PointF[] RouteDisplayPoints(Rectangle bounds, IReadOnlyList<Point> cells)
    {
        var points = new PointF[cells.Count];
        for (int index = 0; index < cells.Count; index++)
        {
            int roadClass = 0;
            if (index + 1 < cells.Count && data.Edges.TryGetValue(
                    HexEdgeKey.Create(cells[index], cells[index + 1]), out MapEdge? outgoing))
                roadClass = outgoing.RoadClass;
            if (roadClass == 0 && index > 0 && data.Edges.TryGetValue(
                    HexEdgeKey.Create(cells[index - 1], cells[index]), out MapEdge? incoming))
                roadClass = incoming.RoadClass;

            points[index] = roadClass > 0
                ? GeographicToScreen(bounds, NearestRoadPoint(cells[index], roadClass))
                : CellCenter(bounds, cells[index]);
        }
        return points;
    }

    private MapVertex NearestRoadPoint(Point cell, int roadClass)
    {
        if (roadSnapCache.TryGetValue((cell.X, cell.Y, roadClass), out MapVertex? cached) &&
            cached is not null)
            return cached;

        double mapWorldWidth = (data.MapWidth - 1) * 0.75 + 1.0;
        double mapWorldHeight = data.MapHeight + 0.5;
        double cellEasting = data.OriginEasting +
            (cell.X * 0.75 + 0.5) / mapWorldWidth * data.ExtentWidthMeters;
        double cellNorthing = data.OriginNorthing + data.ExtentHeightMeters -
            (cell.Y + ((cell.X & 1) == 1 ? 0.5 : 0.0) + 0.5) /
            mapWorldHeight * data.ExtentHeightMeters;

        MapVertex nearest = new(cellEasting, cellNorthing);
        double nearestDistanceSquared = double.MaxValue;
        foreach (MapFeaturePath road in data.Features.Where(feature =>
                     feature.Type.Equals("road", StringComparison.OrdinalIgnoreCase) &&
                     feature.FeatureClass == roadClass))
        {
            for (int index = 1; index < road.Vertices.Count; index++)
            {
                MapVertex candidate = ProjectToSegment(
                    cellEasting, cellNorthing, road.Vertices[index - 1], road.Vertices[index]);
                double dx = candidate.Easting - cellEasting;
                double dy = candidate.Northing - cellNorthing;
                double distanceSquared = dx * dx + dy * dy;
                if (distanceSquared >= nearestDistanceSquared) continue;
                nearest = candidate;
                nearestDistanceSquared = distanceSquared;
            }
        }

        roadSnapCache[(cell.X, cell.Y, roadClass)] = nearest;
        return nearest;
    }

    private static MapVertex ProjectToSegment(
        double x, double y, MapVertex start, MapVertex end)
    {
        double dx = end.Easting - start.Easting;
        double dy = end.Northing - start.Northing;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 0.000001) return start;
        double fraction = Math.Clamp(
            ((x - start.Easting) * dx + (y - start.Northing) * dy) / lengthSquared, 0, 1);
        return new MapVertex(start.Easting + fraction * dx, start.Northing + fraction * dy);
    }

    private PointF GeographicToScreen(Rectangle bounds, MapVertex vertex)
    {
        float worldWidth = (data.MapWidth - 1) * HexColumnStep + cellSize;
        float worldHeight = (data.MapHeight + 0.5f) * HexHeight;
        float worldX = (float)((vertex.Easting - data.OriginEasting) /
            data.ExtentWidthMeters * worldWidth);
        float worldY = (float)((data.OriginNorthing + data.ExtentHeightMeters -
            vertex.Northing) / data.ExtentHeightMeters * worldHeight);
        return new PointF(
            bounds.Left + worldX - cameraX * HexColumnStep,
            bounds.Top + worldY - cameraY * HexHeight);
    }

    private void DrawTerrainChunks(Graphics g, Rectangle bounds)
    {
        // Terrain is rasterized at one resolution, not rebuilt for every wheel event.
        const float rasterCellSize = 72f;
        float displayCellSize = cellSize;
        float scale = displayCellSize / rasterCellSize;
        GraphicsState state = g.Save();
        try
        {
            g.TranslateTransform(bounds.X, bounds.Y);
            g.ScaleTransform(scale, scale);
            cellSize = rasterCellSize;
            DrawTerrainChunksAtRasterSize(g, new Rectangle(0, 0,
                (int)Math.Ceiling(bounds.Width / scale),
                (int)Math.Ceiling(bounds.Height / scale)));
        }
        finally
        {
            cellSize = displayCellSize;
            g.Restore(state);
        }
    }

    private void DrawTerrainChunksAtRasterSize(Graphics g, Rectangle bounds)
    {
        if (Math.Abs(terrainLayerCellSize - cellSize) >= 0.01f)
        {
            foreach (Bitmap chunk in terrainChunks.Values) chunk.Dispose();
            terrainChunks.Clear();
            terrainLayerCellSize = cellSize;
        }

        float sourceX = cameraX * HexColumnStep;
        float sourceY = cameraY * HexHeight;
        int firstChunkX = Math.Max(0, (int)Math.Floor(sourceX / TerrainChunkPixels));
        int firstChunkY = Math.Max(0, (int)Math.Floor(sourceY / TerrainChunkPixels));
        int lastChunkX = (int)Math.Floor((sourceX + bounds.Width) / TerrainChunkPixels);
        int lastChunkY = (int)Math.Floor((sourceY + bounds.Height) / TerrainChunkPixels);
        for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
        for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
        {
            Bitmap chunk = GetTerrainChunk(chunkX, chunkY);
            Rectangle chunkWorldBounds = TerrainChunkWorldBounds(chunkX, chunkY);
            float destinationX = bounds.X + chunkWorldBounds.Left - sourceX;
            float destinationY = bounds.Y + chunkWorldBounds.Top - sourceY;
            g.DrawImage(chunk, new RectangleF(destinationX, destinationY, chunk.Width, chunk.Height),
                new RectangleF(0, 0, chunk.Width, chunk.Height), GraphicsUnit.Pixel);
        }
    }

    private Bitmap GetTerrainChunk(int chunkX, int chunkY)
    {
        if (terrainChunks.TryGetValue((chunkX, chunkY), out Bitmap? existing))
            return existing;

        int worldWidth = (int)Math.Ceiling((data.MapWidth - 1) * HexColumnStep + cellSize) + 2;
        int worldHeight = (int)Math.Ceiling((data.MapHeight + 0.5f) * HexHeight) + 2;
        Rectangle chunkWorldBounds = TerrainChunkWorldBounds(chunkX, chunkY);
        int left = chunkWorldBounds.Left;
        int top = chunkWorldBounds.Top;
        int width = chunkWorldBounds.Width;
        int height = chunkWorldBounds.Height;
        var chunk = new Bitmap(width, height);
        terrainChunks.Add((chunkX, chunkY), chunk);

        using Graphics layer = Graphics.FromImage(chunk);
        layer.SmoothingMode = SmoothingMode.AntiAlias;
        layer.InterpolationMode = InterpolationMode.HighQualityBilinear;
        layer.Clear(Color.FromArgb(67, 75, 50));
        layer.TranslateTransform(-left, -top);

        int firstX = Math.Max(0, (int)Math.Floor(left / HexColumnStep) - 2);
        int lastX = Math.Min(data.MapWidth - 1,
            (int)Math.Ceiling((left + width) / HexColumnStep) + 2);
        int firstY = Math.Max(0, (int)Math.Floor(top / HexHeight) - 2);
        int lastY = Math.Min(data.MapHeight - 1,
            (int)Math.Ceiling((top + height) / HexHeight) + 2);

        for (int y = firstY; y <= lastY; y++)
        for (int x = firstX; x <= lastX; x++)
        {
            RectangleF cellBounds = WorldCellBounds(x, y);
            using GraphicsPath hex = HexPath(cellBounds);
            GraphicsState state = layer.Save();
            layer.SetClip(hex, CombineMode.Intersect);
            DrawTerrainCell(layer, cellBounds, data.Cells[x, y]);
            layer.Restore(state);
        }

        DrawGeographicFeatures(layer, worldWidth, worldHeight);

        using var gridPen = new Pen(Color.FromArgb(70, 31, 42, 34), 1);
        for (int y = firstY; y <= lastY; y++)
        for (int x = firstX; x <= lastX; x++)
        {
            using GraphicsPath hex = HexPath(WorldCellBounds(x, y));
            layer.DrawPath(gridPen, hex);
        }
        layer.ResetTransform();
        return chunk;
    }

    private Rectangle TerrainChunkWorldBounds(int chunkX, int chunkY)
    {
        int worldWidth = (int)Math.Ceiling((data.MapWidth - 1) * HexColumnStep + cellSize) + 2;
        int worldHeight = (int)Math.Ceiling((data.MapHeight + 0.5f) * HexHeight) + 2;
        int logicalLeft = chunkX * TerrainChunkPixels;
        int logicalTop = chunkY * TerrainChunkPixels;
        int left = Math.Max(0, logicalLeft - TerrainChunkBleed);
        int top = Math.Max(0, logicalTop - TerrainChunkBleed);
        int right = Math.Min(worldWidth, logicalLeft + TerrainChunkPixels + TerrainChunkBleed);
        int bottom = Math.Min(worldHeight, logicalTop + TerrainChunkPixels + TerrainChunkBleed);
        return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    private void DrawGeographicFeatures(Graphics g, int worldWidth, int worldHeight)
    {
        foreach (string featureType in new[] { "road", "river", "bridge" })
        {
            foreach (MapFeaturePath feature in data.Features.Where(feature =>
                         feature.Type.Equals(featureType, StringComparison.OrdinalIgnoreCase)))
            {
                PointF[] points = feature.Vertices.Select(vertex => new PointF(
                    (float)((vertex.Easting - data.OriginEasting) /
                            data.ExtentWidthMeters * worldWidth),
                    (float)((data.OriginNorthing + data.ExtentHeightMeters - vertex.Northing) /
                            data.ExtentHeightMeters * worldHeight))).ToArray();
                if (points.Length < 2) continue;

                if (featureType == "road")
                {
                    float scale = feature.FeatureClass switch { 3 => 0.14f, 2 => 0.11f, _ => 0.08f };
                    using var edge = new Pen(Color.FromArgb(66, 55, 43), Math.Max(4, cellSize * scale));
                    using var surface = new Pen(Color.FromArgb(187, 169, 132),
                        Math.Max(2, cellSize * scale * 0.55f));
                    edge.StartCap = edge.EndCap = LineCap.Round;
                    surface.StartCap = surface.EndCap = LineCap.Round;
                    g.DrawLines(edge, points);
                    g.DrawLines(surface, points);
                }
                else if (featureType == "river")
                {
                    float bankScale = feature.FeatureClass switch { 1 => 0.055f, 2 => 0.12f, _ => 0.20f };
                    float waterScale = feature.FeatureClass switch { 1 => 0.032f, 2 => 0.08f, _ => 0.15f };
                    using var bank = new Pen(Color.FromArgb(54, 74, 64), Math.Max(2, cellSize * bankScale));
                    using var water = new Pen(Color.FromArgb(48, 126, 174), Math.Max(1.5f, cellSize * waterScale));
                    bank.StartCap = bank.EndCap = LineCap.Round;
                    water.StartCap = water.EndCap = LineCap.Round;
                    g.DrawLines(bank, points);
                    g.DrawLines(water, points);
                }
                else
                {
                    float railScale = feature.FeatureClass switch
                    {
                        1 => 0.075f,
                        2 => 0.09f,
                        _ => 0.11f
                    };
                    float deckScale = feature.FeatureClass switch
                    {
                        1 => 0.042f,
                        2 => 0.052f,
                        _ => 0.064f
                    };
                    using var edge = new Pen(Color.FromArgb(35, 32, 29),
                        Math.Max(3, cellSize * railScale));
                    using var deck = new Pen(Color.FromArgb(224, 207, 169),
                        Math.Max(2, cellSize * deckScale));
                    edge.StartCap = edge.EndCap = LineCap.Square;
                    deck.StartCap = deck.EndCap = LineCap.Square;
                    g.DrawLines(edge, points);
                    g.DrawLines(deck, points);
                }
            }
        }

    }

    private void DrawTerrainCell(Graphics g, RectangleF b, MapCell cell)
    {
        Color baseColor = cell.Terrain.ToLowerInvariant() switch
        {
            "woods" => Color.FromArgb(57, 90, 45),
            "rough" => Color.FromArgb(126, 116, 74),
            "marsh" => Color.FromArgb(103, 125, 79),
            "water" => Color.FromArgb(69, 121, 149),
            "urban" or "built_up" => Color.FromArgb(144, 128, 104),
            "cultivated" => Color.FromArgb(151, 142, 77),
            _ => Color.FromArgb(133, 139, 79)
        };
        int elevationShade = Math.Clamp((cell.Elevation - 300) / 15, -12, 12);
        baseColor = Color.FromArgb(
            Math.Clamp(baseColor.R + elevationShade, 0, 255),
            Math.Clamp(baseColor.G + elevationShade, 0, 255),
            Math.Clamp(baseColor.B + elevationShade / 2, 0, 255));
        using var fill = new SolidBrush(baseColor);
        g.FillRectangle(fill, b);

        if (terrainAtlas is not null && !cell.Terrain.Equals("water", StringComparison.OrdinalIgnoreCase))
        {
            DrawTerrainTexture(g, b, cell);
            if (cell.Terrain.Equals("cultivated", StringComparison.OrdinalIgnoreCase))
                DrawCultivatedField(g, b, cell);
            int elevationAlpha = Math.Clamp(Math.Abs(cell.Elevation - 380) / 7, 0, 22);
            Color elevationColor = cell.Elevation >= 380
                ? Color.FromArgb(elevationAlpha, 238, 225, 174)
                : Color.FromArgb(elevationAlpha, 23, 34, 29);
            using var elevationWash = new SolidBrush(elevationColor);
            g.FillRectangle(elevationWash, b);
            return;
        }

        if (cell.Terrain.Equals("woods", StringComparison.OrdinalIgnoreCase))
        {
            using var shadow = new SolidBrush(Color.FromArgb(92, 20, 52, 29));
            using var crown = new SolidBrush(Color.FromArgb(180, 43, 82, 37));
            float r = Math.Max(4, b.Width * 0.10f);
            for (int i = 0; i < 5; i++)
            {
                float px = b.X + b.Width * (0.16f + ((i * 37 + cell.X * 11 + cell.Y * 7) % 68) / 100f);
                float py = b.Y + b.Height * (0.20f + ((i * 19 + cell.X * 5 + cell.Y * 13) % 56) / 100f);
                g.FillEllipse(shadow, px - r + 2, py - r + 3, r * 2, r * 2);
                g.FillEllipse(crown, px - r, py - r, r * 2, r * 2);
            }
        }
        else
        {
            using var texture = new Pen(Color.FromArgb(38, 242, 230, 150), 1);
            float y = b.Y + b.Height * 0.28f;
            g.DrawLine(texture, b.X + 6, y, b.Right - 8, y + 2);
            g.DrawLine(texture, b.X + 12, y + b.Height * 0.36f, b.Right - 5, y + b.Height * 0.34f);
        }

        if (cell.SettlementLevel > 0)
        {
            using var wall = new SolidBrush(Color.FromArgb(177, 175, 137, 102));
            using var roof = new SolidBrush(Color.FromArgb(190, 120, 54, 43));
            float scale = Math.Max(4, b.Width / 8);
            for (int i = 0; i < Math.Min(4, cell.SettlementLevel + 1); i++)
            {
                float px = b.X + 6 + (i % 2) * b.Width * 0.42f;
                float py = b.Y + 7 + (i / 2) * b.Height * 0.40f;
                g.FillRectangle(wall, px, py + scale * 0.4f, scale * 2.0f, scale * 1.2f);
                g.FillPolygon(roof,
                [
                    new PointF(px - 1, py + scale * 0.5f),
                    new PointF(px + scale, py),
                    new PointF(px + scale * 2.1f, py + scale * 0.5f)
                ]);
            }
        }
    }

    private void DrawTerrainTexture(Graphics g, RectangleF bounds, MapCell cell)
    {
        if (terrainAtlas is null) return;
        int tile = cell.Terrain.ToLowerInvariant() switch
        {
            "woods" => 2,
            "urban" or "built_up" => 3,
            "marsh" => 4,
            "rough" => 5,
            "cultivated" => 1,
            _ => 0
        };
        int column = tile % 3;
        int row = tile / 3;
        int panelWidth = terrainAtlas.Width / 3;
        int panelHeight = terrainAtlas.Height / 2;
        int padding = Math.Max(8, Math.Min(panelWidth, panelHeight) / 50);

        if (cell.Terrain.Equals("cultivated", StringComparison.OrdinalIgnoreCase))
        {
            DrawCultivatedTexture(g, bounds, cell, column, row, panelWidth, panelHeight, padding);
            return;
        }

        int sourceWidth = Math.Min(260, panelWidth - padding * 2);
        int sourceHeight = Math.Min(225, panelHeight - padding * 2);
        int availableX = Math.Max(1, panelWidth - padding * 2 - sourceWidth);
        int availableY = Math.Max(1, panelHeight - padding * 2 - sourceHeight);
        uint hash = unchecked((uint)(cell.X * 73856093) ^
                              (uint)(cell.Y * 19349663) ^
                              (uint)(tile * 83492791));
        int sourceX = column * panelWidth + padding + (int)(hash % (uint)availableX);
        int sourceY = row * panelHeight + padding + (int)((hash >> 12) % (uint)availableY);
        var source = new RectangleF(sourceX, sourceY, sourceWidth, sourceHeight);
        g.DrawImage(terrainAtlas, bounds, source, GraphicsUnit.Pixel);
    }

    private void DrawCultivatedTexture(
        Graphics g, RectangleF bounds, MapCell cell, int column, int row,
        int panelWidth, int panelHeight, int padding)
    {
        if (terrainAtlas is null) return;
        const int districtColumns = 3;
        const int districtRows = 2;
        int firstColumn = cell.X / districtColumns * districtColumns;
        int firstRow = cell.Y / districtRows * districtRows;

        RectangleF districtBounds = RectangleF.Empty;
        for (int y = firstRow; y < Math.Min(data.MapHeight, firstRow + districtRows); y++)
        for (int x = firstColumn; x < Math.Min(data.MapWidth, firstColumn + districtColumns); x++)
        {
            RectangleF cellBounds = WorldCellBounds(x, y);
            districtBounds = districtBounds.IsEmpty
                ? cellBounds
                : RectangleF.Union(districtBounds, cellBounds);
        }

        float contentLeft = column * panelWidth + padding;
        float contentTop = row * panelHeight + padding;
        float contentWidth = panelWidth - padding * 2;
        float contentHeight = panelHeight - padding * 2;
        var source = new RectangleF(
            contentLeft + (bounds.Left - districtBounds.Left) / districtBounds.Width * contentWidth,
            contentTop + (bounds.Top - districtBounds.Top) / districtBounds.Height * contentHeight,
            bounds.Width / districtBounds.Width * contentWidth,
            bounds.Height / districtBounds.Height * contentHeight);
        g.DrawImage(terrainAtlas, bounds, source, GraphicsUnit.Pixel);
    }

    private void DrawCultivatedField(Graphics g, RectangleF bounds, MapCell cell)
    {
        const int districtColumns = 3;
        const int districtRows = 2;
        int districtX = cell.X / districtColumns;
        int districtY = cell.Y / districtRows;
        uint hash = unchecked((uint)(districtX * 73856093) ^
                              (uint)(districtY * 19349663) ^ 0x9E3779B9u);

        Color[] fieldTints =
        [
            Color.FromArgb(62, 175, 151, 76),
            Color.FromArgb(55, 191, 166, 91),
            Color.FromArgb(58, 142, 119, 61),
            Color.FromArgb(52, 181, 139, 69)
        ];
        using (var tint = new SolidBrush(fieldTints[hash % (uint)fieldTints.Length]))
            g.FillRectangle(tint, bounds);

        float[] angles = [-24f, -12f, 0f, 14f, 27f];
        float angle = angles[(hash >> 8) % (uint)angles.Length];
        int firstColumn = districtX * districtColumns;
        int firstRow = districtY * districtRows;
        RectangleF districtStart = WorldCellBounds(firstColumn, firstRow);
        RectangleF districtEnd = WorldCellBounds(
            Math.Min(data.MapWidth - 1, firstColumn + districtColumns - 1),
            Math.Min(data.MapHeight - 1, firstRow + districtRows - 1));
        float centerX = (districtStart.Left + districtEnd.Right) / 2;
        float centerY = (districtStart.Top + districtEnd.Bottom) / 2;
        float reach = Math.Max(
            districtEnd.Right - districtStart.Left,
            districtEnd.Bottom - districtStart.Top) * 1.4f;
        float spacing = Math.Max(5, cellSize * 0.075f);

        GraphicsState state = g.Save();
        g.TranslateTransform(centerX, centerY);
        g.RotateTransform(angle);
        using var furrow = new Pen(Color.FromArgb(68, 67, 79, 43),
            Math.Max(1, cellSize * 0.012f));
        using var highlight = new Pen(Color.FromArgb(38, 229, 211, 137),
            Math.Max(1, cellSize * 0.008f));
        for (float row = -reach; row <= reach; row += spacing)
        {
            g.DrawLine(furrow, -reach, row, reach, row);
            g.DrawLine(highlight, -reach, row + Math.Max(1, spacing * 0.25f), reach,
                row + Math.Max(1, spacing * 0.25f));
        }
        g.Restore(state);
    }

    private static Bitmap? LoadTerrainAtlas()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "terrain_atlas_v1.png");
        if (!File.Exists(path)) return null;
        using var source = new Bitmap(path);
        return new Bitmap(source);
    }

    private void DrawLinearFeatures(Graphics g, RectangleF b, MapCell cell)
    {
        PointF center = new(b.X + b.Width / 2, b.Y + b.Height / 2);

        // Roads go down first. Water then interrupts ordinary road crossings;
        // only an authored bridge is allowed to put a road deck back on top.
        if (cell.RoadClass > 0)
            DrawRoadFeature(g, b, cell.RoadLinks, center, false);

        if (cell.RiverClass > 0)
        {
            float bankScale = cell.RiverClass switch { 1 => 0.08f, 2 => 0.15f, _ => 0.23f };
            float waterScale = cell.RiverClass switch { 1 => 0.045f, 2 => 0.10f, _ => 0.17f };
            using var bank = new Pen(Color.FromArgb(58, 77, 67), Math.Max(3, cellSize * bankScale));
            using var water = new Pen(Color.FromArgb(54, 128, 170), Math.Max(2, cellSize * waterScale));
            bank.StartCap = bank.EndCap = LineCap.Round;
            water.StartCap = water.EndCap = LineCap.Round;
            DrawConnections(g, b, cell.RiverLinks, bank, center);
            DrawConnections(g, b, cell.RiverLinks, water, center);
        }

        if (cell.HasBridge)
        {
            if (cell.RoadClass > 0)
                DrawRoadFeature(g, b, cell.RoadLinks, center, true);
            else
            {
                using var bridge = new Pen(Color.FromArgb(225, 205, 166), Math.Max(3, cellSize * 0.07f));
                g.DrawLine(bridge, b.X + b.Width * 0.18f, center.Y,
                    b.Right - b.Width * 0.18f, center.Y);
            }
        }
    }

    private void DrawRoadFeature(Graphics g, RectangleF b, int links, PointF center, bool bridge)
    {
        Color edgeColor = bridge ? Color.FromArgb(48, 43, 38) : Color.FromArgb(68, 58, 46);
        Color roadColor = bridge ? Color.FromArgb(220, 205, 170) : Color.FromArgb(179, 161, 124);
        using var edge = new Pen(edgeColor, Math.Max(6, cellSize * (bridge ? 0.16f : 0.14f)));
        using var road = new Pen(roadColor, Math.Max(3, cellSize * (bridge ? 0.09f : 0.08f)));
        edge.StartCap = edge.EndCap = LineCap.Round;
        road.StartCap = road.EndCap = LineCap.Round;
        DrawConnections(g, b, links, edge, center);
        DrawConnections(g, b, links, road, center);
    }

    private void DrawConnections(Graphics g, RectangleF b, int links, Pen pen, PointF center)
    {
        bool any = false;
        (int bit, float edgeX, float edgeY)[] directions =
        [
            (1, 0f, -0.5f),
            (2, 0.375f, -0.25f),
            (4, 0.375f, 0.25f),
            (8, 0f, 0.5f),
            (16, -0.375f, 0.25f),
            (32, -0.375f, -0.25f)
        ];

        foreach ((int bit, float edgeX, float edgeY) in directions)
        {
            if ((links & bit) == 0) continue;

            g.DrawLine(pen, center, new PointF(
                center.X + edgeX * cellSize,
                center.Y + edgeY * HexHeight));
            any = true;
        }
        if (!any) g.DrawEllipse(pen, center.X - 2, center.Y - 2, 4, 4);
    }

    private void DrawCounter(Graphics g, Rectangle bounds, ScenarioUnit unit, bool selected)
    {
        bool nato = unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase);
        Color fillColor = nato ? Color.FromArgb(23, 77, 164) : Color.FromArgb(166, 35, 37);
        Color borderColor = selected ? Color.FromArgb(245, 213, 42) : Color.FromArgb(224, 232, 218);
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(borderColor, selected ? 3 : 2);
        using var ink = new Pen(Color.White, Math.Max(1.5f, bounds.Width / 35f));
        using var text = new SolidBrush(Color.White);
        g.FillRectangle(fill, bounds);
        g.DrawRectangle(border, bounds);

        Rectangle symbol = new(bounds.X + bounds.Width / 5, bounds.Y + bounds.Height / 4,
            bounds.Width * 3 / 5, bounds.Height * 2 / 5);
        g.DrawRectangle(ink, symbol);
        string type = unit.Type.ToLowerInvariant();
        if (type.Contains("tank") || type.Contains("cavalry"))
            g.DrawEllipse(ink, symbol.X + 4, symbol.Y + 3, symbol.Width - 8, symbol.Height - 6);
        else if (type.Contains("headquarters"))
            g.DrawString("HQ", counterFont, text, symbol.X + 3, symbol.Y);
        else
        {
            g.DrawLine(ink, symbol.Left, symbol.Top, symbol.Right, symbol.Bottom);
            g.DrawLine(ink, symbol.Right, symbol.Top, symbol.Left, symbol.Bottom);
        }

        string echelon = type.Contains("team") ? "••" : "•••";
        g.DrawString(echelon, counterFont, text, bounds.X + 4, bounds.Y - 1);
        string strength = unit.Strength.ToString();
        SizeF size = g.MeasureString(strength, counterFont);
        g.DrawString(strength, counterFont, text, bounds.Right - size.Width - 3, bounds.Bottom - size.Height + 2);
    }

    private void DrawFooter(Graphics g, Rectangle bounds)
    {
        using var background = new SolidBrush(Color.FromArgb(17, 48, 52));
        using var active = new SolidBrush(Color.FromArgb(218, 191, 55));
        using var text = new SolidBrush(Color.FromArgb(207, 222, 218));
        using var line = new Pen(Color.FromArgb(112, 145, 151));
        g.FillRectangle(background, bounds);
        g.DrawLine(line, bounds.Left, bounds.Top, bounds.Right, bounds.Top);

        string[] commands = FooterCommands();
        int x = 20;
        foreach (string command in commands)
        {
            Rectangle button = FooterButtonBounds(bounds, x);
            g.DrawRectangle(line, button);
            bool highlighted = command is "SELECT" or "CONFIRM" or "EXECUTE" or
                "NEXT TURN" or "RESUME";
            g.DrawString(command, bodyBoldFont, highlighted ? active : text,
                x + 17, bounds.Top + 19);
            x += 116;
        }

        string help = turnPhase switch
        {
            _ when inspectingLosUnit is not null =>
                "MOUSE / ARROWS  INSPECT ANY HEX     DONE / ESC / L  CLOSE",
            TurnPhase.Executing => "SIMULTANEOUS MOVEMENT / FIRE     SPACE  PAUSE / RESUME     DRAG  PAN",
            TurnPhase.Review => "REVIEW RESULTS     ENTER / NEXT TURN  RETURN TO PLANNING",
            _ when targetingFireUnit is not null =>
                "CLICK / ENTER  SELECT ENEMY TARGET     ESC / CANCEL  ABORT",
            _ when plottingOrder is not null =>
                "CLICK / ENTER  WAYPOINT     DOUBLE-CLICK  CONFIRM     BACKSPACE  UNDO     ESC  CANCEL",
            _ => "RIGHT-CLICK UNIT  ORDERS     WASD / ARROWS  CURSOR     EDGE / DRAG  PAN"
        };
        SizeF size = g.MeasureString(help, bodyFont);
        g.DrawString(help, bodyFont, text, bounds.Right - size.Width - 18, bounds.Top + 21);

        string? notice = inspectingLosUnit is not null
            ? LosInspectionNotice()
            : targetingFireUnit is not null
            ? FirePreviewNotice()
            : plottingOrder switch
        {
            { IsReachable: false } => plottingOrder.FailureReason ?? "NO LEGAL ROUTE",
            { IsReachable: true } => $"ROUTE COST {plottingOrder.TotalCost / 10f:0.0}",
            _ => ExecutionNotice() ?? orderNotice
        };
        if (!string.IsNullOrWhiteSpace(notice))
        {
            SizeF noticeSize = g.MeasureString(notice, bodyBoldFont);
            float noticeX = Math.Max(x + 12, bounds.Right - size.Width - noticeSize.Width - 48);
            using var noticeBrush = new SolidBrush(plottingOrder is { IsReachable: false }
                ? Color.FromArgb(239, 112, 102)
                : Color.FromArgb(218, 191, 55));
            g.DrawString(notice, bodyBoldFont, noticeBrush, noticeX, bounds.Top + 21);
        }
    }

    private static Rectangle FooterButtonBounds(Rectangle footer, int x) =>
        new(x, footer.Top + 10, 105, 40);

    private string[] FooterCommands() => turnPhase switch
    {
        _ when inspectingLosUnit is not null => ["DONE"],
        TurnPhase.Executing => [executionPaused ? "RESUME" : "PAUSE"],
        TurnPhase.Review => ["NEXT TURN", "LOS"],
        _ when targetingFireUnit is not null => ["CONFIRM", "CANCEL"],
        _ when plottingOrder is not null => ["CONFIRM", "UNDO", "CANCEL"],
        _ => ["SELECT", "MOVE", "FIRE", "ASSAULT", "EXECUTE", "LOS"]
    };

    private string? ExecutionNotice()
    {
        if (turnPhase != TurnPhase.Executing || movementExecution is null) return null;
        int percent = (int)Math.Round(
            movementExecution.ElapsedGameSeconds / movementExecution.TurnDurationSeconds * 100);
        string state = executionPaused ? "PAUSED" : "RESOLVING";
        int natoMoving = movementExecution.Units.Count(unit => !unit.RouteComplete &&
            unit.Order.Unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase));
        int pactMoving = movementExecution.Units.Count(unit => !unit.RouteComplete &&
            !unit.Order.Unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase));
        int firePending = combatExecution?.Orders.Count(order =>
            order.State == FireOrderState.Executing) ?? 0;
        return $"{state} {percent}% — NATO {natoMoving} / PACT {pactMoving} MOVING, " +
               $"{firePending} FIRE ORDERS PENDING";
    }

    private string FirePreviewNotice()
    {
        if (targetingFireUnit is null || selectedCell is not Point cell)
            return orderNotice ?? "SELECT ENEMY TARGET";
        ScenarioUnit? target = EnemyAt(cell, targetingFireUnit.Faction);
        if (target is not null) return directFire.Validate(targetingFireUnit, target).Message;
        LineOfSightResult sight = lineOfSight.Trace(
            new Point(targetingFireUnit.X, targetingFireUnit.Y), cell);
        return sight.Quality == LineOfSightQuality.Blocked
            ? sight.Reason ?? "LINE OF SIGHT BLOCKED"
            : $"SELECT ENEMY TARGET — RANGE {sight.Range} / {targetingFireUnit.RangeCells}";
    }

    private void DrawMiniMap(Graphics g, Rectangle mapBounds)
    {
        Rectangle bounds = MiniMapBounds(mapBounds);
        using var frame = new SolidBrush(Color.FromArgb(220, 7, 25, 34));
        using var border = new Pen(Color.FromArgb(186, 210, 218), 2);
        g.FillRectangle(frame, bounds);
        g.DrawRectangle(border, bounds);

        Rectangle inner = MiniMapInnerBounds(mapBounds);
        GraphicsState miniMapState = g.Save();
        g.SetClip(inner, CombineMode.Intersect);
        float mapWorldWidth = (data.MapWidth - 1) * 0.75f + 1f;
        float mapWorldHeight = data.MapHeight + 0.5f;
        float sx = inner.Width / mapWorldWidth;
        float sy = inner.Height / mapWorldHeight;
        EnsureMiniMapLayer(inner.Size, sx, sy);
        if (miniMapLayer is not null)
            g.DrawImageUnscaled(miniMapLayer, inner.Location);

        foreach (ScenarioUnit unit in data.Units.Where(unit => !unit.IsDestroyed))
        {
            bool nato = unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase);
            PointF mapPosition = UnitMiniMapPosition(unit);
            float markerX = inner.X + mapPosition.X * sx;
            float markerY = inner.Y + mapPosition.Y * sy;
            RectangleF marker = new(markerX - 3.5f, markerY - 3.5f, 8, 8);
            using var halo = new Pen(Color.FromArgb(235, 239, 235), 1.5f);
            using var dot = new SolidBrush(nato
                ? Color.FromArgb(20, 91, 230)
                : Color.FromArgb(225, 38, 43));

            if (nato)
            {
                g.FillRectangle(dot, marker);
                g.DrawRectangle(halo, marker.X, marker.Y, marker.Width, marker.Height);
            }
            else
            {
                g.FillEllipse(dot, marker);
                g.DrawEllipse(halo, marker);
            }
        }

        float viewW = (mapBounds.Width / HexColumnStep) * 0.75f * sx;
        float viewH = (mapBounds.Height / HexHeight) * sy;
        using var viewPen = new Pen(Color.White, 2);
        g.DrawRectangle(viewPen,
            inner.X + cameraX * 0.75f * sx,
            inner.Y + cameraY * sy,
            viewW, viewH);
        g.Restore(miniMapState);
    }

    private void EnsureMiniMapLayer(Size size, float sx, float sy)
    {
        if (miniMapLayer is not null && miniMapLayer.Size == size)
            return;

        miniMapLayer?.Dispose();
        miniMapLayer = new Bitmap(size.Width, size.Height);
        using Graphics mini = Graphics.FromImage(miniMapLayer);
        mini.Clear(Color.FromArgb(67, 75, 50));
        mini.SetClip(new Rectangle(Point.Empty, size));
        using var woods = new SolidBrush(Color.FromArgb(43, 82, 39));
        using var clear = new SolidBrush(Color.FromArgb(121, 133, 75));
        using var water = new SolidBrush(Color.FromArgb(49, 112, 151));

        for (int y = 0; y < data.MapHeight; y++)
        for (int x = 0; x < data.MapWidth; x++)
        {
            MapCell cell = data.Cells[x, y];
            Brush brush = cell.RiverClass > 0
                ? water
                : cell.Terrain.Equals("woods", StringComparison.OrdinalIgnoreCase) ? woods : clear;
            float miniX = x * 0.75f * sx;
            float miniY = (y + ((x & 1) == 1 ? 0.5f : 0f)) * sy;
            mini.FillEllipse(brush, miniX, miniY, sx + 1, sy + 1);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        Rectangle map = MapBounds();
        if (e.Button == MouseButtons.Left && MiniMapBounds(map).Contains(e.Location))
        {
            navigatingMiniMap = true;
            Capture = true;
            CenterCameraFromMiniMap(e.Location, map);
            return;
        }
        if ((e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle ||
             e.Button == MouseButtons.Right) && map.Contains(e.Location))
        {
            dragging = true;
            dragMoved = false;
            dragButton = e.Button;
            dragStart = e.Location;
            dragCameraX = cameraX;
            dragCameraY = cameraY;
            Capture = true;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (navigatingMiniMap)
        {
            CenterCameraFromMiniMap(e.Location, MapBounds());
        }
        else if (dragging)
        {
            int deltaX = e.X - dragStart.X;
            int deltaY = e.Y - dragStart.Y;
            if (!dragMoved && deltaX * deltaX + deltaY * deltaY < 16)
            {
                base.OnMouseMove(e);
                return;
            }

            dragMoved = true;
            Cursor = Cursors.SizeAll;
            cameraX = dragCameraX - (e.X - dragStart.X) / HexColumnStep;
            cameraY = dragCameraY - (e.Y - dragStart.Y) / HexHeight;
            ClampCamera();
            Invalidate();
        }
        else if ((plottingOrder is not null || targetingFireUnit is not null ||
                  inspectingLosUnit is not null) &&
                 MapBounds().Contains(e.Location))
        {
            Point? hover = HitCell(e.Location);
            if (hover is Point cell && selectedCell != cell)
            {
                selectedCell = cell;
                if (plottingOrder is not null) RebuildRoute(plottingOrder);
                Invalidate();
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (navigatingMiniMap)
        {
            navigatingMiniMap = false;
            Capture = false;
            return;
        }
        if (dragging)
        {
            dragging = false;
            Cursor = Cursors.Default;
            Capture = false;
            if (!dragMoved)
            {
                if (dragButton == MouseButtons.Left)
                {
                    if (inspectingLosUnit is not null)
                    {
                        selectedCell = HitCell(e.Location) ?? selectedCell;
                        Invalidate();
                    }
                    else if (targetingFireUnit is not null)
                    {
                        Point? cell = HitCell(e.Location);
                        if (cell is Point target)
                        {
                            selectedCell = target;
                            ConfirmFireTargetAtCursor();
                        }
                    }
                    else if (plottingOrder is not null)
                    {
                        Point? cell = HitCell(e.Location);
                        if (cell is Point waypoint) AddWaypoint(waypoint);
                    }
                    else SelectAt(e.Location);
                }
                else if (dragButton == MouseButtons.Right)
                {
                    if (inspectingLosUnit is not null)
                    {
                        ToggleLosInspection();
                    }
                    else if (targetingFireUnit is not null)
                    {
                        CancelFireTargeting();
                    }
                    else if (plottingOrder is not null)
                    {
                        CancelPlotting();
                    }
                    else
                    {
                        SelectAt(e.Location);
                        if (selectedUnit is not null) ShowUnitActionMenu(e.Location);
                    }
                }
            }
            return;
        }
        if (e.Button == MouseButtons.Left && FooterBounds().Contains(e.Location))
        {
            HandleFooterClick(e.Location);
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && plottingOrder is not null && MapBounds().Contains(e.Location))
            ConfirmPlotting();
        base.OnMouseDoubleClick(e);
    }

    private void HandleFooterClick(Point point)
    {
        Rectangle footer = FooterBounds();
        int index = (point.X - 20) / 116;
        string[] commands = FooterCommands();
        if (point.X < 20 || index < 0 || index >= commands.Length ||
            !FooterButtonBounds(footer, 20 + index * 116).Contains(point))
            return;

        if (inspectingLosUnit is not null)
        {
            ToggleLosInspection();
            return;
        }

        if (turnPhase == TurnPhase.Executing)
        {
            if (index == 0) ToggleExecutionPause();
            return;
        }
        if (turnPhase == TurnPhase.Review)
        {
            if (index == 0) BeginNextTurn();
            else if (index == 1) ToggleLosInspection();
            return;
        }

        if (targetingFireUnit is not null)
        {
            if (index == 0) ConfirmFireTargetAtCursor();
            else if (index == 1) CancelFireTargeting();
            return;
        }

        if (plottingOrder is not null)
        {
            if (index == 0) ConfirmPlotting();
            else if (index == 1) UndoWaypoint();
            else if (index == 2) CancelPlotting();
            return;
        }

        if (index == 1 && selectedUnit is not null)
            BeginMoveOrder(OrderPosture.Tactical);
        else if (index == 2)
            BeginFireOrder();
        else if (index == 3)
            ShowDeferredAction("ASSAULT — W2.6");
        else if (index == 4)
            BeginExecution();
        else if (index == 5)
            ToggleLosInspection();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e.Delta != 0 && MapBounds().Contains(e.Location))
            ZoomAt(e.Location, MathF.Pow(1.2f, e.Delta / 120f));
        base.OnMouseWheel(e);
    }

    private void SelectAt(Point point)
    {
        Point? hit = HitCell(point);
        if (hit is not Point cell) return;

        selectedCell = cell;
        selectedUnit = data.Units.LastOrDefault(unit => !unit.IsDestroyed &&
            unit.X == cell.X && unit.Y == cell.Y);
        Invalidate();
    }

    private Point? HitCell(Point point)
    {
        Rectangle map = MapBounds();
        if (!map.Contains(point)) return null;
        int approximateX = (int)Math.Round(cameraX + (point.X - map.X - cellSize / 2) / HexColumnStep);
        int approximateY = (int)Math.Round(cameraY + (point.Y - map.Y) / HexHeight -
            (((approximateX & 1) == 1 ? 0.5f : 0f)));
        for (int y = approximateY - 2; y <= approximateY + 2; y++)
        for (int x = approximateX - 2; x <= approximateX + 2; x++)
        {
            if (x < 0 || x >= data.MapWidth || y < 0 || y >= data.MapHeight) continue;
            RectangleF bounds = CellBounds(map, x, y);
            using GraphicsPath hex = HexPath(bounds);
            if (hex.IsVisible(point)) return new Point(x, y);
        }
        return null;
    }

    private void ZoomAt(Point point, float factor)
    {
        Rectangle map = MapBounds();
        float anchorX = cameraX + (point.X - map.X) / HexColumnStep;
        float anchorY = cameraY + (point.Y - map.Y) / HexHeight;
        cellSize = Math.Clamp(cellSize * factor, 34f, 96f);
        cameraX = anchorX - (point.X - map.X) / HexColumnStep;
        cameraY = anchorY - (point.Y - map.Y) / HexHeight;
        ClampCamera();
        Invalidate();
    }

    private RectangleF CellBounds(Rectangle mapBounds, int x, int y) => new(
        mapBounds.X + (x - cameraX) * HexColumnStep,
        mapBounds.Y + (y - cameraY + ((x & 1) == 1 ? 0.5f : 0f)) * HexHeight,
        cellSize,
        HexHeight);

    private PointF CellCenter(Rectangle mapBounds, Point cell)
    {
        RectangleF bounds = CellBounds(mapBounds, cell.X, cell.Y);
        return new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private PointF UnitScreenCenter(Rectangle mapBounds, ScenarioUnit unit)
    {
        UnitMovementExecution? execution = movementExecution?.ForUnit(unit);
        if (execution is null || execution.RouteComplete)
            return CellCenter(mapBounds, new Point(unit.X, unit.Y));

        PointF from = CellCenter(mapBounds, execution.Current);
        PointF to = CellCenter(mapBounds, execution.Next);
        float progress = execution.VisualProgress;
        return new PointF(
            from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress);
    }

    private PointF UnitMiniMapPosition(ScenarioUnit unit)
    {
        UnitMovementExecution? execution = movementExecution?.ForUnit(unit);
        if (execution is null || execution.RouteComplete)
            return new PointF(unit.X * 0.75f,
                unit.Y + ((unit.X & 1) == 1 ? 0.5f : 0f));
        float progress = execution.VisualProgress;
        float fromX = execution.Current.X * 0.75f;
        float fromY = execution.Current.Y + ((execution.Current.X & 1) == 1 ? 0.5f : 0f);
        float toX = execution.Next.X * 0.75f;
        float toY = execution.Next.Y + ((execution.Next.X & 1) == 1 ? 0.5f : 0f);
        return new PointF(
            fromX + (toX - fromX) * progress,
            fromY + (toY - fromY) * progress);
    }

    private RectangleF WorldCellBounds(int x, int y) => new(
        x * HexColumnStep,
        (y + ((x & 1) == 1 ? 0.5f : 0f)) * HexHeight,
        cellSize,
        HexHeight);

    private static GraphicsPath HexPath(RectangleF bounds)
    {
        float quarter = bounds.Width * 0.25f;
        var path = new GraphicsPath();
        path.AddPolygon(
        [
            new PointF(bounds.Left + quarter, bounds.Top),
            new PointF(bounds.Right - quarter, bounds.Top),
            new PointF(bounds.Right, bounds.Top + bounds.Height / 2),
            new PointF(bounds.Right - quarter, bounds.Bottom),
            new PointF(bounds.Left + quarter, bounds.Bottom),
            new PointF(bounds.Left, bounds.Top + bounds.Height / 2)
        ]);
        path.CloseFigure();
        return path;
    }

    private MapCell? SelectedMapCell() => selectedCell is Point p ? data.Cells[p.X, p.Y] : null;

    private Rectangle MapBounds() => new(
        InspectorWidth,
        HeaderHeight,
        Math.Max(1, Width - InspectorWidth),
        Math.Max(1, Height - HeaderHeight - FooterHeight));

    private Rectangle FooterBounds() => new(0, Height - FooterHeight, Width, FooterHeight);

    private static Rectangle MiniMapBounds(Rectangle mapBounds) => new(
        mapBounds.Right - MiniMapWidth - 16,
        mapBounds.Bottom - MiniMapHeight - 16,
        MiniMapWidth,
        MiniMapHeight);

    private static Rectangle MiniMapInnerBounds(Rectangle mapBounds) =>
        Rectangle.Inflate(MiniMapBounds(mapBounds), -7, -7);

    private void CenterCameraFromMiniMap(Point point, Rectangle mapBounds)
    {
        Rectangle inner = MiniMapInnerBounds(mapBounds);
        float mapWorldWidth = (data.MapWidth - 1) * 0.75f + 1f;
        float mapWorldHeight = data.MapHeight + 0.5f;
        float sx = inner.Width / mapWorldWidth;
        float sy = inner.Height / mapWorldHeight;
        float worldX = (Math.Clamp(point.X, inner.Left, inner.Right) - inner.Left) / sx;
        float worldY = (Math.Clamp(point.Y, inner.Top, inner.Bottom) - inner.Top) / sy;
        float visibleWorldWidth = mapBounds.Width / cellSize;
        float visibleRows = mapBounds.Height / HexHeight;

        cameraX = (worldX - visibleWorldWidth / 2f) / 0.75f;
        cameraY = worldY - visibleRows / 2f;
        ClampCamera();
        Invalidate();
    }

    private void ClampCamera()
    {
        Rectangle map = MapBounds();
        float visibleColumns = Math.Max(1, map.Width / HexColumnStep);
        float visibleRows = Math.Max(1, map.Height / HexHeight);
        float mapWidthInColumnSteps = data.MapWidth - 1 + cellSize / HexColumnStep;
        cameraX = Math.Clamp(cameraX, 0, Math.Max(0, mapWidthInColumnSteps - visibleColumns));
        cameraY = Math.Clamp(cameraY, 0, Math.Max(0, data.MapHeight + 0.5f - visibleRows));
    }

    private static string MoraleText(int morale) => morale switch
    {
        >= 85 => "VETERAN",
        >= 70 => "STEADY",
        >= 50 => "SHAKEN",
        _ => "BROKEN"
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            edgeScrollTimer.Dispose();
            executionTimer.Dispose();
            unitActionMenu.Dispose();
            titleFont.Dispose();
            menuFont.Dispose();
            headingFont.Dispose();
            bodyFont.Dispose();
            bodyBoldFont.Dispose();
            counterFont.Dispose();
            terrainAtlas?.Dispose();
            foreach (Bitmap chunk in terrainChunks.Values) chunk.Dispose();
            terrainChunks.Clear();
            miniMapLayer?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal enum TurnPhase
{
    Planning,
    Executing,
    Review
}

internal sealed class TacticalMenuRenderer : ToolStripProfessionalRenderer
{
    public TacticalMenuRenderer() : base(new TacticalColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? Color.FromArgb(226, 235, 226)
            : Color.FromArgb(101, 128, 135);
        base.OnRenderItemText(e);
    }
}

internal sealed class TacticalColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(10, 38, 48);
    public override Color MenuItemSelected => Color.FromArgb(32, 78, 84);
    public override Color MenuItemBorder => Color.FromArgb(222, 192, 53);
    public override Color MenuBorder => Color.FromArgb(116, 157, 178);
    public override Color SeparatorDark => Color.FromArgb(63, 100, 111);
    public override Color SeparatorLight => Color.FromArgb(63, 100, 111);
}
