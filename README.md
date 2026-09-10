# Battalion: Fulda

`Battalion: Fulda` is an Amiga 1200 tactical wargame inspired by the design of
SSI's *Mech Brigade*. The production runtime is written in straightforward C,
with assembly reserved for measured performance needs. Editable maps, units,
equipment, and scenarios remain external data compiled by Python.

The stock Amiga 1200 baseline is a 68020, AGA, and 2 MB of Chip RAM. VS Code is
the recommended editor, but PowerShell scripts are the authoritative builds.

## Amiga A0 toolchain

- BartmanAbyss Amiga Debug VS Code extension, which supplies
  `m68k-amiga-elf-gcc`, `elf2hunk`, and `exe2adf`
- A licensed Kickstart 3.1 A1200 ROM
- WinUAE at `C:\Emulators\WinUAE\winuae64.exe`
- The existing `A1200 Basic.uae` WinUAE configuration
- PowerShell

## Build and run A0

Open a PowerShell terminal and run:

```powershell
cd C:\Repositories\Games\BattFulda
.\build-amiga.ps1 -Clean -Run
```

The build creates:

```text
build\amiga\battalion_fulda_a0.exe
build\amiga\battalion_fulda_a0.adf
```

A0 verifies a native high-resolution Amiga screen and a shared map cursor
controlled by the Amiga mouse or WASD. Left-click reports selection and Escape
exits cleanly. See `docs\A0.md` for the acceptance test.

## Preserved C64 D1 prototype

The earlier C64 proof of concept remains buildable for reference. It uses cc65,
Python, and VICE:

```powershell
cd C:\Repositories\Games\BattFulda
.\build.ps1 -Clean -Test
```

It produces `build\battalion_fulda_d1.prg`. See `docs\D1.md` for its
architecture and binary data formats.
