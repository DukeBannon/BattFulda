#include <dos/dos.h>
#include <exec/execbase.h>
#include <exec/libraries.h>
#include <graphics/gfxbase.h>
#include <graphics/modeid.h>
#include <graphics/rastport.h>
#include <intuition/intuition.h>
#include <intuition/intuitionbase.h>
#include <proto/dos.h>
#include <proto/exec.h>
#include <proto/graphics.h>
#include <proto/intuition.h>
#include <utility/tagitem.h>

#define SCREEN_WIDTH 640
#define SCREEN_HEIGHT 256
#define MAP_LEFT 24
#define MAP_TOP 48
#define CELL_SIZE 16
#define VIEW_WIDTH 20
#define VIEW_HEIGHT 11
#define MAX_MAP_CELLS 300
#define MAX_UNITS 31

#define TILE_CLEAR 0
#define TILE_ROAD 1
#define TILE_WOODS 2
#define TILE_WATER 3
#define TILE_TOWN 4

#define COLOR_BACKGROUND 0
#define COLOR_TEXT 1
#define COLOR_HIGHLIGHT 2
#define COLOR_CLEAR 3
#define COLOR_CLEAR_ALT 4
#define COLOR_GRID 5
#define COLOR_ROAD 6
#define COLOR_WOODS 7
#define COLOR_WATER 8
#define COLOR_TOWN 9
#define COLOR_NATO 10
#define COLOR_WARSAW 11
#define COLOR_BLACK 12
#define COLOR_WHITE 13

struct ExecBase *SysBase;
struct GfxBase *GfxBase;
struct IntuitionBase *IntuitionBase;
struct DosLibrary *DOSBase;

struct TacticalUnit {
    UBYTE side;
    UBYTE type;
    UBYTE x;
    UBYTE y;
    UBYTE strength;
};

static struct Screen *game_screen;
static struct Window *game_window;
static UBYTE map_width;
static UBYTE map_height;
static UBYTE map_tiles[MAX_MAP_CELLS];
static struct TacticalUnit units[MAX_UNITS];
static UBYTE unit_count;
static UBYTE cursor_x;
static UBYTE cursor_y;
static UBYTE camera_y;
static BYTE selected_unit = -1;

static const char *const terrain_names[] = {
    "CLEAR", "ROAD", "WOODS", "WATER", "TOWN"
};
static const UBYTE terrain_lengths[] = {5, 4, 5, 5, 4};
static const char *const side_names[] = {"NATO", "WARSAW PACT"};
static const UBYTE side_lengths[] = {4, 11};
static const char *const type_names[] = {"ARMOR", "INFANTRY", "RECON"};
static const UBYTE type_lengths[] = {5, 8, 5};
static const UBYTE movement_values[] = {6, 4, 8};

static void draw_text(struct RastPort *rp, WORD x, WORD y,
                      const char *text, UWORD length, UWORD color)
{
    SetAPen(rp, color);
    Move(rp, x, y);
    Text(rp, (CONST_STRPTR)text, length);
}

static UBYTE format_number(UBYTE value, char *buffer)
{
    if (value >= 10) {
        buffer[0] = '1';
        buffer[1] = (char)('0' + value - 10);
        return 2;
    }
    buffer[0] = (char)('0' + value);
    return 1;
}

static BOOL read_file(const char *name, UBYTE *buffer, LONG capacity, LONG *size)
{
    BPTR file = Open((CONST_STRPTR)name, MODE_OLDFILE);
    LONG bytes_read;
    if (!file) return FALSE;
    bytes_read = Read(file, buffer, capacity);
    Close(file);
    if (bytes_read < 0) return FALSE;
    *size = bytes_read;
    return TRUE;
}

