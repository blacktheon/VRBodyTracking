# Blacktheon Body Tracking

A Unity 6000.3 client for SlimeVR on a Windows PC. For **v0.1.4**, choose **Install package from disk** in Unity Package Manager and select this folder's `package.json`. Once the tag is on GitHub, **Install package from git URL** accepts the address below. The runtime assembly is `Blacktheon.BodyTracking`.

```text
https://github.com/blacktheon/VRBodyTracking.git?path=/Packages/com.blacktheon.body-tracking#v0.1.4
```

Attach `BodyTrackingClient` to a GameObject. Call `ConnectAuto` to find SlimeVR on the same LAN. The optional last argument is a previously confirmed host to try first. Read `ConnectedHost` after the server replies to display or remember its address. To choose a specific server, call `Connect(pcHost, slot, mode, origin, head)` with a LAN host name or IP address. The default tracking port is TCP 21110. An explicit `host:port` also works.

For Android, set **Internet Access** to **Require** in Player Settings. This package includes the Wi-Fi multicast permission for discovery. The new paired server answers a discovery request on UDP 6969 in both solo and paired modes. Quest clients also listen for the stock SlimeVR broadcast, which v21.1.0 sends while it has no active UDP trackers. This covers USB receiver kits. On a network that blocks discovery, or with a stock server already handling Wi-Fi trackers, use a known host. The component reconnects after a network interruption and releases its sockets when the app loses focus. Call `Disconnect()` to stop.

## Six trackers on one wearer

Assign the physical IMUs and calibrate the body and mounting in SlimeVR v21.1.0. Supply the Unity floor tracking origin and the headset camera transform. Use metre-scale coordinates. A locomotion rig can translate and turn the origin during play.

```csharp
using Blacktheon.BodyTracking;

// client is a BodyTrackingClient, floorOrigin and headset are ordinary Transforms.
client.ConnectAuto(1, BodyTrackingMode.SixTrackers, floorOrigin, headset);

// For example, update a foot target each LateUpdate.
if (client.TryGetJoint(BodyJoint.LeftFoot, out var foot))
    leftFootTarget.SetPositionAndRotation(foot.position, foot.rotation);
```

The stock bridge accepts one HMD and returns its solved skeleton. The client follows the announced head, chest, waist, knee and foot roles. Joint poses are in Unity world space with the current origin applied. Results expire 0.5 seconds after their last reply. The stock bridge reports solved poses even if a physical sensor is missing. Check physical tracker status in SlimeVR. `Calibrate()` sends the stock yaw reset. Full body and mounting resets are available in the SlimeVR UI.

## Two shoes per seated player

Use the paired-feet server extension shipped with the example. In SlimeVR, label the physical sensors `P1-L` and `P1-R`, with corresponding names for slots 2 and 3. Only one connected headset can hold each slot.

```csharp
client.ConnectAuto(playerSlot, BodyTrackingMode.PairedFeet, null, null);
client.Calibrate(); // Sit still with both shoes flat.

// Rest positions are expressed in the avatar's local frame.
client.TryGetFootLift(true, out float leftMetres);
client.TryGetFootLift(false, out float rightMetres);
leftFoot.localPosition = leftRest + UnityEngine.Vector3.up * leftMetres;
rightFoot.localPosition = rightRest + UnityEngine.Vector3.up * rightMetres;
```

Each shoe learns its neutral acceleration from roughly 0.35 seconds of quiet samples. Calibration also starts on connection, sensor replacement or recovery from signal loss. The client uses vertical, gravity-compensated acceleration to raise each foot independently. A still shoe holds its estimated height. A downward stroke lowers it, including a quick reversal without a pause at the top. Gentle descents and a downstroke followed by a landing impact can also return a boot to the floor. Joystick movement controls the avatar's horizontal motion.

Paired mode needs a continuous linear acceleration stream while both shoes are still. If the Quest reports **Check shoe acceleration stream**, confirm the physical trackers are online and have acceleration enabled. On ESP firmware v0.7.4, use `SEND_ACCELERATION=true` and `OPTIMIZE_UPDATES=false` for a level shoe that lifts without changing orientation. [SlimeVR firmware settings](https://github.com/SlimeVR/SlimeVR-Tracker-ESP/blob/v0.7.4/src/debug.h)

The two IMUs give a motion estimate. A stationary raised shoe and a stationary grounded shoe can have identical readings. Slow movement below the onset threshold, a missed impulse, or calibration with raised shoes can produce an incorrect height. Lower both shoes and call `Calibrate()` to recover. The component Inspector exposes **Motion Threshold** (default 0.7 m/s²) and **Maximum Foot Lift** (default 0.35 m). Lower the threshold for gentle movement or raise it to reject bumps. Inspector settings take effect at the next connection.

`TryGetFootLift` returns `false` and zero metres for an unavailable or calibrating foot. Only a newer source timestamp refreshes a shoe. Without a new sample for 0.4 seconds, that foot falls back to ground. Read `Status` to display connection, calibration and missing-shoe feedback. Paired mode only requests the selected shoe pair and keeps the headset pose local.

## Tests

Add `com.blacktheon.body-tracking` to the consuming project's `testables` in `Packages/manifest.json`. Run `Blacktheon.BodyTracking.Tests` under Edit Mode. The tests include a loopback WebSocket peer and synthetic seated motion. Validate mounting and thresholds with the actual shoes before use.
