using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BattalionFulda;

internal sealed class BattlefieldView : Control
{
    private const int HeaderHeight = 44;
    private const int FooterHeight = 62;
    private const int InspectorWidth = 238;
    private const int MiniMapWidth = 220;
    private const int MiniMapHeight = 150;

    private readonly GameData data;
    private readonly Font titleFont = new("Bahnschrift SemiCondensed", 18, FontStyle.Bold);
    private readonly Font menuFont = new("Segoe UI", 11, FontStyle.Regular);
    private readonly Font headingFont = new("Bahnschrift SemiCondensed", 13, FontStyle.Bold);
    private readonly Font bodyFont = new("Segoe UI", 10, FontStyle.Regular);
    private readonly Font bodyBoldFont = new("Segoe UI Semibold", 10, FontStyle.Bold);
    private readonly Font counterFont = new("Arial", 9, FontStyle.Bold);
    private readonly Bitmap? terrainAtlas = LoadTerrainAtlas();
    private Bitmap? terrainLayer;
    private float terrainLayerCellSize;
    private Bitmap? miniMapLayer;
    private readonly System.Windows.Forms.Timer edgeScrollTimer = new() { Interval = 16 };
    private float cellSize = 72f;
    private float cameraX = 4.5f;
    private float cameraY = 14.5f;
    private ScenarioUnit? selectedUnit;
    private Point? selectedCell = new Point(10, 24);
    private Point dragStart;
    private float dragCameraX;
    private float dragCameraY;
    private bool dragging;
    private bool dragMoved;
    private MouseButtons dragButton;
    private bool navigatingMiniMap;

    private float HexHeight => cellSize * 0.8660254f;
    private float HexColumnStep => cellSize * 0.75f;

    public BattlefieldView(GameData data)
    {
        this.data = data;
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        edgeScrollTimer.Tick += (_, _) => EdgeScrollTick();
        edgeScrollTimer.Start();
    }

