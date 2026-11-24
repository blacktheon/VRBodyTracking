# Quest body tracking for Unity

Use six SlimeVR trackers with one Quest 3, or give three seated players one tracker per shoe. The headset runs the Unity app locally. The PC receives the trackers, and an Android tablet hosts the multiplayer game.

Download **VRBodyTracking-Windows-x64.zip** and **BodyTracking-Quest.apk**. For multiplayer, also download **BodyTracking-Tablet.apk**. Extract the Windows ZIP before starting either launcher. [Download the v0.1.0 test builds](https://github.com/blacktheon/VRBodyTracking/releases/tag/v0.1.0)

To sideload via USB, enable developer mode and authorize USB debugging on the target device. Run `adb devices` to find its serial. Use `adb -s SERIAL install -r BodyTracking-Quest.apk` for a headset or `adb -s SERIAL install -r BodyTracking-Tablet.apk` for a tablet. Open **Body Tracking Quest** from **Unknown Sources** on the Quest or **Body Tracking Tablet** from the Android launcher.

| Mode | Physical trackers | What the Quest displays |
| --- | --- | --- |
| Six-tracker solo | Chest, hip, both thighs and both lower legs on one player | SlimeVR's solved body joints, including the knees and feet |
| Paired feet | Left and right shoe for each of three players | Independent foot lifts and drops, plus the other players' avatars |

## Import into another Unity project

In Unity 6.3, open **Window → Package Management → Package Manager**, choose **+ → Install package from git URL**, and paste the entire line.

```text
https://github.com/blacktheon/VRBodyTracking.git?path=/Packages/com.blacktheon.body-tracking#v0.1.0
```

For the current development branch, replace `#v0.1.0` with `#develop`. The package reads a Unity tracking origin and head transform. It can drive an existing avatar or feed another networking system. The demo project adds the Meta Quest SDK and VRNet. [Package API and usage](Packages/com.blacktheon.body-tracking/README.md)

## Try one player with six trackers

1. Run `Start-Stock-Solo.cmd` on the PC and pair all six sensors in the SlimeVR setup UI. Assign one sensor to each of the six body locations in the table. Complete SlimeVR's mounting and body-dimension setup. [SlimeVR tracker placement](https://docs.slimevr.dev/server/putting-on-trackers.html)
2. Connect the Quest 3 to a network that can reach the PC. Install and open **BodyTracking-Quest.apk**. Choose the six-tracker mode and enter the PC's LAN address.
3. Calibrate and move a single leg. Check that the knee and foot on that side respond. The headset supplies the head pose and reads solved joints directly from the PC. This test works without the tablet.

## Try three seated players

1. Close the stock server and run `Start-Paired.cmd` on the PC. In SlimeVR's tracker settings give each physical sensor its own custom name. Assign the left and right shoes as `P1-L` and `P1-R`, `P2-L` and `P2-R`, and `P3-L` and `P3-R`. An extension sensor gets its own name, even when it shares hardware with another sensor.
2. Install and open **BodyTracking-Tablet.apk** on the Android tablet. The game server starts automatically and shows the tablet address and three seats. Connect the three Quests and the PC to the same reachable LAN. A tablet hotspot also needs to allow traffic between its clients.
3. Install the same **BodyTracking-Quest.apk** on all three Quests. In each headset choose **Paired feet** and a different seat, then enter the PC address. Leave the tablet address on **Auto** for discovery or enter the address on its dashboard.
4. Have each player sit normally with both shoes on the floor and calibrate. Lift just player 2's left foot, hold it, then set it down. The other headsets should see that foot rise and settle. Repeat on the right, then try simultaneous movements on different players.
5. Walk with the left joystick and turn with the right. The feet travel with the virtual chair and each shoe controls its own vertical lift. Lean forward or sideways in the physical chair to check that the grounded feet stay in place.

In the headset menu, move the left stick to select an item and press **A** to edit it. The address keypad works with the same two controls. In the room, **A** calibrates and **B** reopens settings. Each virtual chair has its own starting position, so the three physical chairs can face different directions.

The two-sensor mode estimates lift from vertical acceleration. A stationary shoe produces the same acceleration whether it is raised or on the floor. The client uses the preceding motion to maintain the lift and uses calibration to establish the floor again. Tracker status distinguishes a missing signal from a foot resting on the floor.

**Check the shoe data before your first session.** Leave both shoes flat for a second. The Quest should say **Tracking both feet** and both sides should continue showing zero. If it says **Check shoe acceleration stream** or a still foot repeatedly loses tracking, check that the tracker sends continuous linear acceleration. On ESP trackers running SlimeVR firmware v0.7.4, rotation-based update suppression can skip a level shoe's lift or hold. For a board-specific firmware build, set `SEND_ACCELERATION` to `true` and `OPTIMIZE_UPDATES` to `false` in `src/debug.h`. Other tracker models may expose this differently. [Upstream firmware settings](https://github.com/SlimeVR/SlimeVR-Tracker-ESP/blob/v0.7.4/src/debug.h) · [Acceleration transmission](https://github.com/SlimeVR/SlimeVR-Tracker-ESP/blob/v0.7.4/src/sensors/sensor.cpp#L40-L74)

## Network connections

| Connection | Default endpoint | Purpose |
| --- | --- | --- |
| Wi-Fi trackers → Windows PC | UDP 6969 | SlimeVR sensor input |
| Each Quest → Windows PC | TCP 21110 | Solo body poses or that player's paired sensor readings |
| Each Quest → tablet | UDP 7777 | Multiplayer game traffic |
| Tablet → LAN | UDP 47777 | VRNet game discovery |

The tablet and the PC have different roles and may have different addresses. The paired mode reads only the headset's selected slot. Each connected Quest sends its own avatar movement to the tablet, which relays it to the other clients.

## Build from source

Use Unity **6000.3.19f1** with Android Build Support, its OpenJDK, SDK and NDK. Run these commands from the repository on Windows.

```powershell
.\Tools\Build-TestApps.ps1
.\Tools\SlimeVR\Build-Windows.ps1
```

The Android script writes the Quest and tablet APKs to `Builds` and checks their ARM64 ABI, internet permission and headset requirements. It builds the tablet in a project containing the networking and demo code so it launches as an ordinary Android app. Use `-Role Quest` or `-Role Tablet` to build one app. Open the test scenes in `Assets/BodyTrackingDemo/Scenes` to inspect their setup.

The Windows build script produces both server variants, the SlimeVR desktop UI and a bundled Java runtime. The paired-foot source patch and upstream notices are in `Tools/SlimeVR`. [SlimeVR Server v21.1.0](https://github.com/SlimeVR/SlimeVR-Server/releases/tag/v21.1.0)
