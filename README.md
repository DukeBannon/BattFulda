# Battalion: Fulda

`Battalion: Fulda` is a Commodore 64 game project. The current D0 milestone is
limited to verifying the assembler toolchain with a minimal runnable program.

## D0 prerequisites

- Kick Assembler at `C:\MiscApps\KickAssembler\KickAss.jar`
- Java available on `PATH` as `java`
- VICE x64sc at `C:\Emulators\VICE\bin\x64sc.exe`
- Python 3 available on `PATH` as `python` (reserved for later tooling; D0 does
  not invoke Python)

VS64 is optional IDE support and is not part of the build process.

## Build D0

Open a PowerShell terminal and run:

```powershell
Set-Location C:\Repositories\Games\BattFulda
.\build.ps1
```

The script assembles `src\main.asm` and writes the PRG plus Kick Assembler
symbol/debug files to `build\`. The runnable program is:

```text
C:\Repositories\Games\BattFulda\build\battalion_fulda_d0.prg
```

## Build and run D0 in VICE

From the repository root, run:

```powershell
.\build.ps1 -Run
```

After a successful assembly, the script launches the resulting PRG in x64sc.
The C64 screen displays:

```text
BATTALION: FULDA
D0 TOOLCHAIN TEST
BUILD SUCCESSFUL
```
