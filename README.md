# VR Body Tracking for Quest 3

[![Release v0.1.0 prerelease](https://img.shields.io/badge/release-v0.1.0%20prerelease-7259A5)][release] ![Unity 6000.3.19f1](https://img.shields.io/badge/Unity-6000.3.19f1-263748?logo=unity&logoColor=white) [![SlimeVR Server v21.1.0](https://img.shields.io/badge/SlimeVR-v21.1.0-167D78)][slimevr]

Use six SlimeVR trackers on **one Quest 3** to see your knees and feet in a virtual mirror. Or give **three seated players** one tracker per shoe and let them see each other lift and lower their feet. The same standalone Quest app runs both modes. Unity developers can also import the tracking package into their own games.

**[Run the demo](#run-the-demo) · [Import into Unity](#import-into-unity) · [Build from source](#build-from-source) · [Troubleshooting](#troubleshooting)**

![Tablet dashboard showing three seated avatars and separate left and right foot heights for each player](docs/images/tablet-room.png)

*The game server running on a Galaxy Tab S9+ with three simulated headset clients. Seat 1 has its left foot raised 15 cm. Seat 2 has its right foot raised 25 cm.*

---

## Run the demo

| Where | Download | What it does |
| --- | --- | --- |
| Windows x64 PC | [Windows server ZIP][windows] | Receives all six trackers. Includes both SlimeVR launchers and Java. |
| Each Quest 3 | [Quest APK][quest] | Displays the solo mirror or joins a multiplayer seat. |
| Android tablet, for multiplayer | [Tablet APK][tablet] | Hosts the shared room and shows all three avatars. |

The standing and sitting controls below are in the [development build](#build-from-source). The linked **v0.1.0** APKs use **A** for calibration and show a seated multiplayer avatar.

Extract the entire Windows ZIP. When switching launchers, close the running SlimeVR GUI and server first. Connect the PC, trackers and headsets to the same LAN. Include the tablet for multiplayer. On Windows, run `ipconfig` and find the **IPv4 Address** of the Wi-Fi or Ethernet adapter on that network. This is the **Tracking PC** address the Quest asks for.

<details>
<summary><strong>Install the apps over USB</strong></summary>

Enable developer mode and USB debugging on the device, then accept the USB debugging prompt. From the download folder, use Android Platform Tools to list the connected devices. Replace each serial below with the one printed by `adb devices`.

```shell
adb devices
adb -s QUEST_SERIAL install -r BodyTracking-Quest.apk
adb -s TABLET_SERIAL install -r BodyTracking-Tablet.apk
```

On Quest, open **Body Tracking Quest** under **Unknown Sources**. On the tablet, open **Body Tracking Tablet** from the Android launcher.

</details>

### One player with six trackers

1. Run **`Start-Stock-Solo.cmd`** on the PC. Pair the sensors in SlimeVR and assign them to the **chest, hip, left and right thighs, and left and right lower legs**. [SlimeVR mounting guide][mounting]
2. Open the Quest app. Select **Six trackers / solo**, enter the PC's IPv4 address under **Tracking PC**, then choose **Start solo test**. The Quest now supplies the head pose to SlimeVR.
3. Complete the body measurements and mounting calibration in SlimeVR. With your shoes on the floor, face forward and press **X** in the room to reset yaw and set the first-person foot reference. Raise one knee and watch the knee and foot on that side move in the mirror. **A** switches your first-person avatar between standing and sitting. The mirror shows the six-tracker solve.

<img src="docs/images/solo-mirror.png" alt="Unity view of the solo mirror showing the solved head, torso, knees and feet" width="640">

*The solo mirror in Unity, driven by the stock Windows server and six simulated sensors. The counter shows how many of the seven solved joints the headset is receiving.*

### Three seated players with two trackers each

1. Run **`Start-Paired.cmd`** on the PC. Pair the sensors in SlimeVR, then give each physical sensor the exact **custom name** shown below. Each extension sensor gets its own name, even if it shares a board with another sensor.

   | Quest mode | Left shoe | Right shoe |
   | --- | --- | --- |
   | **Paired feet / seat 1** | `P1-L` | `P1-R` |
   | **Paired feet / seat 2** | `P2-L` | `P2-R` |
   | **Paired feet / seat 3** | `P3-L` | `P3-R` |

2. Open the tablet app. Its game server starts automatically and displays its LAN address. On each Quest, select a different seat and enter the same **Tracking PC** address. Leave **Tablet** on **Find on Wi-Fi** or enter the tablet's displayed address. Choose **Enter multiplayer room**.
3. Sit facing forward with both shoes flat. Press **X** on the left controller and hold still until the headset says **Tracking both feet**. Have seat 2 lift only its left foot, hold it, then lower it. Watch **Seat 2 → L** on the tablet and the same foot on the other players' avatars.
4. Your avatar starts standing while you sit in your physical chair. Press **A** on the right controller to sit in the game, and press it again to stand. The other players and the tablet see your pose. The left stick moves you and the right stick turns you. Both shoes stay at floor height during joystick travel until you physically lift one.

<img src="docs/images/headset-setup.png" alt="Quest setup menu with paired feet for seat 2, a tracking PC address field, tablet discovery and the Enter multiplayer room action" width="560">

*The headset menu in Unity with seat 2 selected. Press **B** in the room to return here.*

| Controller | In settings | In the room |
| --- | --- | --- |
| Left stick | Select a row or keypad key | Walk |
| Right stick | — | Turn |
| **A** (right controller) | Change the mode, press a key or activate the selected row | Toggle standing or sitting |
| **X** (left controller) | — | Reset yaw and foot reference in solo mode or calibrate the shoes in paired mode |
| **B** | Back out of the keypad or close settings | Open settings |

<details>
<summary><strong>Network layout and ports</strong></summary>

```mermaid
flowchart LR
    sensors["SlimeVR trackers"] -->|UDP 6969| pc["Windows PC · SlimeVR"]
    pc <-->|TCP 21110| quest["Quest 3 clients"]
    quest <-->|UDP 7777| tablet["Android tablet · game server"]
```

Each Quest reads its own pair from the PC, then sends its avatar pose to the tablet. The tablet relays those poses and advertises the game on **UDP 47777**. A tablet hotspot works when its clients can reach the PC and one another.

</details>

---

## Import into Unity

In Unity 6.3, open **Window → Package Management → Package Manager → + → Install package from git URL** and paste the full line.

```text
https://github.com/blacktheon/VRBodyTracking.git?path=/Packages/com.blacktheon.body-tracking#v0.1.0
```

Look for **Blacktheon Body Tracking** by **blacktheon**. Add `BodyTrackingClient` to a GameObject. The package works with your own rig, avatars and networking. For an Android build, set **Internet Access** to **Require** in Player Settings. [Package API and examples][api]

| To read | Connect with | Use in your game |
| --- | --- | --- |
| Solved body joints | `BodyTrackingMode.SixTrackers`, a floor tracking origin and the headset transform | When `TryGetJoint(BodyJoint.LeftFoot, out var pose)` succeeds, `pose` is in Unity world space. |
| Seated foot lifts | `BodyTrackingMode.PairedFeet` and a seat number from 1 to 3 | `TryGetFootLift(true, out var metres)` reads the left shoe. Pass `false` for the right. |

<details>
<summary><strong>Example · drive two seated foot targets</strong></summary>

Save this as `SeatedFeet.cs`, attach it to a GameObject, and assign the two avatar foot targets in the Inspector. Enter the tracking PC's address and this player's seat. Start with both real feet flat.

```csharp
using Blacktheon.BodyTracking;
using UnityEngine;

[RequireComponent(typeof(BodyTrackingClient))]
public class SeatedFeet : MonoBehaviour
{
    public string pcAddress;
    [Range(1, 3)] public int seat = 1;
    public Transform leftFoot, rightFoot;

    BodyTrackingClient client;
    Vector3 leftRest, rightRest;

    void Start()
    {
        client = GetComponent<BodyTrackingClient>();
        leftRest = leftFoot.localPosition;
        rightRest = rightFoot.localPosition;
        client.Connect(pcAddress, seat, BodyTrackingMode.PairedFeet, null, null);
    }

    void LateUpdate()
    {
        client.TryGetFootLift(true, out float left);
        client.TryGetFootLift(false, out float right);
        leftFoot.localPosition = leftRest + Vector3.up * left;
        rightFoot.localPosition = rightRest + Vector3.up * right;
    }
}
```

An unavailable shoe returns `false` and zero metres, so this example puts that foot back at rest. Your UI can use the return value and `client.Status` to show signal loss. Call `client.Calibrate()` while seated to reset the floor reference.

</details>

## Build from source

Use **Unity 6000.3.19f1** with Android Build Support, including Unity's OpenJDK, SDK and NDK. The working Unity project is on **`develop`**. To reproduce the published version, use **`v0.1.0`** in the clone command. [Browse the Unity project][develop]

```powershell
git clone --branch develop https://github.com/blacktheon/VRBodyTracking.git
cd VRBodyTracking
.\Tools\Build-TestApps.ps1
```

The script writes both APKs to `Builds/`. Use `-Role Quest` or `-Role Tablet` for a single build. The demo entry points are `Quest.unity` and `Tablet.unity`. [Demo scenes][scenes]

To rebuild the Windows companion, install Git and Node.js 22 or newer, then run `.\Tools\SlimeVR\Build-Windows.ps1`. The script builds and tests the stock and paired servers against SlimeVR v21.1.0 and writes the ZIP under `Builds/SlimeVR/`. [Windows build and protocol guide][server-source]

## Troubleshooting

| What you see | What to check |
| --- | --- |
| The headset is waiting for the PC | Check **Tracking PC** against `ipconfig`, confirm the correct launcher is running, and allow TCP 21110 on the PC's private LAN. |
| The tablet room is missing | Open the tablet app, enter its displayed IP on the Quest, and check that the Wi-Fi or hotspot permits traffic between clients on UDP 7777. |
| A seat is occupied or a shoe is unavailable | Give each headset its own seat. Check that each sensor is online and its custom name appears exactly once, including saved sensors currently unplugged. |
| A level shoe loses tracking when held still | Paired mode needs continuous linear acceleration. On SlimeVR ESP firmware v0.7.4, check `SEND_ACCELERATION=true` and `OPTIMIZE_UPDATES=false` in the firmware build for your board. [Firmware settings][firmware] |
| A foot stays raised after landing | Put both shoes flat and press **X** (**A** in v0.1.0). A still shoe produces the same acceleration at any height. The app estimates lift from the preceding movement, so a missed impulse can leave the estimate high. |

**Test coverage for v0.1.0.** Six simulated tracker devices drove the Windows servers. Three Unity clients exchanged independent foot lifts through a physical Galaxy Tab S9+, including a disconnect and rejoin. A clean Unity project installed the tagged Git package and passed its 24 package tests. Physical Quest 3 and shoe-mounted tracker validation is still pending.

[release]: https://github.com/blacktheon/VRBodyTracking/releases/tag/v0.1.0
[windows]: https://github.com/blacktheon/VRBodyTracking/releases/download/v0.1.0/VRBodyTracking-Windows-x64.zip
[quest]: https://github.com/blacktheon/VRBodyTracking/releases/download/v0.1.0/BodyTracking-Quest.apk
[tablet]: https://github.com/blacktheon/VRBodyTracking/releases/download/v0.1.0/BodyTracking-Tablet.apk
[api]: https://github.com/blacktheon/VRBodyTracking/blob/v0.1.0/Packages/com.blacktheon.body-tracking/README.md
[develop]: https://github.com/blacktheon/VRBodyTracking/tree/develop
[scenes]: https://github.com/blacktheon/VRBodyTracking/tree/v0.1.0/Assets/BodyTrackingDemo/Scenes
[server-source]: https://github.com/blacktheon/VRBodyTracking/blob/v0.1.0/Tools/SlimeVR/README.md
[slimevr]: https://github.com/SlimeVR/SlimeVR-Server/releases/tag/v21.1.0
[mounting]: https://docs.slimevr.dev/server/putting-on-trackers.html
[firmware]: https://github.com/SlimeVR/SlimeVR-Tracker-ESP/blob/v0.7.4/src/debug.h
