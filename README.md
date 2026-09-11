# Battalion: Fulda

`Battalion: Fulda` is a Windows tactical wargame inspired by SSI's *Mech
Brigade* and the map-first presentation of John Tiller's operational games. The
production runtime is written in C# with .NET 10. Editable maps, units,
equipment, formations, and scenarios remain external data authored in SQLite
and prepared by Python.

The earlier Amiga 1200 and C64 prototypes remain preserved and buildable. They
validated the data model, input design, scrolling battlefield, and visual
requirements before the Windows production pivot.

## Windows toolchain

- Visual Studio Community 2026 with the .NET desktop workload
- .NET 10 SDK
- PowerShell
- Python 3
- SQLite or SQLiteStudio for data authoring

Visual Studio 2026 is the recommended IDE. PowerShell and `dotnet` remain the
authoritative build path, so an IDE-specific project configuration is never
required to reproduce a build.

## Build and run W0

Open `BattalionFulda.sln` in Visual Studio 2026, or run:

```powershell
cd C:\Repositories\Games\BattFulda
.\build-windows.ps1 -Run
```

The first W0 foundation loads the real Point Alpha map and database-exported
scenario units. It establishes the map-first desktop layout on a conventional
flat-top hex grid, smooth panning and zooming, unit and terrain selection,
inspector, counters, and minimap. See
`docs\W0.md` for its visual acceptance target.

## Preserved Amiga prototype

The stock Amiga 1200 prototype targets a 68020, AGA, and 2 MB of Chip RAM.

### Amiga toolchain

- BartmanAbyss Amiga Debug VS Code extension, which supplies
  `m68k-amiga-elf-gcc`, `elf2hunk`, and `exe2adf`
- A licensed Kickstart 3.1 A1200 ROM
- WinUAE at `C:\Emulators\WinUAE\winuae64.exe`
- The existing `A1200 Basic.uae` WinUAE configuration
- PowerShell

### Build and run the current Amiga milestone

Open a PowerShell terminal and run:

```powershell
cd C:\Repositories\Games\BattFulda
.\build-amiga.ps1 -Clean -Test -Run
```

The build creates:

```text
build\amiga\battalion_fulda_a4.exe
build\amiga\battalion_fulda_a4.adf
```

A4.1 loads the 40 by 40 Point Alpha terrain map plus database-compiled unit
types, formations, and scenario units from the bootable disk. It introduces
the map-first Tiller-inspired interface foundation: a compact left inspector,
a 29 by 13 cell battlefield, top and bottom command strips, and buffered screen
updates. The first high-resolution AGA art pass adds textured terrain and
military counter symbols. Fast panning, mouse edge-scrolling, WASD navigation, unit
selection, and the SQLite-backed information display remain intact. See
`docs\A4.1.md` for the current acceptance test.
Earlier milestones remain documented under `docs`.

## A2.1 relational data foundation

SQLite is the authoritative development store for unit types, formation
hierarchies, scenarios, and scenario-unit state. Python validates the database,
exports readable CSV snapshots, and compiles compact Amiga binary files. The
Amiga runtime does not contain or depend on SQLite.

```powershell
cd C:\Repositories\Games\BattFulda
python .\tools\manage_database.py validate
python .\tools\manage_database.py export
python .\tools\manage_database.py compile
```

The normal Amiga build performs database validation and compilation through
`tools\compile_data.py`. See `docs\A2.1.md` for the schema and binary formats.

## A2.2 database-driven Amiga runtime

The Amiga runtime now reads `unit_types.bin`, `formations.bin`, and
`a2_scenario.bin` directly. Unit names, types, formation hierarchy, strength,
morale, suppression, readiness, and movement values shown by the tactical
display all originate in SQLite. The disk no longer carries or uses the legacy
A1 `units.bin` and `scenario.bin` fixtures.

## A3.1 geographic map foundation

The database contains a separate 40 by 40 cell, 500-meter production grid for
the Point Alpha-Huenfeld corridor. Its 1,600 elevations are aggregated from the
official German BKG DGM200 terrain model. Terrain, roads, rivers, settlements,
and bridges are independent editable cell properties. The initial grid is
initially marked `elevation_only`; A3.3 adds modern-reference features that
remain explicitly marked for historical review.

The compiler produces `generated\point_alpha_map.bin`, which A3.2 loads and
renders with elevation bands. See `docs\A3.1.md` for provenance, regeneration,
schema, and binary-format details.

## A3.2 in-game map viewer

The Amiga runtime now displays the database-authored Point Alpha map and lets
the player traverse all 1,600 cells. At a viewport edge it shifts the existing
raster and redraws only one newly exposed row or column. The information panel
shows the current cell's elevation and coordinates, while database-backed units
remain selectable. The present unit locations demonstrate interaction and are
not yet a historical scenario order of battle.

## A3.3 recognizable terrain pass

The production grid now combines official BKG DGM200 elevation with clipped
BKG DLM250 roads, waterways, woodland, settlements, water, and transport
structures. Python rasterizes the vector features into editable 500-meter
cells; the Amiga connects neighboring road and river marks and uses clearer
woodland, settlement, and bridge symbols. DLM250 is a modern reference layer,
so every affected cell remains marked `draft` until compared with period
evidence. See `docs\A3.3.md`.

## A4.0 visual foundation

The approved interface concept is now the visual north star for development.
The first implementation pass moves the information panel to the left and
expands the battlefield from 20 by 11 to 29 by 13 visible cells, while adding
compact title and command strips. It deliberately retains the proven terrain,
unit-data, selection, and scrolling systems. See `docs\A4.0.md` and the design
reference under `docs\design`.

## A4.1 AGA visual feasibility

The actual A1200 runtime now uses a controlled 16-color high-resolution palette. Reusable
pixel-art drawing rules give clear, wooded, rough, marsh, water, and urban
cells more depth; roads and waterways have contrasting edges; and unit
counters use faction frames with category-specific military symbols. The work
is deliberately performed inside the stock A1200 build so emulator output—not
the concept image—is the standard for acceptance. See `docs\A4.1.md`.

## A4.2 platform-decision visual slice

A4.2 will replace the A4.1 procedural artwork with production-quality authored
terrain and interface assets. It is intentionally broader than a normal art
increment: its representative battlefield must demonstrate the final font,
terrain, counters, overlays, labels, minimap, information hierarchy, and smooth
navigation well enough to decide whether the stock A1200 remains the target.

## Preserved C64 D1 prototype

The earlier C64 proof of concept remains buildable for reference. It uses cc65,
Python, and VICE:

```powershell
cd C:\Repositories\Games\BattFulda
.\build.ps1 -Clean -Test
```

It produces `build\battalion_fulda_d1.prg`. See `docs\D1.md` for its
architecture and binary data formats.
