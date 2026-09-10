; Link the compact Python-generated data directly into the C64 executable.

.export _map_data
.export _scenario_data
.export _unit_data

.segment "RODATA"

_map_data:
    .incbin "generated/map.bin"

_scenario_data:
    .incbin "generated/scenario.bin"

_unit_data:
    .incbin "generated/units.bin"
