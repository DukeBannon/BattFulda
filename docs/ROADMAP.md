# Battalion: Fulda roadmap

## Deferred planning features

### Timed waypoints and deliberate delays

Movement planning may display the estimated game time at which a unit will
reach each waypoint. A player may optionally add a hold or delay at a waypoint
to coordinate movement, fires, and simultaneous arrivals during WEGO
execution. Arrival estimates must account for the final W2 movement-cost
model, posture, unit condition, command delay, and interruptions; therefore
this is intentionally deferred until routing and the WEGO resolver are stable.

The interface should remain optional and unobtrusive because many players will
not need detailed timing for routine moves.

## Next combat milestones

- **W2.5 — spotting and fog of war:** difficulty-level visibility, positive and
  uncertain contacts, sound and movement cues, identification quality, and
  memory decay built on W2.4 LOS.
  Initial playable scope accepted by the user on 2026-09-17. Further
  acquisition/identification tuning remains deferred, not a closure blocker.
  See `W2.5.md` for the current implementation boundary.
- **W2.6 — close combat and morale reactions:** assault resolution, retreat,
  surrender, and disruption behavior.

## Follow-ups from W2.5 acceptance

- Investigate inability to plot an upper-right adjacent move from the selected
  M1 near cell 41,44. Screenshot shows ORDER CANCELLED and an existing planned
  move, not a route rejection; cause is unconfirmed. Include road-snapped
  counter alignment versus logical hex position in reproduction.
- Audit cavalry-versus-tank combat balance before treating results as realistic:
  separate Bradley cannon/TOW profiles, terrain and exposure, suppression,
  and formation strength. Current generic ratings remain provisional.
- Address asymmetric engagement control: Soviet AI generates fire orders;
  NATO units require explicit orders every turn. Automatic engagement/return
  fire and rules of engagement remain unimplemented.
