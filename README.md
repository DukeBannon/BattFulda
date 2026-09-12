# Battalion: Fulda

`Battalion: Fulda` is a Windows tactical wargame inspired by SSI's *Mech
Brigade* and the map-first presentation of John Tiller's operational games. The
production runtime is written in C# with .NET 10. Editable maps, units,
equipment, formations, and scenarios remain external data authored in SQLite
and prepared by Python.

## Windows toolchain

- Visual Studio Community 2026 with the .NET desktop workload
- .NET 10 SDK
- PowerShell
- Python 3
- SQLite or SQLiteStudio for data authoring

Visual Studio 2026 is the recommended IDE. PowerShell and `dotnet` remain the
authoritative build path, so an IDE-specific project configuration is never
required to reproduce a build.

## Build and run W1

Open `BattalionFulda.sln` in Visual Studio 2026, or run:

```powershell
cd C:\Repositories\Games\BattFulda
.\build-windows.ps1 -Run
```

W1 loads the 92 by 80 Point Alpha battlefield at 250 meters per hex. It retains
the original projected road and river paths, derives bridges at their geographic
intersections, distinguishes cultivated terrain, and caches visible terrain
chunks for smooth navigation. The compact hex-edge topology and full geographic
feature paths are both editable external data backed by SQLite. See `docs\W1.md`
for its acceptance target and regeneration workflow.

## Relational data foundation

SQLite is the authoritative development store for unit types, formation
hierarchies, scenarios, scenario-unit state, terrain, and geographic features.
Python validates the database and exports readable CSV snapshots.

```powershell
cd C:\Repositories\Games\BattFulda
python .\tools\manage_database.py validate
python .\tools\manage_database.py export
```

The Windows game reads the exported map and scenario data directly. SQLiteStudio
can be used to inspect and edit the authoritative database.

## W1 geographic map foundation

The Windows production map covers a 20 by 20 km Point Alpha corridor with 7,360
regular flat-top hexes at 250 meters center-to-center. SQLite schema version 6
stores cells, connected feature edges, and original projected feature vertices.
This gives the renderer natural roads and waterways while preserving the
topology needed for W2 route planning and crossing rules.

Road-over-water bridges are derived from actual vector intersections. This
keeps roads from visually crossing streams or rivers without a bridge and gives
W2 a corresponding legal crossing edge.

Cultivated terrain is distinct from clear ground. Road classes and stream,
minor-river, and major-river classes are present now; their movement-time
effects will remain database data introduced in W2.
