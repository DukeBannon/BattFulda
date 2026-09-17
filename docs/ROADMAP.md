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
- **W2.6 — close combat and morale reactions:** assault resolution, retreat,
  surrender, and disruption behavior.