static BOOL load_game_data(void)
{
    static UBYTE buffer[512];
    LONG size;
    UBYTE index;
    UWORD source;
    UWORD cell_count;

    if (!read_file("map.bin", buffer, sizeof(buffer), &size) || size < 2)
        return FALSE;
    map_width = buffer[0];
    map_height = buffer[1];
    cell_count = (UWORD)map_width * map_height;
    if (map_width != VIEW_WIDTH || map_height < VIEW_HEIGHT ||
        cell_count > MAX_MAP_CELLS || size != (LONG)cell_count + 2)
        return FALSE;
    for (source = 0; source < cell_count; ++source) {
        if (buffer[source + 2] > TILE_TOWN) return FALSE;
        map_tiles[source] = buffer[source + 2];
    }

    if (!read_file("units.bin", buffer, sizeof(buffer), &size) || size < 1)
        return FALSE;
    unit_count = buffer[0];
    if (!unit_count || unit_count > MAX_UNITS || size != (LONG)unit_count * 5 + 1)
        return FALSE;
    source = 1;
    for (index = 0; index < unit_count; ++index) {
        units[index].side = buffer[source++];
        units[index].type = buffer[source++];
        units[index].x = buffer[source++];
        units[index].y = buffer[source++];
        units[index].strength = buffer[source++];
        if (units[index].side > 1 || units[index].type > 2 ||
            units[index].x >= map_width || units[index].y >= map_height)
            return FALSE;
    }

    if (!read_file("scenario.bin", buffer, sizeof(buffer), &size) || size != 4)
        return FALSE;
    if (buffer[0] != 1 || buffer[3] != unit_count ||
        buffer[1] >= map_width || buffer[2] >= map_height)
        return FALSE;
    cursor_x = buffer[1];
    cursor_y = buffer[2];
    camera_y = cursor_y >= VIEW_HEIGHT ? cursor_y - VIEW_HEIGHT + 1 : 0;
    return TRUE;
}

static BYTE unit_at(UBYTE x, UBYTE y)
{
    UBYTE index;
    for (index = 0; index < unit_count; ++index) {
        if (units[index].x == x && units[index].y == y) return (BYTE)index;
    }
    return -1;
}

static UBYTE tile_at(UBYTE x, UBYTE y)
{
    return map_tiles[(UWORD)y * map_width + x];
}

static void draw_box(struct RastPort *rp, WORD left, WORD top, WORD right,
                     WORD bottom, UWORD color)
{
    SetAPen(rp, color);
    Move(rp, left, top);
    Draw(rp, right, top);
    Draw(rp, right, bottom);
    Draw(rp, left, bottom);
    Draw(rp, left, top);
}

static void draw_terrain(struct RastPort *rp, UBYTE tile, WORD left, WORD top)
{
    UWORD fill = COLOR_CLEAR;
    if (tile == TILE_ROAD) fill = COLOR_ROAD;
    else if (tile == TILE_WOODS) fill = COLOR_WOODS;
    else if (tile == TILE_WATER) fill = COLOR_WATER;
    else if (tile == TILE_TOWN) fill = COLOR_TOWN;
    else if (((left >> 4) + (top >> 4)) & 1) fill = COLOR_CLEAR_ALT;

    SetAPen(rp, fill);
    RectFill(rp, left + 1, top + 1, left + CELL_SIZE - 2,
             top + CELL_SIZE - 2);
    draw_box(rp, left, top, left + CELL_SIZE - 1,
             top + CELL_SIZE - 1, COLOR_GRID);

    if (tile == TILE_ROAD) {
        SetAPen(rp, COLOR_TEXT);
        Move(rp, left + 1, top + 8);
        Draw(rp, left + 14, top + 8);
    } else if (tile == TILE_WOODS) {
        SetAPen(rp, COLOR_CLEAR_ALT);
        RectFill(rp, left + 4, top + 3, left + 6, top + 7);
        RectFill(rp, left + 9, top + 6, left + 11, top + 10);
    } else if (tile == TILE_WATER) {
        SetAPen(rp, COLOR_WHITE);
        Move(rp, left + 2, top + 6);
        Draw(rp, left + 6, top + 6);
        Move(rp, left + 8, top + 10);
        Draw(rp, left + 13, top + 10);
    } else if (tile == TILE_TOWN) {
        SetAPen(rp, COLOR_BLACK);
        RectFill(rp, left + 4, top + 4, left + 11, top + 12);
        SetAPen(rp, COLOR_TEXT);
        RectFill(rp, left + 7, top + 8, left + 8, top + 12);
    }
}

static void draw_unit(struct RastPort *rp, BYTE index, WORD left, WORD top)
{
    const struct TacticalUnit *unit = &units[(UBYTE)index];
    UWORD color = unit->side == 0 ? COLOR_NATO : COLOR_WARSAW;
    const char *symbol = unit->side == 0 ? "N" : "W";

    SetAPen(rp, color);
    RectFill(rp, left + 3, top + 3, left + 12, top + 12);
    draw_text(rp, left + 4, top + 12, symbol, 1, COLOR_BLACK);
    if (selected_unit == index)
        draw_box(rp, left + 1, top + 1, left + 14, top + 14,
                 COLOR_HIGHLIGHT);
}

