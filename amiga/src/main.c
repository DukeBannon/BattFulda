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
#define MAX_UNITS 64
#define MAX_UNIT_TYPES 96
#define MAX_FORMATIONS 64
#define UNIT_TYPE_DATA_SIZE 4096
#define FORMATION_DATA_SIZE 1024
#define SCENARIO_DATA_SIZE 1024

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
    UBYTE id;
    UBYTE type_id;
    UBYTE formation_id;
    UBYTE x;
    UBYTE y;
    UBYTE strength;
    UBYTE morale;
    UBYTE suppression;
    UBYTE readiness;
    const char *name;
};

struct UnitType {
    UBYTE faction;
    UBYTE nation;
    UBYTE echelon;
    UBYTE category;
    UBYTE mobility;
    UBYTE move;
    UBYTE hard_attack;
    UBYTE soft_attack;
    UBYTE defense;
    UBYTE range;
    UBYTE recon;
    UBYTE command;
    const char *name;
};

struct Formation {
    UBYTE id;
    UBYTE parent_id;
    UBYTE faction;
    UBYTE nation;
    UBYTE kind;
    UBYTE command;
    UBYTE base_morale;
    const char *name;
};

static struct Screen *game_screen;
static struct Window *game_window;
static UBYTE map_width;
static UBYTE map_height;
static UBYTE map_tiles[MAX_MAP_CELLS];
static UBYTE unit_type_data[UNIT_TYPE_DATA_SIZE];
static UBYTE formation_data[FORMATION_DATA_SIZE];
static UBYTE scenario_data[SCENARIO_DATA_SIZE];
static struct UnitType unit_types[MAX_UNIT_TYPES];
static struct Formation formations[MAX_FORMATIONS];
static struct TacticalUnit units[MAX_UNITS];
static UBYTE unit_type_count;
static UBYTE formation_count;
static UBYTE unit_count;
static UBYTE cursor_x;
static UBYTE cursor_y;
static UBYTE camera_y;
static BYTE selected_unit = -1;
static const char *load_error = "UNKNOWN DATA ERROR";

static const char *const terrain_names[] = {
    "CLEAR", "ROAD", "WOODS", "WATER", "TOWN"
};
static const UBYTE terrain_lengths[] = {5, 4, 5, 5, 4};
static const char *const side_names[] = {"NATO", "WARSAW PACT"};
static const UBYTE side_lengths[] = {4, 11};

static void draw_text(struct RastPort *rp, WORD x, WORD y,
                      const char *text, UWORD length, UWORD color)
{
    SetAPen(rp, color);
    Move(rp, x, y);
    Text(rp, (CONST_STRPTR)text, length);
}

static UBYTE format_number(UBYTE value, char *buffer)
{
    UBYTE length = 0;
    if (value >= 100) {
        buffer[length++] = (char)('0' + value / 100);
        value %= 100;
        buffer[length++] = (char)('0' + value / 10);
    } else if (value >= 10) {
        buffer[length++] = (char)('0' + value / 10);
    }
    buffer[length++] = (char)('0' + value % 10);
    return length;
}

static UWORD read_be16(const UBYTE *source)
{
    return ((UWORD)source[0] << 8) | source[1];
}

static BOOL has_magic(const UBYTE *data, const char *magic)
{
    return data[0] == (UBYTE)magic[0] && data[1] == (UBYTE)magic[1] &&
           data[2] == (UBYTE)magic[2] && data[3] == (UBYTE)magic[3];
}