    public bool HandleKey(Keys key)
    {
        switch (key)
        {
            case Keys.A or Keys.Left: MoveCursor(-1, 0); return true;
            case Keys.D or Keys.Right: MoveCursor(1, 0); return true;
            case Keys.W or Keys.Up: MoveCursor(0, -1); return true;
            case Keys.S or Keys.Down: MoveCursor(0, 1); return true;
            case Keys.Enter: SelectUnitAtCursor(); return true;
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
        Point current = selectedCell ?? new Point(GameData.MapWidth / 2, GameData.MapHeight / 2);
        selectedCell = new Point(
            Math.Clamp(current.X + dx, 0, GameData.MapWidth - 1),
            Math.Clamp(current.Y + dy, 0, GameData.MapHeight - 1));
        EnsureCursorVisible();
        Invalidate();
    }

    private void SelectUnitAtCursor()
    {
        if (selectedCell is not Point cell) return;
        selectedUnit = data.Units.LastOrDefault(unit => unit.X == cell.X && unit.Y == cell.Y);
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
        if (!activationArea.Contains(pointer) || MiniMapBounds(map).Contains(pointer))
            return;

        float oldX = cameraX;
        float oldY = cameraY;
        cameraX += EdgeScrollDelta(pointer.X, map.Left, map.Right);
        cameraY += EdgeScrollDelta(pointer.Y, map.Top, map.Bottom);
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

        string turn = "TURN 1     06 JUN 1985     0600";
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
        if (cell.RoadClass > 0) features.Add("ROAD");
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

    private void DrawMap(Graphics g, Rectangle bounds)
    {
        using Region oldClip = g.Clip;
        g.SetClip(bounds);
        EnsureTerrainLayer();
        if (terrainLayer is not null)
        {
            InterpolationMode oldInterpolation = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(terrainLayer, bounds,
                cameraX * HexColumnStep, cameraY * HexHeight,
                bounds.Width, bounds.Height, GraphicsUnit.Pixel);
            g.InterpolationMode = oldInterpolation;
        }

        int firstX = Math.Max(0, (int)Math.Floor(cameraX) - 2);
        int firstY = Math.Max(0, (int)Math.Floor(cameraY) - 2);
        int lastX = Math.Min(GameData.MapWidth - 1,
            firstX + (int)Math.Ceiling(bounds.Width / HexColumnStep) + 4);
        int lastY = Math.Min(GameData.MapHeight - 1,
            firstY + (int)Math.Ceiling(bounds.Height / HexHeight) + 4);

        foreach (ScenarioUnit unit in data.Units)
        {
            if (unit.X < firstX - 1 || unit.X > lastX + 1 || unit.Y < firstY - 1 || unit.Y > lastY + 1)
                continue;
            RectangleF cb = CellBounds(bounds, unit.X, unit.Y);
            int w = Math.Clamp((int)(cellSize * 0.72f), 32, 54);
            int h = Math.Clamp((int)(cellSize * 0.62f), 29, 48);
            var counterBounds = new Rectangle((int)(cb.X + (cb.Width - w) / 2),
                (int)(cb.Y + (cb.Height - h) / 2), w, h);
            DrawCounter(g, counterBounds, unit, ReferenceEquals(unit, selectedUnit));
        }

        if (selectedCell is Point selected)
        {
            RectangleF cb = CellBounds(bounds, selected.X, selected.Y);
            using var selection = new Pen(Color.FromArgb(240, 210, 42), 3);
            using GraphicsPath selectedHex = HexPath(RectangleF.Inflate(cb, -2, -2));
            g.DrawPath(selection, selectedHex);
        }

        g.Clip = oldClip;
    }

    private void EnsureTerrainLayer()
    {
        if (terrainLayer is not null && Math.Abs(terrainLayerCellSize - cellSize) < 0.01f)
            return;

        terrainLayer?.Dispose();
        int width = (int)Math.Ceiling((GameData.MapWidth - 1) * HexColumnStep + cellSize) + 2;
        int height = (int)Math.Ceiling((GameData.MapHeight + 0.5f) * HexHeight) + 2;
        terrainLayer = new Bitmap(width, height);
        terrainLayerCellSize = cellSize;

        using Graphics layer = Graphics.FromImage(terrainLayer);
        layer.SmoothingMode = SmoothingMode.AntiAlias;
        layer.InterpolationMode = InterpolationMode.HighQualityBilinear;
        layer.Clear(Color.FromArgb(67, 75, 50));

        for (int y = 0; y < GameData.MapHeight; y++)
        for (int x = 0; x < GameData.MapWidth; x++)
        {
            RectangleF cellBounds = WorldCellBounds(x, y);
            using GraphicsPath hex = HexPath(cellBounds);
            GraphicsState state = layer.Save();
            layer.SetClip(hex, CombineMode.Intersect);
            DrawTerrainCell(layer, cellBounds, data.Cells[x, y]);
            layer.Restore(state);
        }

        for (int y = 0; y < GameData.MapHeight; y++)
        for (int x = 0; x < GameData.MapWidth; x++)
            DrawLinearFeatures(layer, WorldCellBounds(x, y), data.Cells[x, y]);

        using var gridPen = new Pen(Color.FromArgb(70, 31, 42, 34), 1);
        for (int y = 0; y < GameData.MapHeight; y++)
        for (int x = 0; x < GameData.MapWidth; x++)
        {
            using GraphicsPath hex = HexPath(WorldCellBounds(x, y));
            layer.DrawPath(gridPen, hex);
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

        string[] commands = ["SELECT", "MOVE", "FIRE", "ASSAULT", "INFO"];
        int x = 20;
        foreach (string command in commands)
        {
            Rectangle button = new(x, bounds.Top + 10, 105, 40);
            g.DrawRectangle(line, button);
            g.DrawString(command, bodyBoldFont, command == "SELECT" ? active : text, x + 17, bounds.Top + 19);
            x += 116;
        }

        string help = "WASD / ARROWS  CURSOR     ENTER  SELECT     EDGE / DRAG  PAN     WHEEL  ZOOM";
        SizeF size = g.MeasureString(help, bodyFont);
        g.DrawString(help, bodyFont, text, bounds.Right - size.Width - 18, bounds.Top + 21);
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
        float mapWorldWidth = (GameData.MapWidth - 1) * 0.75f + 1f;
        float mapWorldHeight = GameData.MapHeight + 0.5f;
        float sx = inner.Width / mapWorldWidth;
        float sy = inner.Height / mapWorldHeight;
        EnsureMiniMapLayer(inner.Size, sx, sy);
        if (miniMapLayer is not null)
            g.DrawImageUnscaled(miniMapLayer, inner.Location);

        foreach (ScenarioUnit unit in data.Units)
        {
            bool nato = unit.Faction.Equals("NATO", StringComparison.OrdinalIgnoreCase);
            float markerX = inner.X + unit.X * 0.75f * sx;
            float markerY = inner.Y + (unit.Y + ((unit.X & 1) == 1 ? 0.5f : 0f)) * sy;
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

        for (int y = 0; y < GameData.MapHeight; y++)
        for (int x = 0; x < GameData.MapWidth; x++)
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
            if (dragButton == MouseButtons.Left && !dragMoved)
                SelectAt(e.Location);
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ZoomAt(e.Location, e.Delta > 0 ? 1.12f : 0.89f);
        base.OnMouseWheel(e);
    }

    private void SelectAt(Point point)
    {
        Rectangle map = MapBounds();
        if (!map.Contains(point)) return;
        Point? hit = null;
        for (int y = 0; y < GameData.MapHeight && hit is null; y++)
        for (int x = 0; x < GameData.MapWidth; x++)
        {
            RectangleF bounds = CellBounds(map, x, y);
            if (!bounds.Contains(point)) continue;
            using GraphicsPath hex = HexPath(bounds);
            if (hex.IsVisible(point))
            {
                hit = new Point(x, y);
                break;
            }
        }
        if (hit is not Point cell) return;

        selectedCell = cell;
        selectedUnit = data.Units.LastOrDefault(unit => unit.X == cell.X && unit.Y == cell.Y);
        Invalidate();
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
        float mapWorldWidth = (GameData.MapWidth - 1) * 0.75f + 1f;
        float mapWorldHeight = GameData.MapHeight + 0.5f;
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
        float mapWidthInColumnSteps = GameData.MapWidth - 1 + cellSize / HexColumnStep;
        cameraX = Math.Clamp(cameraX, 0, Math.Max(0, mapWidthInColumnSteps - visibleColumns));
        cameraY = Math.Clamp(cameraY, 0, Math.Max(0, GameData.MapHeight + 0.5f - visibleRows));
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
            titleFont.Dispose();
            menuFont.Dispose();
            headingFont.Dispose();
            bodyFont.Dispose();
            bodyBoldFont.Dispose();
            counterFont.Dispose();
            terrainAtlas?.Dispose();
            terrainLayer?.Dispose();
            miniMapLayer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