static void draw_cell(struct RastPort *rp, UBYTE map_x, UBYTE map_y)
{
    BYTE unit;
    WORD left;
    WORD top;
    if (map_y < camera_y || map_y >= camera_y + VIEW_HEIGHT) return;
    left = MAP_LEFT + ((WORD)map_x << 4);
    top = MAP_TOP + ((WORD)(map_y - camera_y) << 4);
    draw_terrain(rp, tile_at(map_x, map_y), left, top);
    unit = unit_at(map_x, map_y);
    if (unit >= 0) draw_unit(rp, unit, left, top);
}

static void draw_cursor(struct RastPort *rp)
{
    WORD left = MAP_LEFT + ((WORD)cursor_x << 4);
    WORD top = MAP_TOP + ((WORD)(cursor_y - camera_y) << 4);
    draw_box(rp, left, top, left + CELL_SIZE - 1,
             top + CELL_SIZE - 1, COLOR_HIGHLIGHT);
    draw_box(rp, left + 1, top + 1, left + CELL_SIZE - 2,
             top + CELL_SIZE - 2, COLOR_HIGHLIGHT);
}

static void clear_panel(struct RastPort *rp)
{
    SetAPen(rp, COLOR_BACKGROUND);
    RectFill(rp, 368, 42, 639, 255);
}

static void draw_value(struct RastPort *rp, WORD y, const char *label,
                       UBYTE label_length, const char *value,
                       UBYTE value_length)
{
    draw_text(rp, 384, y, label, label_length, COLOR_TEXT);
    draw_text(rp, 488, y, value, value_length, COLOR_WHITE);
}

static void draw_panel(struct RastPort *rp)
{
    char number[2];
    UBYTE length;
    UBYTE tile = tile_at(cursor_x, cursor_y);
    clear_panel(rp);
    draw_text(rp, 384, 58, "TACTICAL DISPLAY", 16, COLOR_HIGHLIGHT);

    if (selected_unit >= 0) {
        const struct TacticalUnit *unit = &units[(UBYTE)selected_unit];
        tile = tile_at(unit->x, unit->y);
        draw_text(rp, 384, 82, "SELECTED UNIT", 13, COLOR_HIGHLIGHT);
        draw_value(rp, 102, "SIDE:", 5, side_names[unit->side],
                   side_lengths[unit->side]);
        draw_value(rp, 118, "TYPE:", 5, type_names[unit->type],
                   type_lengths[unit->type]);
        length = format_number(unit->strength, number);
        draw_value(rp, 134, "STRENGTH:", 9, number, length);
        length = format_number(movement_values[unit->type], number);
        draw_value(rp, 150, "MOVE:", 5, number, length);
        draw_value(rp, 166, "TERRAIN:", 8, terrain_names[tile],
                   terrain_lengths[tile]);
        draw_value(rp, 182, "FORMATION:", 10,
                   unit->side == 0 ? "TASK FORCE ALPHA" : "GUARDS REGIMENT",
                   unit->side == 0 ? 16 : 15);
        draw_text(rp, 384, 222, "RIGHT CLICK/ESC: CANCEL", 23, COLOR_TEXT);
    } else {
        draw_text(rp, 384, 86, "NO UNIT SELECTED", 16, COLOR_TEXT);
        draw_value(rp, 110, "TERRAIN:", 8, terrain_names[tile],
                   terrain_lengths[tile]);
        draw_text(rp, 384, 150, "MOUSE/WASD: MOVE", 16, COLOR_TEXT);
        draw_text(rp, 384, 166, "LEFT CLICK/ENTER: SELECT", 24, COLOR_TEXT);
        draw_text(rp, 384, 182, "ESC: EXIT", 9, COLOR_TEXT);
    }

    draw_text(rp, 384, 246, "A1 MAP + UNIT INTERACTION", 25, COLOR_HIGHLIGHT);
}

static void update_cursor_terrain(struct RastPort *rp)
{
    UBYTE tile = tile_at(cursor_x, cursor_y);
    SetAPen(rp, COLOR_BACKGROUND);
    RectFill(rp, 488, 98, 624, 112);
    draw_text(rp, 488, 110, terrain_names[tile], terrain_lengths[tile],
              COLOR_WHITE);
}