static const char *data_string(UBYTE *data, LONG size, UWORD table_offset,
                               UWORD string_offset)
{
    ULONG position = (ULONG)table_offset + string_offset;
    ULONG scan;
    if (position >= (ULONG)size) return 0;
    for (scan = position; scan < (ULONG)size; ++scan) {
        if (data[scan] == 0) return (const char *)&data[position];
    }
    return 0;
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

static BOOL load_map_data(void)
{
    static UBYTE buffer[512];
    LONG size;
    UWORD source;
    UWORD cell_count;

    if (!read_file("map.bin", buffer, sizeof(buffer), &size) || size < 2) {
        load_error = "MAP.BIN MISSING OR SHORT";
        return FALSE;
    }
    map_width = buffer[0];
    map_height = buffer[1];
    cell_count = (UWORD)map_width * map_height;
    if (map_width != VIEW_WIDTH || map_height < VIEW_HEIGHT ||
        cell_count > MAX_MAP_CELLS || size != (LONG)cell_count + 2) {
        load_error = "MAP.BIN FORMAT ERROR";
        return FALSE;
    }
    for (source = 0; source < cell_count; ++source) {
        if (buffer[source + 2] > TILE_TOWN) {
            load_error = "MAP.BIN TERRAIN ERROR";
            return FALSE;
        }
        map_tiles[source] = buffer[source + 2];
    }
    return TRUE;
}

static BOOL load_unit_types(void)
{
    LONG size;
    UBYTE index;
    UWORD strings_offset;
    if (!read_file("unit_types.bin", unit_type_data, sizeof(unit_type_data), &size) ||
        size < 9) {
        load_error = "UNIT_TYPES.BIN MISSING";
        return FALSE;
    }
    unit_type_count = unit_type_data[5];
    strings_offset = read_be16(&unit_type_data[7]);
    if (!has_magic(unit_type_data, "BFUT") || unit_type_data[4] != 1 ||
        !unit_type_count || unit_type_count > MAX_UNIT_TYPES ||
        unit_type_data[6] != 14 || strings_offset < 9 + unit_type_count * 14 ||
        strings_offset >= (UWORD)size) {
        load_error = "UNIT_TYPES.BIN FORMAT ERROR";
        return FALSE;
    }
    for (index = 0; index < unit_type_count; ++index) {
        UBYTE *record = &unit_type_data[9 + (UWORD)index * 14];
        struct UnitType *type = &unit_types[index];
        type->faction = record[0];
        type->nation = record[1];
        type->echelon = record[2];
        type->category = record[3];
        type->mobility = record[4];
        type->move = record[5];
        type->hard_attack = record[6];
        type->soft_attack = record[7];
        type->defense = record[8];
        type->range = record[9];
        type->recon = record[10];
        type->command = record[11];
        type->name = data_string(unit_type_data, size, strings_offset,
                                 read_be16(&record[12]));
        if (type->faction > 1 || !type->name) {
            load_error = "UNIT TYPE RECORD ERROR";
            return FALSE;
        }
    }
    return TRUE;
}

static BOOL load_formations(void)
{
    LONG size;
    UBYTE index;
    UWORD strings_offset;
    if (!read_file("formations.bin", formation_data, sizeof(formation_data), &size) ||
        size < 9) {
        load_error = "FORMATIONS.BIN MISSING";
        return FALSE;
    }
    formation_count = formation_data[5];
    strings_offset = read_be16(&formation_data[7]);
    if (!has_magic(formation_data, "BFFM") || formation_data[4] != 1 ||
        !formation_count || formation_count > MAX_FORMATIONS ||
        formation_data[6] != 9 || strings_offset < 9 + formation_count * 9 ||
        strings_offset >= (UWORD)size) {
        load_error = "FORMATIONS.BIN FORMAT ERROR";
        return FALSE;
    }
    for (index = 0; index < formation_count; ++index) {
        UBYTE *record = &formation_data[9 + (UWORD)index * 9];
        struct Formation *formation = &formations[index];
        formation->id = record[0];
        formation->parent_id = record[1];
        formation->faction = record[2];
        formation->nation = record[3];
        formation->kind = record[4];
        formation->command = record[5];
        formation->base_morale = record[6];
        formation->name = data_string(formation_data, size, strings_offset,
                                      read_be16(&record[7]));
        if (formation->faction > 1 || !formation->name) {
            load_error = "FORMATION RECORD ERROR";
            return FALSE;
        }
    }
    return TRUE;
}

static struct Formation *formation_by_id(UBYTE id)
{
    UBYTE index;
    for (index = 0; index < formation_count; ++index) {
        if (formations[index].id == id) return &formations[index];
    }
    return 0;
}

static BOOL load_scenario(void)
{
    LONG size;
    UBYTE index;
    UWORD strings_offset;
    if (!read_file("a2_scenario.bin", scenario_data, sizeof(scenario_data), &size) ||
        size < 16) {
        load_error = "A2_SCENARIO.BIN MISSING";
        return FALSE;
    }
    cursor_x = scenario_data[9];
    cursor_y = scenario_data[10];
    unit_count = scenario_data[12];
    strings_offset = read_be16(&scenario_data[14]);
    if (!has_magic(scenario_data, "BFSC") || scenario_data[4] != 2 ||
        scenario_data[6] != map_width || scenario_data[7] != map_height ||
        !scenario_data[8] ||
        scenario_data[11] != formation_count || !unit_count ||
        unit_count > MAX_UNITS || scenario_data[13] != 11 ||
        cursor_x >= map_width || cursor_y >= map_height ||
        strings_offset < 16 + unit_count * 11 || strings_offset >= (UWORD)size) {
        load_error = "A2_SCENARIO.BIN FORMAT ERROR";
        return FALSE;
    }
    for (index = 0; index < unit_count; ++index) {
        UBYTE *record = &scenario_data[16 + (UWORD)index * 11];
        struct TacticalUnit *unit = &units[index];
        struct Formation *formation;
        unit->id = record[0];
        unit->type_id = record[1];
        unit->formation_id = record[2];
        unit->x = record[3];
        unit->y = record[4];
        unit->strength = record[5];
        unit->morale = record[6];
        unit->suppression = record[7];
        unit->readiness = record[8];
        unit->name = data_string(scenario_data, size, strings_offset,
                                 read_be16(&record[9]));
        formation = formation_by_id(unit->formation_id);
        if (unit->type_id >= unit_type_count || !formation || !unit->name ||
            unit->x >= map_width || unit->y >= map_height ||
            formation->faction != unit_types[unit->type_id].faction) {
            load_error = "SCENARIO UNIT RECORD ERROR";
            return FALSE;
        }
    }
    camera_y = cursor_y >= VIEW_HEIGHT ? cursor_y - VIEW_HEIGHT + 1 : 0;
    return TRUE;
}

static BOOL load_game_data(void)
{
    if (!load_map_data() || !load_unit_types() || !load_formations() ||
        !load_scenario()) return FALSE;
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
    const struct UnitType *type = &unit_types[unit->type_id];
    UWORD color = type->faction == 0 ? COLOR_NATO : COLOR_WARSAW;
    const char *symbol = type->faction == 0 ? "N" : "W";

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

static void draw_clipped(struct RastPort *rp, WORD x, WORD y,
                         const char *text, UBYTE maximum, UWORD color)
{
    UBYTE length = 0;
    while (length < maximum && text[length]) ++length;
    draw_text(rp, x, y, text, length, color);
}

static const char *morale_name(UBYTE morale, UBYTE *length)
{
    if (morale >= 85) {
        *length = 9;
        return "CONFIDENT";
    }
    if (morale >= 70) {
        *length = 6;
        return "STEADY";
    }
    if (morale >= 50) {
        *length = 6;
        return "SHAKEN";
    }
    *length = 6;
    return "BROKEN";
}

static void draw_panel(struct RastPort *rp)
{
    char number[3];
    UBYTE length;
    UBYTE tile = tile_at(cursor_x, cursor_y);
    clear_panel(rp);
    draw_text(rp, 384, 58, "TACTICAL DISPLAY", 16, COLOR_HIGHLIGHT);

    if (selected_unit >= 0) {
        const struct TacticalUnit *unit = &units[(UBYTE)selected_unit];
        const struct UnitType *type = &unit_types[unit->type_id];
        struct Formation *formation = formation_by_id(unit->formation_id);
        struct Formation *parent = formation && formation->parent_id != 255 ?
                                   formation_by_id(formation->parent_id) : 0;
        const char *morale;
        tile = tile_at(unit->x, unit->y);
        draw_text(rp, 384, 76, "SELECTED UNIT", 13, COLOR_HIGHLIGHT);
        draw_text(rp, 520, 76, side_names[type->faction],
                  side_lengths[type->faction], COLOR_HIGHLIGHT);
        draw_clipped(rp, 384, 92, unit->name, 31, COLOR_WHITE);
        draw_clipped(rp, 384, 108, type->name, 31, COLOR_TEXT);
        if (formation) draw_clipped(rp, 384, 124, formation->name, 31, COLOR_WHITE);
        if (parent) draw_clipped(rp, 384, 140, parent->name, 31, COLOR_TEXT);

        length = format_number(unit->strength, number);
        draw_value(rp, 158, "STRENGTH:", 9, number, length);
        morale = morale_name(unit->morale, &length);
        draw_value(rp, 174, "MORALE:", 7, morale, length);
        length = format_number(unit->suppression, number);
        draw_value(rp, 190, "SUPPRESSION:", 12, number, length);
        length = format_number(unit->readiness, number);
        draw_value(rp, 206, "READINESS:", 10, number, length);
        length = format_number(type->move, number);
        draw_value(rp, 222, "MOVE:", 5, number, length);
        draw_value(rp, 238, "TERRAIN:", 8, terrain_names[tile],
                   terrain_lengths[tile]);
    } else {
        draw_text(rp, 384, 86, "NO UNIT SELECTED", 16, COLOR_TEXT);
        draw_value(rp, 110, "TERRAIN:", 8, terrain_names[tile],
                   terrain_lengths[tile]);
        draw_text(rp, 384, 150, "MOUSE/WASD: MOVE", 16, COLOR_TEXT);
        draw_text(rp, 384, 166, "LEFT CLICK/ENTER: SELECT", 24, COLOR_TEXT);
        draw_text(rp, 384, 182, "ESC: EXIT", 9, COLOR_TEXT);
    }

    if (selected_unit < 0)
        draw_text(rp, 384, 246, "A2.2 DATABASE RUNTIME", 21, COLOR_HIGHLIGHT);
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
    draw_text(rp, 24, 37, "FULDA GAP - A2.2", 16, COLOR_TEXT);
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

static void data_error_loop(void)
{
    BOOL waiting = TRUE;
    struct RastPort *rp = game_window->RPort;
    SetRast(rp, COLOR_BACKGROUND);
    draw_text(rp, 24, 30, "BATTALION: FULDA", 16, COLOR_HIGHLIGHT);
    draw_text(rp, 24, 62, "DATA LOAD FAILED", 16, COLOR_WARSAW);
    draw_clipped(rp, 24, 86, load_error, 60, COLOR_WHITE);
    draw_text(rp, 24, 118, "CHECK THE A2.2 DATA FILES", 25, COLOR_TEXT);
    draw_text(rp, 24, 150, "PRESS A KEY OR MOUSE BUTTON", 27, COLOR_TEXT);
    while (waiting) {
        struct IntuiMessage *message;
        Wait(1UL << game_window->UserPort->mp_SigBit);
        while ((message = (struct IntuiMessage *)GetMsg(game_window->UserPort))) {
            ULONG event_class = message->Class;
            ReplyMsg((struct Message *)message);
            if (event_class == IDCMP_RAWKEY || event_class == IDCMP_MOUSEBUTTONS)
                waiting = FALSE;
        }
    }
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
    BOOL data_loaded;
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

    data_loaded = load_game_data();
    if (open_display()) {
        if (data_loaded) input_loop();
        else data_error_loop();
        close_display();
    }

    CloseLibrary((struct Library *)GfxBase);
    CloseLibrary((struct Library *)IntuitionBase);
    CloseLibrary((struct Library *)DOSBase);
    return 0;
}
