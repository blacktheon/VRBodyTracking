# Seated Quest foot tracking implementation plan

Implement the [tracking design](../specs/2026-11-25-body-tracking-design.md) in four independently verifiable parts.

1. Build SlimeVR Server v21.1.0 and test its stock head-to-body WebSocket with six simulated sensor devices. Add an opt-in paired-foot request to that server, with slot ownership, per-sensor freshness and a reproducible source patch. Exercise three simultaneous clients and loss of a tracker.
2. Add the reusable Unity package `com.blacktheon.body-tracking`. Support stock solved joint poses and independent foot lifts from paired vertical acceleration. Test coordinate conversion, lift/hold/drop, disconnects and stale data. Keep Meta and game networking dependencies in the example project.
3. Build the example using the existing headset rig and VRNet tablet session. Provide seat calibration, adjustable PC address and player slots, joystick walking and turning, and visible per-player feet. Exercise the shared pose transport and late joining.
4. Build both ARM64 Android apps, inspect their manifests, and install the package from its published Git URL in a clean consumer project. Deliver the PC launcher, APKs and one setup guide with the remaining physical tests.