static void draw_map(struct RastPort *rp)
{
    UBYTE x;
    UBYTE view_y;
    for (view_y = 0; view_y < VIEW_HEIGHT; ++view_y) {
        for (x = 0; x < VIEW_WIDTH; ++x)
            draw_cell(rp, x, camera_y + view_y);
    }
    draw_cursor(rp);
}

static void draw_scene(struct RastPort *rp)
{
    SetRast(rp, COLOR_BACKGROUND);
    draw_text(rp, 24, 22, "BATTALION: FULDA", 16, COLOR_HIGHLIGHT);
    draw_text(rp, 24, 37, "FULDA GAP - A1", 14, COLOR_TEXT);
    draw_map(rp);
    draw_panel(rp);
}

static void move_cursor(struct RastPort *rp, UBYTE x, UBYTE y)
{
    UBYTE old_x = cursor_x;
    UBYTE old_y = cursor_y;
    UBYTE old_camera = camera_y;
    UBYTE maximum_camera = map_height - VIEW_HEIGHT;
    if (x >= map_width || y >= map_height) return;
    if (x == cursor_x && y == cursor_y) return;

    cursor_x = x;
    cursor_y = y;
    if (cursor_y < camera_y) camera_y = cursor_y;
    else if (cursor_y >= camera_y + VIEW_HEIGHT)
        camera_y = cursor_y - VIEW_HEIGHT + 1;
    if (camera_y > maximum_camera) camera_y = maximum_camera;

    if (camera_y != old_camera) draw_map(rp);
    else {
        draw_cell(rp, old_x, old_y);
        draw_cursor(rp);
    }
    if (selected_unit < 0) update_cursor_terrain(rp);
}

static void select_unit(struct RastPort *rp)
{
    BYTE found = unit_at(cursor_x, cursor_y);
    if (selected_unit >= 0) {
        BYTE old = selected_unit;
        selected_unit = -1;
        draw_cell(rp, units[(UBYTE)old].x, units[(UBYTE)old].y);
    }
    selected_unit = found;
    if (selected_unit >= 0)
        draw_cell(rp, units[(UBYTE)selected_unit].x,
                  units[(UBYTE)selected_unit].y);
    draw_cursor(rp);
    draw_panel(rp);
}

static void cancel_selection(struct RastPort *rp)
{
    if (selected_unit >= 0) {
        BYTE old = selected_unit;
        selected_unit = -1;
        draw_cell(rp, units[(UBYTE)old].x, units[(UBYTE)old].y);
        draw_cursor(rp);
        draw_panel(rp);
    }
}

static BOOL open_display(void)
{
    static const struct TagItem screen_tags[] = {
        {SA_Width, SCREEN_WIDTH}, {SA_Height, SCREEN_HEIGHT},
        {SA_Depth, 4}, {SA_DisplayID, HIRES_KEY},
        {SA_Type, CUSTOMSCREEN}, {SA_Title, (ULONG)"Battalion: Fulda"},
        {TAG_DONE, 0}
    };
    struct TagItem window_tags[] = {
        {WA_CustomScreen, 0}, {WA_Left, 0}, {WA_Top, 0},
        {WA_Width, SCREEN_WIDTH}, {WA_Height, SCREEN_HEIGHT},
        {WA_Backdrop, TRUE}, {WA_Borderless, TRUE}, {WA_Activate, TRUE},
        {WA_RMBTrap, TRUE}, {WA_ReportMouse, TRUE},
        {WA_IDCMP, IDCMP_RAWKEY | IDCMP_MOUSEMOVE | IDCMP_MOUSEBUTTONS},
        {TAG_DONE, 0}
    };

    game_screen = OpenScreenTagList(0, screen_tags);
    if (!game_screen) return FALSE;
    window_tags[0].ti_Data = (ULONG)game_screen;
    game_window = OpenWindowTagList(0, window_tags);
    if (!game_window) {
        CloseScreen(game_screen);
        game_screen = 0;
        return FALSE;
    }

    SetRGB4(&game_screen->ViewPort, COLOR_BACKGROUND, 0, 1, 2);
    SetRGB4(&game_screen->ViewPort, COLOR_TEXT, 9, 11, 12);
    SetRGB4(&game_screen->ViewPort, COLOR_HIGHLIGHT, 15, 12, 1);
    SetRGB4(&game_screen->ViewPort, COLOR_CLEAR, 3, 7, 3);
    SetRGB4(&game_screen->ViewPort, COLOR_CLEAR_ALT, 4, 8, 4);
    SetRGB4(&game_screen->ViewPort, COLOR_GRID, 1, 3, 1);
    SetRGB4(&game_screen->ViewPort, COLOR_ROAD, 7, 7, 6);
    SetRGB4(&game_screen->ViewPort, COLOR_WOODS, 1, 4, 1);
    SetRGB4(&game_screen->ViewPort, COLOR_WATER, 1, 5, 10);
    SetRGB4(&game_screen->ViewPort, COLOR_TOWN, 10, 6, 2);
    SetRGB4(&game_screen->ViewPort, COLOR_NATO, 4, 8, 15);
    SetRGB4(&game_screen->ViewPort, COLOR_WARSAW, 14, 3, 3);
    SetRGB4(&game_screen->ViewPort, COLOR_BLACK, 0, 0, 0);
    SetRGB4(&game_screen->ViewPort, COLOR_WHITE, 15, 15, 15);
    return TRUE;
}

