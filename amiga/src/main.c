#include <exec/execbase.h>
#include <exec/libraries.h>
#include <graphics/gfxbase.h>
#include <graphics/modeid.h>
#include <graphics/rastport.h>
#include <intuition/intuition.h>
#include <intuition/intuitionbase.h>
#include <proto/exec.h>
#include <proto/graphics.h>
#include <proto/intuition.h>
#include <utility/tagitem.h>

#define SCREEN_WIDTH 640
#define SCREEN_HEIGHT 256
#define MAP_LEFT 32
#define MAP_TOP 48
#define CELL_SIZE 16
#define MAP_WIDTH 24
#define MAP_HEIGHT 11

struct ExecBase *SysBase;
struct GfxBase *GfxBase;
struct IntuitionBase *IntuitionBase;

static struct Screen *game_screen;
static struct Window *game_window;
static UWORD cursor_x = 6;
static UWORD cursor_y = 5;

static void draw_text(struct RastPort *rp, WORD x, WORD y,
                      const char *text, UWORD length, UWORD color)
{
    SetAPen(rp, color);
    Move(rp, x, y);
    Text(rp, (CONST_STRPTR)text, length);
}

static void draw_cell(struct RastPort *rp, UWORD x, UWORD y)
{
    WORD left = MAP_LEFT + (WORD)(x << 4);
    WORD top = MAP_TOP + (WORD)(y << 4);
    UWORD color = ((x + y) & 1U) ? 3U : 4U;

    SetAPen(rp, color);
    RectFill(rp, left + 1, top + 1, left + CELL_SIZE - 2,
             top + CELL_SIZE - 2);
    SetAPen(rp, 5);
    Move(rp, left, top);
    Draw(rp, left + CELL_SIZE - 1, top);
    Draw(rp, left + CELL_SIZE - 1, top + CELL_SIZE - 1);
    Draw(rp, left, top + CELL_SIZE - 1);
    Draw(rp, left, top);
}

static void draw_cursor(struct RastPort *rp)
{
    WORD left = MAP_LEFT + (WORD)(cursor_x << 4);
    WORD top = MAP_TOP + (WORD)(cursor_y << 4);

    SetAPen(rp, 2);
    Move(rp, left, top);
    Draw(rp, left + CELL_SIZE - 1, top);
    Draw(rp, left + CELL_SIZE - 1, top + CELL_SIZE - 1);
    Draw(rp, left, top + CELL_SIZE - 1);
    Draw(rp, left, top);
    Move(rp, left + 1, top + 1);
    Draw(rp, left + CELL_SIZE - 2, top + 1);
    Draw(rp, left + CELL_SIZE - 2, top + CELL_SIZE - 2);
    Draw(rp, left + 1, top + CELL_SIZE - 2);
    Draw(rp, left + 1, top + 1);
}

static void move_cursor(struct RastPort *rp, UWORD x, UWORD y)
{
    if (x >= MAP_WIDTH || y >= MAP_HEIGHT) return;
    draw_cell(rp, cursor_x, cursor_y);
    cursor_x = x;
    cursor_y = y;
    draw_cursor(rp);
}

static void draw_scene(struct RastPort *rp)
{
    UWORD x;
    UWORD y;

    SetRast(rp, 0);
    draw_text(rp, 32, 22, "BATTALION: FULDA", 16, 2);
    draw_text(rp, 32, 37, "A0 AMIGA 1200 INPUT FOUNDATION", 30, 1);

    for (y = 0; y < MAP_HEIGHT; ++y) {
        for (x = 0; x < MAP_WIDTH; ++x) draw_cell(rp, x, y);
    }

    draw_cursor(rp);
    draw_text(rp, 432, 64, "INPUT TEST", 10, 2);
    draw_text(rp, 432, 88, "MOVE: MOUSE OR WASD", 19, 1);
    draw_text(rp, 432, 104, "CLICK: SELECT", 13, 1);
    draw_text(rp, 432, 120, "ESC: EXIT", 9, 1);
    draw_text(rp, 432, 152, "STATUS: READY", 13, 1);
}

static void show_click(struct RastPort *rp)
{
    SetAPen(rp, 0);
    RectFill(rp, 432, 138, 600, 158);
    draw_text(rp, 432, 152, "STATUS: CLICK", 13, 2);
}

static BOOL open_display(void)
{
    static const struct TagItem screen_tags[] = {
        {SA_Width, SCREEN_WIDTH},
        {SA_Height, SCREEN_HEIGHT},
        {SA_Depth, 4},
        {SA_DisplayID, HIRES_KEY},
        {SA_Type, CUSTOMSCREEN},
        {SA_Title, (ULONG)"Battalion: Fulda"},
        {TAG_DONE, 0}
    };
    struct TagItem window_tags[] = {
        {WA_CustomScreen, 0},
        {WA_Left, 0},
        {WA_Top, 0},
        {WA_Width, SCREEN_WIDTH},
        {WA_Height, SCREEN_HEIGHT},
        {WA_Backdrop, TRUE},
        {WA_Borderless, TRUE},
        {WA_Activate, TRUE},
        {WA_RMBTrap, TRUE},
        {WA_ReportMouse, TRUE},
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

    SetRGB4(&game_screen->ViewPort, 0, 0, 1, 2);
    SetRGB4(&game_screen->ViewPort, 1, 11, 12, 13);
    SetRGB4(&game_screen->ViewPort, 2, 15, 12, 2);
    SetRGB4(&game_screen->ViewPort, 3, 2, 6, 2);
    SetRGB4(&game_screen->ViewPort, 4, 3, 8, 3);
    SetRGB4(&game_screen->ViewPort, 5, 1, 3, 1);
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
                mouse_x < MAP_LEFT + (MAP_WIDTH << 4) &&
                mouse_y < MAP_TOP + (MAP_HEIGHT << 4)) {
                move_cursor(rp, (UWORD)(mouse_x - MAP_LEFT) >> 4,
                            (UWORD)(mouse_y - MAP_TOP) >> 4);
            } else if (event_class == IDCMP_MOUSEBUTTONS && code == SELECTDOWN) {
                show_click(rp);
            } else if (event_class == IDCMP_RAWKEY && !(code & 0x80U)) {
                if (code == 0x45U) running = FALSE;
                else if (code == 0x11U && cursor_y > 0) move_cursor(rp, cursor_x, cursor_y - 1);
                else if (code == 0x21U && cursor_y + 1 < MAP_HEIGHT) move_cursor(rp, cursor_x, cursor_y + 1);
                else if (code == 0x20U && cursor_x > 0) move_cursor(rp, cursor_x - 1, cursor_y);
                else if (code == 0x22U && cursor_x + 1 < MAP_WIDTH) move_cursor(rp, cursor_x + 1, cursor_y);
            }
        }
    }
}

int game_main(void)
{
    SysBase = *(struct ExecBase **)4UL;
    IntuitionBase = (struct IntuitionBase *)OpenLibrary(
        (CONST_STRPTR)"intuition.library", 39);
    if (!IntuitionBase) return 20;

    GfxBase = (struct GfxBase *)OpenLibrary((CONST_STRPTR)"graphics.library", 39);
    if (!GfxBase) {
        CloseLibrary((struct Library *)IntuitionBase);
        return 20;
    }

    if (open_display()) {
        input_loop();
        close_display();
    }

    CloseLibrary((struct Library *)GfxBase);
    CloseLibrary((struct Library *)IntuitionBase);
    return 0;
}
