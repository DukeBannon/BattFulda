# Unit capability audit: 1985, 250-meter hexes

## Scope and status

The 64 unit types retain provisional combat ratings. This is a researched first
pass, not certification of the complete database. Research must distinguish
variant, date, ammunition, weapon reach, useful engagement range, and sensor
capability. Later-era specifications must not be applied to 1985 equipment.

## Corrections applied

| Unit | Previous | Corrected | Basis |
| --- | --- | --- | --- |
| 105 mm M1 | 4 hexes / 1000 m | 10 hexes / 2500 m | FM 20-32 Table 2-6 maximum effective range; planning range is 2000 m |
| Improved TOW / TOW 2 | 8 hexes / 2000 m | 15 hexes / 3750 m | Army AL&T TOW evolution: extended range in 1978; Improved TOW 1981; TOW 2 1983 |
| T-64B main gun | 5 hexes / 1250 m | 8 hexes / 2000 m | FM 100-2-3 gives 2100 m effective HVAPFSDS range; rounded down to whole hexes |

Sources:

- [FM 20-32](https://www.marines.mil/Portals/1/Publications/FM%2020-32%20W%20CH%201-4.pdf)
- [Army AL&T: The TOW Missile](https://asc.army.mil/docs/pubs/alt/2009/3_JulAugSep/articles/31_The_TOW_Missile--Precise_and_Powerful_200907.pdf)
- [FM 100-2-3](https://www.trngcmd.marines.mil/Portals/207/Docs/MCIS/ITEP/RITC-East/FM%20100-2-3.pdf)

These later publications distinguish older weapon variants, but are not a
substitute for a formation-specific 1985 ammunition and equipment audit.
CSV seeds, SQLite records, and runtime exports are synchronized without
rebuilding the map. Use `python tools/manage_database.py sync-ranges`.

## Required follow-up

- Separate weapon profiles and ammunition from unit types. One range field
  cannot correctly represent Bradley cannon versus TOW, BMP cannon versus
  ATGM, or T-64B main-gun rounds versus gun-launched AT-8.
- Research all remaining unit types, prioritizing M3 Bradley, BMP-2, and the
  ambiguous AT-4/AT-5 team used in the current scenario. Split that team into
  distinct 2000 m and 4000 m missile variants rather than assigning both one
  maximum range. Do not arbitrarily give a Bradley cannon the TOW range.
- Review attack and protection ratings separately: numeric ratings are design
  abstractions, not verified armor thickness or penetration figures.
- Convert artillery and air-defense ranges separately; do not apply their
  missile/indirect ranges to direct ground fire. Some seed notes still describe
  500-meter cells and require individual review.
- Account for range bands, ammunition, target armor/facing, posture, and
  sensor/weather limitations before calling the combat model realistic.

## Stacking design recommendation (not implemented)

Permit two friendly maneuver platoons to share a hex as a provisional capacity
rule, with small command/support elements handled by size rather than a flat
counter count. Allow friendly passage through occupied hexes, independently of
end-of-move stacking. Narrow roads and bridges require traffic scheduling;
congestion should cause delay, not an unexplained permanent block. Enemy
occupation remains a combat/assault problem, never friendly passage.

Sharing a cell is a spatial abstraction, not a claim that both platoons achieve
full dispersion or unrestricted firing positions. Concentration penalties and
counter selection/display need design and testing. Modern Army road-march
guidance uses 50-100 m vehicle distances for open columns and 20-25 m for close
columns: passing occupies road space and time even when vehicles physically fit.

## Current terrain resolution (implemented, provisional)

Intervening woods block LOS beyond the first wooded destination. Elevation and
buildings can also block. Concealing destinations produce obscured LOS. In
combat scoring, obscured LOS subtracts 2; urban destination subtracts another
3, woods 2, cultivated 1, and other terrain 0. These are cumulative. Terrain
does not currently reduce suppression separately, model hull-down positions,
or distinguish protection for dismounted infantry from armored vehicles.
Rivers and roads primarily affect movement, not combat scoring.

Uniform cultivated-ground protection is especially provisional: crops and
hedgerows may conceal troops but should not automatically protect tank armor.
