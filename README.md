# Battalion: Fulda

`Battalion: Fulda` is an Amiga 1200 tactical wargame inspired by the design of
SSI's *Mech Brigade*. The production runtime is written in straightforward C,
with assembly reserved for measured performance needs. Editable maps, units,
equipment, and scenarios remain external data compiled by Python.

The stock Amiga 1200 baseline is a 68020, AGA, and 2 MB of Chip RAM. VS Code is
the recommended editor, but PowerShell scripts are the authoritative builds.

## Amiga toolchain

- BartmanAbyss Amiga Debug VS Code extension, which supplies
  `m68k-amiga-elf-gcc`, `elf2hunk`, and `exe2adf`
- A licensed Kickstart 3.1 A1200 ROM
- WinUAE at `C:\Emulators\WinUAE\winuae64.exe`
- The existing `A1200 Basic.uae` WinUAE configuration
- PowerShell

## Build and run the current Amiga milestone

Open a PowerShell terminal and run:

```powershell
cd C:\Repositories\Games\BattFulda
.\build-amiga.ps1 -Clean -Test -Run
```

The build creates:

```text
build\amiga\battalion_fulda_a2.exe
build\amiga\battalion_fulda_a2.adf
```

A2.2 loads the Fulda terrain plus database-compiled unit types, formations, and
scenario units from the bootable disk. It provides vertical map panning,
mouse/WASD navigation, unit selection, and a tactical information panel backed
by the SQLite-authored order of battle. See `docs\A2.2.md` for the acceptance
test. The completed A1 milestone is documented in `docs\A1.md`.

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

## Preserved C64 D1 prototype

The earlier C64 proof of concept remains buildable for reference. It uses cc65,
Python, and VICE:

```powershell
cd C:\Repositories\Games\BattFulda
.\build.ps1 -Clean -Test
```

It produces `build\battalion_fulda_d1.prg`. See `docs\D1.md` for its
architecture and binary data formats.
