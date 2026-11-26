# Quest body tracking with six SlimeVR sensors

## Test setup

A Windows PC receives six physical IMUs. One Quest 3 can wear the complete set on the chest, hips, thighs and shins to test SlimeVR's normal body solver. In the multiplayer test three people wear one tracker on each shoe. Each standalone Quest gets the readings for its wearer over Wi-Fi. An Android tablet runs the game server. A player looking down sees their own feet, and looking across the scene sees the other players and their foot movements.

## Tracking approaches

Use the stock SlimeVR v21.1.0 desktop solver for the single-wearer test. The Quest sends a floor-relative head pose on the server's existing WebSocket and receives solved body poses, mapped by the roles the server announces. The stock bridge represents one head and one skeleton.

For three wearers, extend the same Windows server with a request for one pair of physical sensors. Give each sensor a persistent label in the SlimeVR setup UI, such as `P1-L` and `P1-R`. The server resolves labels against physical sensors, including extensions. A second headset claiming an occupied slot receives an explicit error. Missing, duplicate or stale readings appear as unavailable. Disconnecting releases the slot. This mode keeps the three head poses on their respective Quests.

A separate SlimeVR process per person would require separate device routing and three configurations on the PC. Building three full-body solvers would still leave each solver with two IMUs and an underdetermined leg pose. The proposed implementation limits the two-sensor estimate to the foot movements the game needs.

## Player experience

- The same Quest APK offers a six-tracker solo test and three numbered player slots. The operator enters the PC's LAN address. The tablet accepts LAN game connections. Game networking uses the existing VRNet project dependency.
- Each player sits in a chair with both feet flat for calibration. Detect lifts and drops from gravity-compensated vertical acceleration, including lifts with little foot rotation. Use hysteresis and bounded displacement, show loss of tracking, and return to a grounded display on stale data. Static IMUs at different heights can produce the same reading. The preceding movement supplies the indication that a foot was raised.
- Give each player a distinct virtual spawn. The left joystick walks and the right joystick turns. The feet have fixed lateral and forward offsets in the seated avatar's frame. Each tracker changes only its foot's vertical offset. The players can use separate rooms without aligning their chairs.
- The Unity client drives its own head and hands using the Quest rig. Each avatar has visible left and right feet. The tablet relays each owner's pose and foot state, including the current state for late joiners. A client dropping its PC connection keeps its game connection and displays the tracking loss.

## Distribution and verification

Publish an embedded Unity Package Manager package under `Packages/com.blacktheon.body-tracking` on this repository's `develop` branch. Its tracking API accepts an ordinary Unity head transform and tracking origin. Keep the example scene and tablet build outside the core package. Other projects can use their own XR rig, network stack and avatars. Ship the Windows server modification as reproducible source against the official pinned commit, with SlimeVR's original notices.

Build the Windows server, a standalone ARM64 Quest test APK and an ordinary ARM64 tablet server APK. Verify the server with six synthetic firmware devices, pair isolation with three simultaneous clients, reassign/reconnect and loss handling, both APK manifests, and package installation from its published Git URL in a clean project. Physical Quest tracking, shoe mounting and LAN calibration require an on-device test.
