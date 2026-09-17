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

## Build and run W2.4

Open `BattalionFulda.sln` in Visual Studio 2026, or run:

```powershell
cd C:\Repositories\Games\BattFulda
.\build-windows.ps1 -Run
```

W2.4 loads the 92 by 80 Point Alpha battlefield at 250 meters per hex. It retains
the original projected road and river paths, derives bridges at their geographic
intersections, distinguishes cultivated terrain, and caches visible terrain
chunks for smooth navigation. The compact hex-edge topology and full geographic
feature paths are both editable external data backed by SQLite.

Right-click a friendly unit to open its cascading action menu. Choose a movement
posture, then click or press Enter to add waypoints. Double-click or use the
footer to confirm the plan; Backspace undoes a waypoint and Escape cancels the
draft. Route previews use data-driven A* pathfinding with mobility-specific
terrain, road, elevation, bridge, ford, stream, and river costs. Illegal routes
are marked in red and cannot be confirmed.

The Execute footer command resolves every confirmed movement and direct-fire
order simultaneously across the scenario's 15-minute turn. Counters animate between
hexes, Quick/Tactical/Hunt costs determine their progress, occupied hexes cause
traffic holds, and unfinished routes carry into the next turn. Pause and Resume
are available during playback; completed movement enters a review phase before
the next planning turn.

Select a friendly counter and choose Fire from its right-click menu or the
footer. The target cursor previews a clear, obscured, blocked, or out-of-range
line of sight. A confirmed shot resolves during WEGO playback using the unit's
external hard/soft attack, defence, and range data, producing strength,
suppression, readiness, and morale effects. See `docs\W2.4.md` for the milestone
boundary and acceptance target.

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
regular flat-top hexes at 250 meters center-to-center. W1 introduced SQLite
schema version 6 for cells, connected feature edges, and original projected
feature vertices. W2.2 added movement costs and actual water-crossing
boundaries; W2.3 advances the current store to version 8 with a data-driven
scenario start clock. This gives the renderer natural
roads and waterways while preserving authoritative route topology.

Road-over-water bridges are derived from actual vector intersections. This
keeps roads from visually crossing streams or rivers without a bridge and gives
W2 a corresponding legal crossing edge.

Cultivated terrain is distinct from clear ground. Road classes and stream,
minor-river, and major-river classes now have database-driven movement effects.
Geographic waterways and actual crossing edges are stored separately so a path
crosses water only where its hex boundary intersects the mapped watercourse.
