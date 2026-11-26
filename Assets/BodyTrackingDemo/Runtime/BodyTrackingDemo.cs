using DSTA.VRNet.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo
{
    [DefaultExecutionOrder(150)]
    public sealed class BodyTrackingDemo : MonoBehaviour
    {
        [Tooltip("Start the ordinary Android tablet server. Headsets use the existing floor-level Quest rig.")]
        [SerializeField] bool tablet;
        [SerializeField] StepOverCableMission cableMission;

        DemoRoom room;
        NetworkManager manager;
        VRNetSession session;
        PoseRelay relay;
        ClimbLadder climbWorld;
        LadderClimber climber;
        TabletPanel dashboard;
        QuestPanel panel;
        readonly DemoControls controls = new();
        BodyTrackingClient tracking;
        Transform origin, rig, head, leftHand, rightHand, leftPalm, rightPalm;
        readonly SeatAvatar[] remote = new SeatAvatar[3], local = new SeatAvatar[3];
        SolvedSkeleton skeleton;
        DemoChoices choices;
        Quaternion body = Quaternion.identity;
        Vector3 chairInRig;
        float neutralEyeInRig;
        bool needsSeat = true, configured, sitting;
        bool paused, focused = true;
        double nextStatus, calibrationNoticeUntil;
        string network = "Waiting for tablet";
        string lastTrackingPc;

        void Start()
        {
            room = new DemoRoom(transform);
            var networkObject = new GameObject("Body tracking VRNet");
            networkObject.SetActive(false);
            var transport = networkObject.AddComponent<UnityTransport>();
            manager = networkObject.AddComponent<NetworkManager>();
            // The tablet and Quest have intentionally different scenes. Poses use named messages.
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
            session = networkObject.AddComponent<VRNetSession>();
            networkObject.SetActive(true);
            session.Config.teamId = "BODY-TRACKING";
            session.Config.serverName = "Seated tracking tablet";
            session.Config.appVersion = "1";
            session.OnStatusChanged += RecordStatus;
            relay = new PoseRelay(manager);
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                var ladder = root.GetComponentInChildren<ClimbLadder>();
                if (ladder == null || ladder.RungCount == 0) continue;
                climbWorld = ladder;
                relay.BindWorld(ladder.VehicleRoot, ladder.LadderRoot);
                if (!tablet) climber = new LadderClimber(ladder);
                break;
            }
            if (tablet)
            {
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                dashboard = new TabletPanel(transform);
                dashboard.BindLadder(climbWorld);
                session.StartServer();
                return;
            }

            origin = GameObject.Find("TrackingSpace")?.transform;
            head = origin != null ? origin.Find("CenterEyeAnchor") : null;
            if (head == null || origin.parent == null)
            {
                Debug.LogError("The headset scene needs a floor-level Quest camera rig with TrackingSpace/CenterEyeAnchor.");
                enabled = false;
                return;
            }
            rig = origin.parent;
            leftHand = origin.Find("LeftHandAnchor");
            rightHand = origin.Find("RightHandAnchor");
            leftPalm = new GameObject("Left palm target").transform;
            rightPalm = new GameObject("Right palm target").transform;
            leftPalm.SetParent(origin, false);
            rightPalm.SetParent(origin, false);
            var camera = head.GetComponent<Camera>();
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(27, 43, 58, 255);
            }
            tracking = gameObject.AddComponent<BodyTrackingClient>();
            cableMission?.BeginLocal(head);
            panel = new QuestPanel(transform, Apply, cableMission != null);
            choices = DemoChoices.Load();
            lastTrackingPc = DemoChoices.LastTrackingPc;
            if (DemoChoices.Saved) Apply(choices);
            else panel.OpenSettings(choices, head);
        }

        void RecordStatus(string message) => network = message;

        void Apply(DemoChoices next)
        {
            cableMission?.ResetApproach();
            bool changeRoom = !configured || next.Role != choices.Role || next.Tablet != choices.Tablet;
            bool changeTracking = !configured || next.Role != choices.Role || next.Pc != choices.Pc;
            needsSeat |= !configured || next.Role != choices.Role;
            if (next.Role != choices.Role)
            {
                foreach (var view in local) view?.Show(false);
                skeleton?.Hide();
            }
            choices = next;
            configured = true;
            choices.Save();
            if (changeTracking)
            {
                climber?.ResetLocation(rig);
                skeleton?.ResetFootReference();
                var mode = choices.Solo ? BodyTrackingMode.SixTrackers : BodyTrackingMode.PairedFeet;
                if (string.IsNullOrEmpty(choices.Pc))
                    tracking.ConnectAuto(choices.Slot, mode, origin, head, lastTrackingPc);
                else
                    tracking.Connect(choices.Pc, choices.Slot, mode, origin, head);
            }

            if (!changeRoom && (choices.Solo || session.IsClientConnected)) return;
            if (choices.Solo)
            {
                session.StopSession();
                return;
            }
            session.StopSession();
            session.PlayerName = "Seat " + choices.Slot;
            session.GetComponent<ReconnectController>().Enabled = true; // retry initial discovery and direct-IP failures, too.
            if (string.IsNullOrEmpty(choices.Tablet)) session.StartClientAuto();
            else session.StartClient(choices.Tablet);
        }

        void Update()
        {
            if (tablet || panel == null) return;
            if (needsSeat && head.position.y - origin.position.y > 0.25f)
            {
                body = SeatedMotion.Place(rig, head, choices.Slot);
                neutralEyeInRig = head.position.y - rig.position.y;
                SetEyeHeight();
                RememberChair();
                needsSeat = false;
                if (panel.Open) panel.OpenSettings(choices, head);
            }
            controls.Read();
            if (panel.Open) { panel.Handle(controls); return; }
            if (controls.Back) { panel.OpenSettings(choices, head); return; }
            if (controls.Confirm && climber?.Holding != true)
            {
                sitting = !sitting;
                if (!needsSeat) SetEyeHeight();
            }
            if (controls.Calibrate)
            {
                climber?.Cancel(rig);
                cableMission?.ResetApproach();
                Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                if (forward.sqrMagnitude > 0.01f) body = Quaternion.LookRotation(forward.normalized);
                RememberChair();
                skeleton?.ResetFootReference();
                tracking.Calibrate();
                calibrationNoticeUntil = Time.unscaledTimeAsDouble + 2;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.06f);
            if (climber?.Holding != true)
                SeatedMotion.Move(rig, head, ref body, controls.Move, controls.Turn * 70 * dt, 1.15f * dt);
        }

        void RememberChair()
        {
            chairInRig = rig.InverseTransformPoint(new Vector3(head.position.x, 0, head.position.z));
        }

        void SetEyeHeight()
        {
            // Offset the entire tracked rig once. Physical head and hand movement remains one-to-one.
            Vector3 position = rig.position;
            position.y = (climber?.PlatformHeight ?? 0f) +
                (sitting ? SeatedMotion.SittingEyeHeight : SeatedMotion.StandingEyeHeight) - neutralEyeInRig;
            rig.position = position;
        }

        void LateUpdate()
        {
            double now = Time.unscaledTimeAsDouble;
            AvatarPose? upload = null;
            AvatarPose? localPose = null;
            if (!tablet && head != null)
            {
                if (choices.Solo)
                {
                    skeleton ??= new SolvedSkeleton(room);
                    skeleton.Draw(tracking, head, body, origin.position.y);
                }
                bool playing = configured && !needsSeat && !panel.Open && focused && !paused;
                bool hasLeft = TryGetLift(true, out float left);
                bool hasRight = TryGetLift(false, out float right);
                byte fresh = (byte)((hasLeft ? 1 : 0) | (hasRight ? 2 : 0));
                bool wasOnRoof = climber?.OnRoof == true;
                Transform leftTarget = TrackedHand(true, leftPalm, leftHand);
                Transform rightTarget = TrackedHand(false, rightPalm, rightHand);
                climber?.Tick(rig, head, leftTarget, rightTarget, controls.LeftGrip,
                    controls.RightGrip, fresh, left, right, playing, now);
                if (!wasOnRoof && climber?.OnRoof == true && sitting)
                {
                    sitting = false;
                    SetEyeHeight();
                }
                panel.Place(head, body);
                // Compute joint poses after climbing moves the rig. The tablet receives this same frame.
                Vector3 floor = rig.TransformPoint(chairInRig);
                floor.y = climber?.PlatformHeight ?? 0f;
                var pose = PlayerPose(floor, left, right, fresh, leftTarget, rightTarget);
                climber?.ApplyPose(ref pose, left, right);
                cableMission?.Tick(floor, fresh, left, right, playing && climber?.Holding != true && climber?.OnRoof != true, now,
                    climber?.Feedback ?? LadderResult.None);
                DrawAvatar(local, pose, true);
                if (!needsSeat) localPose = pose;
                if (!needsSeat && !choices.Solo) upload = pose;
            }
            relay.Tick(upload, now);
            for (int i = 0; i < remote.Length; i++)
            {
                if ((tablet || !choices.Solo) && relay.Roster.TryGet(i + 1, out ulong owner, out var pose)
                    && (tablet || owner != manager.LocalClientId))
                {
                    DrawAvatar(remote, pose, false);
                }
                else remote[i]?.Show(false);
            }
            if (now < nextStatus) return;
            nextStatus = now + 0.15;
            if (tablet) dashboard.Draw(relay.Roster, network, session.Config.serverPort, now);
            else if (head != null) UpdateHeadsetStatus(localPose, now);
        }

        void DrawAvatar(SeatAvatar[] views, AvatarPose pose, bool own)
        {
            int index = pose.Slot - 1;
            var view = views[index];
            if (view == null || view.Model != pose.Model)
            {
                view?.Remove();
                views[index] = view = new SeatAvatar(room, pose.Slot, pose.Model, own);
            }
            view.Draw(pose);
        }

        bool TryGetLift(bool left, out float lift) => choices.Solo
            ? skeleton.TryGetFootLift(left, out lift) : tracking.TryGetFootLift(left, out lift);

        Transform TrackedHand(bool left, Transform palm, Transform anchor)
        {
            if (DemoControls.TryReadPalm(left, out var grip))
            {
                palm.SetLocalPositionAndRotation(grip.position, grip.rotation);
                return palm;
            }
            return anchor != null && anchor.position.y > origin.position.y + 0.08f ? anchor : null;
        }

        AvatarPose PlayerPose(Vector3 floor, float left, float right, byte fresh, Transform leftTarget, Transform rightTarget)
        {
            return new AvatarPose
            {
                Slot = (byte)choices.Slot, Model = choices.Model, FreshFeet = fresh,
                Head = new Pose(head.position, head.rotation),
                LeftHand = Hand(leftTarget, true), RightHand = Hand(rightTarget, false),
                LeftFoot = SeatedMotion.Foot(floor, body, true, left, sitting),
                RightFoot = SeatedMotion.Foot(floor, body, false, right, sitting)
            };
        }

        Pose Hand(Transform anchor, bool left)
        {
            if (anchor != null)
                return new Pose(anchor.position, anchor.rotation);
            return new Pose(head.position + body * new Vector3(left ? -0.28f : 0.28f, -0.37f, 0.25f), body);
        }

        void UpdateHeadsetStatus(AvatarPose? pose, double now)
        {
            string host = tracking.ConnectedHost;
            if (!string.IsNullOrEmpty(host) && host != lastTrackingPc)
                DemoChoices.LastTrackingPc = lastTrackingPc = host;
            string pc = string.IsNullOrEmpty(host) ? tracking.Status : "SlimeVR  " + host + "\n" + tracking.Status;
            string motion = choices.Solo && (!pose.HasValue || !pose.Value.Climb.Active) ? "Mirror   " + skeleton.JointCount + " / 7 joints" :
                pose.HasValue ? TabletPanel.Motion(pose.Value) : "Waiting for a floor-level head pose";
            if (now < calibrationNoticeUntil) motion = "Calibrating. Keep both feet flat and still.";
            string roomStatus = "";
            if (!choices.Solo)
            {
                bool occupied = relay.Roster.TryGet(choices.Slot, out ulong id, out _) && id != manager.LocalClientId;
                roomStatus = occupied ? "Seat occupied. Select another seat." :
                    session.IsClientConnected ? "Tablet online  " + relay.Roster.Count + "/3 seats" : network;
            }
            string cue = cableMission != null ? cableMission.Cue : "";
            if (climber?.Feedback == LadderResult.Fell) cue = "Third step of the same kind. Back at the base.";
            else if (climber?.OnRoof == true) cue = "On the roof. Use the stick to move.";
            else if (climber?.Holding == true) cue = "Hold one grip. Reach or lift and lower the lower shoe.";
            else if (climber != null && climber.NearbyRung > 0) cue = "Squeeze a side grip to hold rung " + climber.NearbyRung + ".";
            panel.Status(choices.Label + (sitting ? "    Sitting" : "    Standing") +
                "\nA  Sit/stand    X  Calibrate    B  Settings\n" + motion + "\n" + pc + (choices.Solo ? "" : "\n" + roomStatus) +
                (cue.Length > 0 ? "\n" + cue : ""));
        }

        void OnApplicationPause(bool value)
        {
            paused = value;
            if (paused) { cableMission?.ResetApproach(); climber?.Cancel(rig); }
        }

        void OnApplicationFocus(bool value)
        {
            focused = value;
            if (!focused) { cableMission?.ResetApproach(); climber?.Cancel(rig); }
        }

        void OnDestroy()
        {
            if (leftPalm) Destroy(leftPalm.gameObject);
            if (rightPalm) Destroy(rightPalm.gameObject);
            relay?.Dispose();
            if (session != null)
            {
                session.OnStatusChanged -= RecordStatus;
                var networkObject = session.gameObject;
                bool running = manager != null && (manager.IsListening || manager.ShutdownInProgress);
                if (running)
                {
                    // NGO releases transport jobs at the end of its update, after StopSession returns.
                    manager.OnServerStopped += _ => { if (networkObject != null) Destroy(networkObject); };
                    manager.OnClientStopped += _ => { if (networkObject != null) Destroy(networkObject); };
                }
                session.StopSession();
                if (!running && networkObject != null) Destroy(networkObject);
            }
            room?.Dispose();
        }
    }
}
