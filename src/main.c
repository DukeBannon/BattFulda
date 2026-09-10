#include <c64.h>
#include <conio.h>
#include <joystick.h>
#include <mouse.h>
#include <stdint.h>

#define SCREEN ((unsigned char*)0x0400)
#define COLOR ((unsigned char*)0xd800)
#define RASTER (*(volatile unsigned char*)0xd012)
#define MAP_LEFT 10
#define MAP_TOP 5
#define MAP_WIDTH 20
#define MAP_HEIGHT 15

extern const unsigned char map_data[];
extern const unsigned char scenario_data[];
extern const unsigned char unit_data[];

static const unsigned char tile_chars[] = {0x2e, 0x20, 0x17, 0x66, 0x14};
static const unsigned char tile_colors[] = {COLOR_GREEN, COLOR_GRAY2, COLOR_GREEN,
                                           COLOR_LIGHTBLUE, COLOR_GREEN};
static const unsigned char unit_chars[] = {0x4e, 0x57};
static const unsigned char unit_colors[] = {COLOR_YELLOW, COLOR_RED};
static unsigned char cursor_x, cursor_y, cursor_phase;
static unsigned char joystick_previous, joystick_repeat, fire_previous;
static unsigned char status_mouse_x = 0xff, status_mouse_y = 0xff;
static unsigned char status_joystick = 0xff, status_key = 0xff;

static void wait_frame(void)
{
    while (RASTER != 0xff) {}
    while (RASTER == 0xff) {}
}

static void draw_map(void)
{
    unsigned int source = 2;
    unsigned char x, y;
    for (y = 0; y < MAP_HEIGHT; ++y) {
        unsigned int destination = (unsigned int)(MAP_TOP + y) * 40 + MAP_LEFT;
        for (x = 0; x < MAP_WIDTH; ++x) {
            unsigned char tile = map_data[source++];
            SCREEN[destination + x] = tile_chars[tile];
            COLOR[destination + x] = tile_colors[tile];
        }
    }
}

static void draw_units(void)
{
    unsigned char count = unit_data[0];
    unsigned int source = 1;
    unsigned char index;
    for (index = 0; index < count; ++index) {
        unsigned char side = unit_data[source];
        unsigned char x = unit_data[source + 2];
        unsigned char y = unit_data[source + 3];
        unsigned int destination = (unsigned int)(MAP_TOP + y) * 40 + MAP_LEFT + x;
        SCREEN[destination] = unit_chars[side];
        COLOR[destination] = unit_colors[side];
        source += 5;
    }
}

static void draw_cell(unsigned char x, unsigned char y)
{
    unsigned int destination = (unsigned int)(MAP_TOP + y) * 40 + MAP_LEFT + x;
    unsigned char tile = map_data[2 + (unsigned int)y * MAP_WIDTH + x];
    unsigned char count = unit_data[0];
    unsigned int source = 1;
    unsigned char index;

    SCREEN[destination] = tile_chars[tile];
    COLOR[destination] = tile_colors[tile];

    for (index = 0; index < count; ++index) {
        if (unit_data[source + 2] == x && unit_data[source + 3] == y) {
            unsigned char side = unit_data[source];
            SCREEN[destination] = unit_chars[side];
            COLOR[destination] = unit_colors[side];
            return;
        }
        source += 5;
    }
}

static void draw_cursor(void)
{
    unsigned int destination = (unsigned int)(MAP_TOP + cursor_y) * 40 + MAP_LEFT + cursor_x;
    SCREEN[destination] |= 0x80;
    COLOR[destination] = (cursor_phase & 0x10) ? COLOR_WHITE : COLOR_YELLOW;
}

static void draw_scene(void)
{
    draw_map();
    draw_units();
    draw_cursor();
}

static void draw_fire_status(unsigned char pressed)
{
    if (pressed == fire_previous) return;
    fire_previous = pressed;
    textcolor(pressed ? COLOR_RED : COLOR_LIGHTBLUE);
    cputsxy(16, 22, pressed ? "FIRE PRESSED " : "INPUT READY  ");
}

static void draw_input_status(unsigned char mouse_x, unsigned char mouse_y,
                              unsigned char joystick, unsigned char key)
{
    if (mouse_x == status_mouse_x && mouse_y == status_mouse_y &&
        joystick == status_joystick && key == status_key) return;
    status_mouse_x = mouse_x;
    status_mouse_y = mouse_y;
    status_joystick = joystick;
    status_key = key;
    textcolor(COLOR_LIGHTBLUE);
    gotoxy(5, 21);
    cprintf("M:%2u,%2u  J:%02X  K:%02X", mouse_x, mouse_y, joystick, key);
}