static void close_display(void)
{
    if (game_window) CloseWindow(game_window);
    if (game_screen) CloseScreen(game_screen);
}

static void input_loop(void)
{
    BOOL running = TRUE;
    struct RastPort *rp = game_window->RPort;
    draw_scene(rp);

    while (running) {
        struct IntuiMessage *message;
        Wait(1UL << game_window->UserPort->mp_SigBit);
        while ((message = (struct IntuiMessage *)GetMsg(game_window->UserPort))) {
            ULONG event_class = message->Class;
            UWORD code = message->Code;
            WORD mouse_x = message->MouseX;
            WORD mouse_y = message->MouseY;
            ReplyMsg((struct Message *)message);

            if (event_class == IDCMP_MOUSEMOVE &&
                mouse_x >= MAP_LEFT && mouse_y >= MAP_TOP &&
                mouse_x < MAP_LEFT + (VIEW_WIDTH << 4) &&
                mouse_y < MAP_TOP + (VIEW_HEIGHT << 4)) {
                move_cursor(rp, (UBYTE)(mouse_x - MAP_LEFT) >> 4,
                            camera_y + ((UBYTE)(mouse_y - MAP_TOP) >> 4));
            } else if (event_class == IDCMP_MOUSEBUTTONS) {
                if (code == SELECTDOWN) select_unit(rp);
                else if (code == MENUDOWN) cancel_selection(rp);
            } else if (event_class == IDCMP_RAWKEY && !(code & 0x80U)) {
                if (code == 0x45U) {
                    if (selected_unit >= 0) cancel_selection(rp);
                    else running = FALSE;
                } else if (code == 0x44U) select_unit(rp);
                else if (code == 0x11U && cursor_y > 0)
                    move_cursor(rp, cursor_x, cursor_y - 1);
                else if (code == 0x21U && cursor_y + 1 < map_height)
                    move_cursor(rp, cursor_x, cursor_y + 1);
                else if (code == 0x20U && cursor_x > 0)
                    move_cursor(rp, cursor_x - 1, cursor_y);
                else if (code == 0x22U && cursor_x + 1 < map_width)
                    move_cursor(rp, cursor_x + 1, cursor_y);
            }
        }
    }
}

int game_main(void)
{
    SysBase = *(struct ExecBase **)4UL;
    DOSBase = (struct DosLibrary *)OpenLibrary((CONST_STRPTR)"dos.library", 39);
    if (!DOSBase) return 20;
    IntuitionBase = (struct IntuitionBase *)OpenLibrary(
        (CONST_STRPTR)"intuition.library", 39);
    if (!IntuitionBase) {
        CloseLibrary((struct Library *)DOSBase);
        return 20;
    }
    GfxBase = (struct GfxBase *)OpenLibrary((CONST_STRPTR)"graphics.library", 39);
    if (!GfxBase) {
        CloseLibrary((struct Library *)IntuitionBase);
        CloseLibrary((struct Library *)DOSBase);
        return 20;
    }

    if (load_game_data() && open_display()) {
        input_loop();
        close_display();
    }

    CloseLibrary((struct Library *)GfxBase);
    CloseLibrary((struct Library *)IntuitionBase);
    CloseLibrary((struct Library *)DOSBase);
    return 0;
}
