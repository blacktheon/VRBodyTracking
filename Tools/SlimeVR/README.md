# Windows server source and protocol

The release includes two locally built desktop JARs, the SlimeVR v21.1.0 Windows x64 GUI, and the Eclipse Temurin 17.0.21.1+2 JRE. The paired JAR supports Quest discovery on the tracker UDP port and a paired-foot request on the existing WebSocket bridge. Both JARs include the stock single-wearer solver. [Operator instructions](Operator.txt)

## Build and test

From the Unity repository root on Windows, run the following command with Git, a JDK 17, the Android SDK and Node.js 22 or newer installed. The build detects the Unity editor's Android JDK and SDK when available. For another installation set `JAVA_HOME` and `ANDROID_HOME`.

```powershell
.\Tools\SlimeVR\Build-Windows.ps1
```

The build checks out upstream v21.1.0 at `c11dc54ed1a745ffc4b15db7f820d8d0c1993990` with its pinned submodules. It keeps separate stock and patched checkouts, pins the upstream desktop runtime's version ranges, builds both JARs, runs the upstream core tests and verifies both packaged JARs with synthetic firmware devices. The test starts its own server on TCP 21110 and an ephemeral tracker UDP port. Close any running SlimeVR server before testing. The build verifies the SHA-256 hashes of the GUI and JRE downloads. [Upstream source](https://github.com/SlimeVR/SlimeVR-Server/tree/c11dc54ed1a745ffc4b15db7f820d8d0c1993990) · [Source patch](paired-feet-v21.1.0.patch)

The distributable is `Builds/SlimeVR/VRBodyTracking-Windows-x64.zip`. The extracted copy is in `Builds/SlimeVR/Paired`. Use `Start-Stock-Solo.cmd` for the original solver or `Start-Paired.cmd` for the paired protocol. The ZIP includes the upstream license, trademark, Electron, Chromium and JRE notices. [Upstream MIT license](upstream-notices/LICENSE-MIT) · [Upstream Apache license](upstream-notices/LICENSE-APACHE) · [Trademark notice](upstream-notices/TRADEMARK.md)

For a JAR-only build run `Build-Server.ps1` with `-Paired` for the extension, or without a switch for stock. The scripts accept `-OutputDirectory` and `-JavaHome`. Run the following commands from the repository root to verify both JARs and their GUI launchers.

Windows Firewall remembers Java by its full path. Keep the same build output folder between runs. The tests reuse a byte-for-byte matching bundled JRE from the installed copy or a versioned build cache. Both GUI launchers use that JRE with disposable test profiles. For headset sessions, keep the extracted server in one folder as well. [Windows Firewall application rules](https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/rules#applications-rules)

```powershell
node Tools/SlimeVR/Test-Server.mjs --stock Builds/SlimeVR/Paired/Stock/slimevr.jar Builds/SlimeVR/Paired/jre/bin/java.exe
node Tools/SlimeVR/Test-Server.mjs
node Tools/SlimeVR/Test-Launchers.mjs
```

## Paired-foot wire contract

With the Quest tracking PC set to **Auto**, the headset broadcasts the ASCII string `BLACKTHEON_SLIMEVR_DISCOVER_1` on UDP 6969. The paired server replies directly to the sender from the same port with `BLACKTHEON_SLIMEVR_HERE_1`. Both messages have no terminator. This exchange works with active Wi-Fi trackers and USB receivers alike. Discovery leaves tracker registrations and player slots untouched. A Quest can also discover the stock server through its periodic heartbeat while it has no active UDP trackers. Enter the PC's LAN IPv4 address if the network filters broadcasts.

Open `ws://PC:21110` and send one JSON text request per poll. The stock bridge sends its normal tracker configuration announcements on connection. The paired client consumes replies whose type matches its request.

```json
{"type":"blacktheon.feet.v1","slot":1}
```

```json
{"type":"blacktheon.feet.v1","slot":1,"error":"","left":{"id":"udp://AA:BB:CC:DD:EE:FF/0","valid":true,"ay":1.25,"t":1240},"right":{"id":"udp://AA:BB:CC:DD:EE:FF/1","valid":true,"ay":0.0,"t":1242}}
```

- Slots 1 through 3 use the stock GUI's case-sensitive custom names `P1-L`, `P1-R`, through `P3-L`, `P3-R`. A durable ID contains the upstream hardware identity and sensor index. USB HID and UDP IMUs are eligible. Body-position assignments are optional.
- `ay` is the physical IMU's gravity-compensated linear acceleration along world +Y in m/s², sampled from `Tracker.getAcceleration().y`. The supported firmware packets carry acceleration without a sensor clock. `t` is the PC's monotonic millisecond time when SlimeVR ingested that acceleration. Repeated polls and rotation-only updates preserve it. Compare timestamps from the same live server session.
- A missing, ambiguous, disconnected or stale foot has `valid:false` and `ay:0`. A unique sensor retains its most recent measurement timestamp even after loss. A sensor with no measurement has `t:0`. Each foot becomes stale after 250 ms. Disconnecting and reconnecting requires a new measurement. A healthy foot keeps reporting if its partner is unavailable.
- The first successful poll claims a slot. A headset polls the same slot to renew its three-second lease. Closing its WebSocket releases the claim immediately. A new connection can claim an expired slot. An error response has two invalid feet with empty IDs and zero timestamps.
- The errors are `slot_in_use` for a live owner, `slot_locked` if a headset tries to switch slots on an established claim, and `invalid_slot` outside the supported integer range. Open a new connection to change player slots. A socket that enters paired mode can only poll physical feet. Solo clients use their own stock connections for HMD poses.