static void stop_with_error(const char* device, unsigned char error)
{
    clrscr();
    textcolor(COLOR_WHITE);
    cputsxy(2, 2, "INPUT DRIVER ERROR");
    cputsxy(2, 4, device);
    gotoxy(2, 6);
    cprintf("CODE %u", error);
    for (;;) {}
}

static void initialize_input(void)
{
    struct mouse_box box;
    unsigned char error = joy_install(joy_static_stddrv);
    if (error != JOY_ERR_OK) stop_with_error("JOYSTICK", error);
    error = mouse_install(&mouse_def_callbacks, (void*)mouse_static_stddrv);
    if (error != MOUSE_ERR_OK) stop_with_error("1351 MOUSE", error);
    box.minx = 0;
    box.miny = 0;
    box.maxx = MAP_WIDTH * 8 - 1;
    box.maxy = MAP_HEIGHT * 8 - 1;
    mouse_hide();
    mouse_setbox(&box);
    mouse_move((int)cursor_x * 8 + 4, (int)cursor_y * 8 + 4);
}

static unsigned char poll_input(void)
{
    struct mouse_info mouse;
    unsigned char joystick, key = 0, moved = 0, mouse_x, mouse_y, fire;
    mouse_info(&mouse);
    mouse_x = (unsigned char)(mouse.pos.x >> 3);
    mouse_y = (unsigned char)(mouse.pos.y >> 3);
    if (mouse_x != cursor_x || mouse_y != cursor_y) {
        cursor_x = mouse_x;
        cursor_y = mouse_y;
        moved = 1;
    }

    joystick = joy_read(JOY_2);
    if (joystick & (JOY_UP_MASK | JOY_DOWN_MASK | JOY_LEFT_MASK | JOY_RIGHT_MASK)) {
        if (joystick != joystick_previous || joystick_repeat == 0) {
            joystick_repeat = 6;
            if (JOY_LEFT(joystick) && cursor_x > 0) { --cursor_x; moved = 1; }
            if (JOY_RIGHT(joystick) && cursor_x < MAP_WIDTH - 1) { ++cursor_x; moved = 1; }
            if (JOY_UP(joystick) && cursor_y > 0) { --cursor_y; moved = 1; }
            if (JOY_DOWN(joystick) && cursor_y < MAP_HEIGHT - 1) { ++cursor_y; moved = 1; }
            if (moved) mouse_move((int)cursor_x * 8 + 4, (int)cursor_y * 8 + 4);
        } else {
            --joystick_repeat;
        }
    } else {
        joystick_repeat = 0;
    }
    joystick_previous = joystick;

    if (kbhit()) {
        key = cgetc();
        if ((key == 'a' || key == 'A') && cursor_x > 0) { --cursor_x; moved = 1; }
        if ((key == 'd' || key == 'D') && cursor_x < MAP_WIDTH - 1) { ++cursor_x; moved = 1; }
        if ((key == 'w' || key == 'W') && cursor_y > 0) { --cursor_y; moved = 1; }
        if ((key == 's' || key == 'S') && cursor_y < MAP_HEIGHT - 1) { ++cursor_y; moved = 1; }
        if (moved) mouse_move((int)cursor_x * 8 + 4, (int)cursor_y * 8 + 4);
    }

    fire = (unsigned char)((mouse.buttons & MOUSE_BTN_LEFT) || JOY_BTN_1(joystick));
    draw_input_status(mouse_x, mouse_y, joystick, key);
    draw_fire_status(fire);
    return moved;
}

int main(void)
{
    unsigned char old_x, old_y;
    bordercolor(COLOR_BLACK);
    bgcolor(COLOR_BLUE);
    textcolor(COLOR_LIGHTBLUE);
    clrscr();
    cputsxy(8, 0, "BATTALION: FULDA");
    cputsxy(7, 1, "D1 CC65 FOUNDATION");
    cputsxy(3, 2, "WASD + 1351 P1 + JOYSTICK P2");
    cursor_x = scenario_data[1];
    cursor_y = scenario_data[2];
    initialize_input();
    draw_scene();
    textcolor(COLOR_LIGHTBLUE);
    cputsxy(8, 23, "WASD; ALT-M FOR MOUSE");
    fire_previous = 0xff;
    draw_fire_status(0);
    for (;;) {
        wait_frame();
        old_x = cursor_x;
        old_y = cursor_y;
        if (poll_input()) {
            draw_cell(old_x, old_y);
            draw_cursor();
        }
        else { ++cursor_phase; draw_cursor(); }
    }
}
