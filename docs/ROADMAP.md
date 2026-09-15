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
