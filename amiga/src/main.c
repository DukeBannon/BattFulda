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
#define EDGE_SCROLL_MARGIN 16
#define MAX_MAP_CELLS 4096
#define MAP_DATA_SIZE 16384
#define MAX_UNITS 64
#define MAX_UNIT_TYPES 96
#define MAX_FORMATIONS 64
#define UNIT_TYPE_DATA_SIZE 4096
#define FORMATION_DATA_SIZE 1024
#define SCENARIO_DATA_SIZE 1024

#define TILE_CLEAR 0
#define TILE_WOODS 1
#define TILE_ROUGH 2
#define TILE_MARSH 3
#define TILE_WATER 4
#define TILE_URBAN 5

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
#define COLOR_HIGH_GROUND 14
#define COLOR_HIGHEST_GROUND 15

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

struct MapCell {
    UBYTE terrain;
    WORD elevation;
    UBYTE road_class;
    UBYTE river_class;
    UBYTE settlement_level;
    UBYTE has_bridge;
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
static UWORD map_cell_size;
static ULONG map_origin_easting;
static ULONG map_origin_northing;
static UBYTE map_data[MAP_DATA_SIZE];
static struct MapCell map_cells[MAX_MAP_CELLS];
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
static UBYTE camera_x;
static UBYTE camera_y;
static BYTE selected_unit = -1;
static const char *load_error = "UNKNOWN DATA ERROR";

static const char *const terrain_names[] = {
    "CLEAR", "WOODS", "ROUGH", "MARSH", "WATER", "URBAN"
};
static const UBYTE terrain_lengths[] = {5, 5, 5, 5, 5, 5};
static const char *const side_names[] = {"NATO", "WARSAW PACT"};
static const UBYTE side_lengths[] = {4, 11};

static void draw_text(struct RastPort *rp, WORD x, WORD y,
                      const char *text, UWORD length, UWORD color)
{
    SetAPen(rp, color);
    Move(rp, x, y);
    Text(rp, (CONST_STRPTR)text, length);
}

static UBYTE format_number(UWORD value, char *buffer)
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

static ULONG read_be32(const UBYTE *source)
{
    return ((ULONG)source[0] << 24) | ((ULONG)source[1] << 16) |
           ((ULONG)source[2] << 8) | source[3];
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
    LONG size;
    UWORD index;
    UWORD source;
    UWORD cell_count;

    if (!read_file("point_alpha_map.bin", map_data, sizeof(map_data), &size) ||
        size < 19) {
        load_error = "POINT_ALPHA_MAP.BIN MISSING";
        return FALSE;
    }
    map_width = map_data[6];
    map_height = map_data[7];
    map_cell_size = read_be16(&map_data[9]);
    map_origin_easting = read_be32(&map_data[11]);
    map_origin_northing = read_be32(&map_data[15]);
    cell_count = (UWORD)map_width * map_height;
    if (!has_magic(map_data, "BFMP") || map_data[4] != 1 ||
        map_width < VIEW_WIDTH || map_height < VIEW_HEIGHT ||
        map_data[8] != 7 || map_cell_size != 500 ||
        !map_origin_easting || !map_origin_northing ||
        cell_count > MAX_MAP_CELLS || size != 19 + (LONG)cell_count * 7) {
        load_error = "POINT ALPHA MAP FORMAT ERROR";
        return FALSE;
    }
    source = 19;
    for (index = 0; index < cell_count; ++index) {
        struct MapCell *cell = &map_cells[index];
        cell->terrain = map_data[source++];
        cell->elevation = (WORD)read_be16(&map_data[source]);
        source += 2;
        cell->road_class = map_data[source++];
        cell->river_class = map_data[source++];
        cell->settlement_level = map_data[source++];
        cell->has_bridge = map_data[source++];
        if (cell->terrain > TILE_URBAN || cell->road_class > 3 ||
            cell->river_class > 3 || cell->settlement_level > 3 ||
            cell->has_bridge > 1) {
            load_error = "POINT ALPHA MAP CELL ERROR";
            return FALSE;
        }
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
    camera_x = cursor_x >= VIEW_WIDTH ? cursor_x - VIEW_WIDTH + 1 : 0;
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

static struct MapCell *map_cell_at(UBYTE x, UBYTE y)
{
    return &map_cells[(UWORD)y * map_width + x];
}

static UBYTE tile_at(UBYTE x, UBYTE y)
{
    return map_cell_at(x, y)->terrain;
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

static void draw_terrain(struct RastPort *rp, const struct MapCell *cell,
                         WORD left, WORD top)
{
    UWORD fill;
    if (cell->elevation < 300) fill = COLOR_CLEAR;
    else if (cell->elevation < 400) fill = COLOR_CLEAR_ALT;
    else if (cell->elevation < 500) fill = COLOR_HIGH_GROUND;
    else fill = COLOR_HIGHEST_GROUND;
    if (cell->terrain == TILE_WOODS) fill = COLOR_WOODS;
    else if (cell->terrain == TILE_MARSH || cell->terrain == TILE_WATER)
        fill = COLOR_WATER;
    else if (cell->terrain == TILE_URBAN) fill = COLOR_TOWN;

    SetAPen(rp, fill);
    RectFill(rp, left + 1, top + 1, left + CELL_SIZE - 2,
             top + CELL_SIZE - 2);
    draw_box(rp, left, top, left + CELL_SIZE - 1,
             top + CELL_SIZE - 1, COLOR_GRID);

    if (cell->road_class) {
        SetAPen(rp, COLOR_ROAD);
        Move(rp, left + 1, top + 8);
        Draw(rp, left + 14, top + 8);
    }
    if (cell->river_class) {
        SetAPen(rp, COLOR_WATER);
        Move(rp, left + 8, top + 1);
        Draw(rp, left + 8, top + 14);
    }
    if (cell->terrain == TILE_ROUGH) {
        SetAPen(rp, COLOR_TEXT);
        Move(rp, left + 3, top + 11);
        Draw(rp, left + 7, top + 5);
        Draw(rp, left + 12, top + 11);
    } else if (cell->terrain == TILE_WOODS) {
        SetAPen(rp, COLOR_CLEAR_ALT);
        RectFill(rp, left + 4, top + 3, left + 6, top + 7);
        RectFill(rp, left + 9, top + 6, left + 11, top + 10);
    } else if (cell->terrain == TILE_WATER) {
        SetAPen(rp, COLOR_WHITE);
        Move(rp, left + 2, top + 6);
        Draw(rp, left + 6, top + 6);
        Move(rp, left + 8, top + 10);
        Draw(rp, left + 13, top + 10);
    } else if (cell->terrain == TILE_URBAN || cell->settlement_level) {
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
    if (map_x < camera_x || map_x >= camera_x + VIEW_WIDTH ||
        map_y < camera_y || map_y >= camera_y + VIEW_HEIGHT) return;
    left = MAP_LEFT + ((WORD)(map_x - camera_x) << 4);
    top = MAP_TOP + ((WORD)(map_y - camera_y) << 4);
    draw_terrain(rp, map_cell_at(map_x, map_y), left, top);
    unit = unit_at(map_x, map_y);
    if (unit >= 0) draw_unit(rp, unit, left, top);
}

static void draw_cursor(struct RastPort *rp)
{
    WORD left = MAP_LEFT + ((WORD)(cursor_x - camera_x) << 4);
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
    struct MapCell *cursor_cell = map_cell_at(cursor_x, cursor_y);
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
        length = format_number((UWORD)map_cell_at(unit->x, unit->y)->elevation,
                               number);
        draw_text(rp, 536, 222, "ELEV:", 5, COLOR_TEXT);
        draw_text(rp, 584, 222, number, length, COLOR_WHITE);
        draw_value(rp, 238, "TERRAIN:", 8, terrain_names[tile],
                   terrain_lengths[tile]);
    } else {
        draw_text(rp, 384, 86, "NO UNIT SELECTED", 16, COLOR_TEXT);
        draw_value(rp, 110, "TERRAIN:", 8, terrain_names[tile],
                   terrain_lengths[tile]);
        length = format_number((UWORD)cursor_cell->elevation, number);
        draw_value(rp, 126, "ELEVATION:", 10, number, length);
        length = format_number(cursor_x, number);
        draw_value(rp, 142, "CELL X:", 7, number, length);
        length = format_number(cursor_y, number);
        draw_value(rp, 158, "CELL Y:", 7, number, length);
        draw_text(rp, 384, 182, "MOUSE/WASD: MOVE", 16, COLOR_TEXT);
        draw_text(rp, 384, 198, "LEFT CLICK/ENTER: SELECT", 24, COLOR_TEXT);
        draw_text(rp, 384, 214, "ESC: EXIT", 9, COLOR_TEXT);
    }

    if (selected_unit < 0)
        draw_text(rp, 384, 246, "A3.2 MAP VIEWER", 15, COLOR_HIGHLIGHT);
}

static void update_cursor_details(struct RastPort *rp)
{
    char number[3];
    UBYTE length;
    UBYTE tile = tile_at(cursor_x, cursor_y);
    struct MapCell *cell = map_cell_at(cursor_x, cursor_y);
    SetAPen(rp, COLOR_BACKGROUND);
    RectFill(rp, 488, 98, 624, 160);
    draw_text(rp, 488, 110, terrain_names[tile], terrain_lengths[tile],
              COLOR_WHITE);
    length = format_number((UWORD)cell->elevation, number);
    draw_text(rp, 488, 126, number, length, COLOR_WHITE);
    length = format_number(cursor_x, number);
    draw_text(rp, 488, 142, number, length, COLOR_WHITE);
    length = format_number(cursor_y, number);
    draw_text(rp, 488, 158, number, length, COLOR_WHITE);
}

static void draw_map(struct RastPort *rp)
{
    UBYTE x;
    UBYTE view_y;
    for (view_y = 0; view_y < VIEW_HEIGHT; ++view_y) {
        for (x = 0; x < VIEW_WIDTH; ++x)
            draw_cell(rp, camera_x + x, camera_y + view_y);
    }
    draw_cursor(rp);
}

static void scroll_map(struct RastPort *rp, UBYTE old_camera_x,
                       UBYTE old_camera_y, UBYTE old_cursor_x,
                       UBYTE old_cursor_y)
{
    UBYTE index;
    WORD delta_x = (WORD)camera_x - old_camera_x;
    WORD delta_y = (WORD)camera_y - old_camera_y;
    WORD right = MAP_LEFT + VIEW_WIDTH * CELL_SIZE - 1;
    WORD bottom = MAP_TOP + VIEW_HEIGHT * CELL_SIZE - 1;

    SetBPen(rp, COLOR_BACKGROUND);
    if (delta_y == 0 && (delta_x == 1 || delta_x == -1)) {
        ScrollRaster(rp, delta_x * CELL_SIZE, 0, MAP_LEFT, MAP_TOP,
                     right, bottom);
        for (index = 0; index < VIEW_HEIGHT; ++index)
            draw_cell(rp,
                      delta_x > 0 ? camera_x + VIEW_WIDTH - 1 : camera_x,
                      camera_y + index);
    } else if (delta_x == 0 && (delta_y == 1 || delta_y == -1)) {
        ScrollRaster(rp, 0, delta_y * CELL_SIZE, MAP_LEFT, MAP_TOP,
                     right, bottom);
        for (index = 0; index < VIEW_WIDTH; ++index)
            draw_cell(rp, camera_x + index,
                      delta_y > 0 ? camera_y + VIEW_HEIGHT - 1 : camera_y);
    } else {
        draw_map(rp);
        return;
    }

    /* The old cursor moved with the raster; restore the cell beneath it. */
    draw_cell(rp, old_cursor_x, old_cursor_y);
    draw_cursor(rp);
}

static void draw_scene(struct RastPort *rp)
{
    SetRast(rp, COLOR_BACKGROUND);
    draw_text(rp, 24, 22, "BATTALION: FULDA", 16, COLOR_HIGHLIGHT);
    draw_text(rp, 24, 37, "POINT ALPHA MAP - A3.2", 22, COLOR_TEXT);
    draw_map(rp);
    draw_panel(rp);
}

static void move_cursor(struct RastPort *rp, UBYTE x, UBYTE y)
{
    UBYTE old_x = cursor_x;
    UBYTE old_y = cursor_y;
    UBYTE old_camera_x = camera_x;
    UBYTE old_camera_y = camera_y;
    UBYTE maximum_camera_x = map_width - VIEW_WIDTH;
    UBYTE maximum_camera = map_height - VIEW_HEIGHT;
    if (x >= map_width || y >= map_height) return;
    if (x == cursor_x && y == cursor_y) return;

    cursor_x = x;
    cursor_y = y;
    if (cursor_x < camera_x) camera_x = cursor_x;
    else if (cursor_x >= camera_x + VIEW_WIDTH)
        camera_x = cursor_x - VIEW_WIDTH + 1;
    if (camera_x > maximum_camera_x) camera_x = maximum_camera_x;
    if (cursor_y < camera_y) camera_y = cursor_y;
    else if (cursor_y >= camera_y + VIEW_HEIGHT)
        camera_y = cursor_y - VIEW_HEIGHT + 1;
    if (camera_y > maximum_camera) camera_y = maximum_camera;

    if (camera_x != old_camera_x || camera_y != old_camera_y)
        scroll_map(rp, old_camera_x, old_camera_y, old_x, old_y);
    else {
        draw_cell(rp, old_x, old_y);
        draw_cursor(rp);
    }
    if (selected_unit < 0) update_cursor_details(rp);
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

static BOOL mouse_map_cell(WORD mouse_x, WORD mouse_y,
                           UBYTE *map_x, UBYTE *map_y)
{
    if (mouse_x < MAP_LEFT || mouse_y < MAP_TOP ||
        mouse_x >= MAP_LEFT + VIEW_WIDTH * CELL_SIZE ||
        mouse_y >= MAP_TOP + VIEW_HEIGHT * CELL_SIZE) return FALSE;
    *map_x = camera_x + (UBYTE)((mouse_x - MAP_LEFT) / CELL_SIZE);
    *map_y = camera_y + (UBYTE)((mouse_y - MAP_TOP) / CELL_SIZE);
    return *map_x < map_width && *map_y < map_height;
}

static void update_mouse_pan(WORD mouse_x, WORD mouse_y,
                             BYTE *pan_x, BYTE *pan_y)
{
    WORD map_right = MAP_LEFT + VIEW_WIDTH * CELL_SIZE;
    WORD map_bottom = MAP_TOP + VIEW_HEIGHT * CELL_SIZE;
    *pan_x = 0;
    *pan_y = 0;

    if (mouse_y >= MAP_TOP && mouse_y < map_bottom) {
        if (mouse_x >= MAP_LEFT - EDGE_SCROLL_MARGIN && mouse_x < MAP_LEFT)
            *pan_x = -1;
        else if (mouse_x >= map_right &&
                 mouse_x < map_right + EDGE_SCROLL_MARGIN)
            *pan_x = 1;
    }
    if (mouse_x >= MAP_LEFT && mouse_x < map_right) {
        if (mouse_y >= MAP_TOP - EDGE_SCROLL_MARGIN && mouse_y < MAP_TOP)
            *pan_y = -1;
        else if (mouse_y >= map_bottom &&
                 mouse_y < map_bottom + EDGE_SCROLL_MARGIN)
            *pan_y = 1;
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
        {WA_IDCMP, IDCMP_RAWKEY | IDCMP_MOUSEMOVE | IDCMP_MOUSEBUTTONS |
                   IDCMP_INTUITICKS},
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
    SetRGB4(&game_screen->ViewPort, COLOR_HIGH_GROUND, 6, 7, 3);
    SetRGB4(&game_screen->ViewPort, COLOR_HIGHEST_GROUND, 8, 5, 2);
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
    draw_text(rp, 24, 118, "CHECK THE A3.2 DATA FILES", 25, COLOR_TEXT);
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
    BYTE mouse_pan_x = 0;
    BYTE mouse_pan_y = 0;
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
            UBYTE map_x;
            UBYTE map_y;
            ReplyMsg((struct Message *)message);

            if (event_class == IDCMP_MOUSEMOVE) {
                update_mouse_pan(mouse_x, mouse_y,
                                 &mouse_pan_x, &mouse_pan_y);
                if (mouse_map_cell(mouse_x, mouse_y, &map_x, &map_y))
                    move_cursor(rp, map_x, map_y);
            } else if (event_class == IDCMP_MOUSEBUTTONS) {
                if (code == SELECTDOWN &&
                    mouse_map_cell(mouse_x, mouse_y, &map_x, &map_y)) {
                    /* A click always acts on its cell, even if a preceding
                       mouse-move message was coalesced or dropped. */
                    move_cursor(rp, map_x, map_y);
                    select_unit(rp);
                }
                else if (code == MENUDOWN) cancel_selection(rp);
            } else if (event_class == IDCMP_INTUITICKS) {
                if (mouse_pan_x < 0 && cursor_x > 0)
                    move_cursor(rp, cursor_x - 1, cursor_y);
                else if (mouse_pan_x > 0 && cursor_x + 1 < map_width)
                    move_cursor(rp, cursor_x + 1, cursor_y);
                else if (mouse_pan_y < 0 && cursor_y > 0)
                    move_cursor(rp, cursor_x, cursor_y - 1);
                else if (mouse_pan_y > 0 && cursor_y + 1 < map_height)
                    move_cursor(rp, cursor_x, cursor_y + 1);
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
