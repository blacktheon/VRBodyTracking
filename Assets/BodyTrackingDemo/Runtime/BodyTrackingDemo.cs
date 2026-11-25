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

        DemoRoom room;
        NetworkManager manager;
        VRNetSession session;
        PoseRelay relay;
        TabletPanel dashboard;
        QuestPanel panel;
        readonly DemoControls controls = new();
        BodyTrackingClient tracking;
        Transform origin, rig, head, leftHand, rightHand;
        readonly SeatAvatar[] remote = new SeatAvatar[3], local = new SeatAvatar[3];
        SolvedSkeleton skeleton;
        DemoChoices choices;
        Quaternion body = Quaternion.identity;
        Vector3 chairInRig;
        float neutralEyeInRig;
        bool needsSeat = true, configured, sitting;
        double nextStatus, calibrationNoticeUntil;
        string network = "Waiting for tablet";

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
            if (tablet)
            {
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                dashboard = new TabletPanel(transform);
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
            var camera = head.GetComponent<Camera>();
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(27, 43, 58, 255);
            }
            tracking = gameObject.AddComponent<BodyTrackingClient>();
            panel = new QuestPanel(transform, Apply);
            choices = DemoChoices.Load();
            if (DemoChoices.Saved) Apply(choices);
            else panel.OpenSettings(choices, head);
        }

        void RecordStatus(string message) => network = message;

        void Apply(DemoChoices next)
        {
            bool changeRoom = !configured || next.Role != choices.Role || next.Tablet != choices.Tablet;
            needsSeat |= !configured || next.Role != choices.Role;
            if (next.Role != choices.Role)
            {
                foreach (var view in local) view?.Show(false);
                skeleton?.Hide();
            }
            choices = next;
            configured = true;
            choices.Save();
            skeleton?.ResetFootReference();
            tracking.Disconnect();
            if (!string.IsNullOrEmpty(choices.Pc))
                tracking.Connect(choices.Pc, choices.Slot, choices.Solo ? BodyTrackingMode.SixTrackers : BodyTrackingMode.PairedFeet, origin, head);

            if (choices.Solo)
            {
                session.StopSession();
                return;
            }
            if (!changeRoom && session.IsClientConnected) return;
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
            if (controls.Confirm)
            {
                sitting = !sitting;
                if (!needsSeat) SetEyeHeight();
            }
            if (controls.Calibrate)
            {
                Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                if (forward.sqrMagnitude > 0.01f) body = Quaternion.LookRotation(forward.normalized);
                RememberChair();
                skeleton?.ResetFootReference();
                tracking.Calibrate();
                calibrationNoticeUntil = Time.unscaledTimeAsDouble + 2;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.06f);
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
            position.y = (sitting ? SeatedMotion.SittingEyeHeight : SeatedMotion.StandingEyeHeight) - neutralEyeInRig;
            rig.position = position;
        }

        void LateUpdate()
        {
            double now = Time.unscaledTimeAsDouble;
            AvatarPose? upload = null;
            if (!tablet && head != null)
            {
                panel.Place(head, body);
                if (choices.Solo)
                {
                    skeleton ??= new SolvedSkeleton(room);
                    skeleton.Draw(tracking, head, body, origin.position.y);
                }
                var pose = PlayerPose();
                int index = choices.Slot - 1;
                local[index] ??= new SeatAvatar(room, choices.Slot, true);
                local[index].Draw(pose);
                if (!needsSeat && !choices.Solo) upload = pose;
            }
            relay.Tick(upload, now);
            for (int i = 0; i < remote.Length; i++)
            {
                if ((tablet || !choices.Solo) && relay.Roster.TryGet(i + 1, out ulong owner, out var pose)
                    && (tablet || owner != manager.LocalClientId))
                {
                    remote[i] ??= new SeatAvatar(room, i + 1, false);
                    remote[i].Draw(pose);
                }
                else remote[i]?.Show(false);
            }
            if (now < nextStatus) return;
            nextStatus = now + 0.15;
            if (tablet) dashboard.Draw(relay.Roster, network, session.Config.serverPort, now);
            else if (head != null) UpdateHeadsetStatus(upload, now);
        }

        bool TryGetLift(bool left, out float lift) => choices.Solo
            ? skeleton.TryGetFootLift(left, out lift) : tracking.TryGetFootLift(left, out lift);

        AvatarPose PlayerPose()
        {
            bool hasLeft = TryGetLift(true, out float left);
            bool hasRight = TryGetLift(false, out float right);
            // Joystick locomotion moves the rig. Leaning or looking down only moves the head inside it.
            Vector3 floor = rig.TransformPoint(chairInRig);
            floor.y = 0;
            return new AvatarPose
            {
                Slot = (byte)choices.Slot, FreshFeet = (byte)((hasLeft ? 1 : 0) | (hasRight ? 2 : 0)),
                Head = new Pose(head.position, head.rotation),
                LeftHand = Hand(leftHand, true), RightHand = Hand(rightHand, false),
                LeftFoot = SeatedMotion.Foot(floor, body, true, hasLeft ? left : 0, sitting),
                RightFoot = SeatedMotion.Foot(floor, body, false, hasRight ? right : 0, sitting)
            };
        }

        Pose Hand(Transform anchor, bool left)
        {
            if (anchor != null && anchor.position.y > origin.position.y + 0.08f)
                return new Pose(anchor.position, anchor.rotation);
            return new Pose(head.position + body * new Vector3(left ? -0.28f : 0.28f, -0.37f, 0.25f), body);
        }

        void UpdateHeadsetStatus(AvatarPose? upload, double now)
        {
            string pc = string.IsNullOrEmpty(choices.Pc) ? "Set the tracking PC address in settings." : tracking.Status;
            string motion = choices.Solo ? "Mirror   " + skeleton.JointCount + " / 7 joints" :
                upload.HasValue ? "L  " + TabletPanel.Lift(upload.Value, true) + "     R  " + TabletPanel.Lift(upload.Value, false) : "Waiting for a floor-level head pose";
            if (now < calibrationNoticeUntil) motion = "Calibrating. Keep both feet flat and still.";
            string roomStatus = "";
            if (!choices.Solo)
            {
                bool occupied = relay.Roster.TryGet(choices.Slot, out ulong id, out _) && id != manager.LocalClientId;
                roomStatus = occupied ? "Seat occupied. Select another seat." :
                    session.IsClientConnected ? "Tablet online  " + relay.Roster.Count + "/3 seats" : network;
            }
            panel.Status(choices.Label + (sitting ? "    Sitting" : "    Standing") +
                "\nA  Sit/stand    X  Calibrate    B  Settings\n" + motion + "\n" + pc + (choices.Solo ? "" : "\n" + roomStatus));
        }

        void OnDestroy()
        {
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
