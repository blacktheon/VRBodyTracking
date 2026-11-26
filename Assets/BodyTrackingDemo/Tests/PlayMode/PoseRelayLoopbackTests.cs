using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using DSTA.VRNet.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class PoseRelayLoopbackTests
    {
        readonly List<NetworkManager> managers = new();
        readonly List<PoseRelay> relays = new();
        readonly List<AvatarPose?> samples = new();
        ushort port;
        VRNetSession session;
        Scene tabletScene;

        [Test]
        public void HighLadderPoseFitsRoomButNonFiniteAndOutOfRoomJointsDoNot()
        {
            var pose = new AvatarPose
            {
                Slot = 1, Head = Pose.identity, LeftHand = Pose.identity, RightHand = Pose.identity,
                LeftFoot = Pose.identity, RightFoot = Pose.identity
            };
            pose.Head.position.y = 5.1f;
            Assert.That(pose.IsValid, Is.True, "a seated player at the top rung");
            pose.Head.position.y = 20;
            Assert.That(pose.IsValid, Is.False);
            pose.Head.position.y = float.PositiveInfinity;
            Assert.That(pose.IsValid, Is.False);
            pose.Head.position.y = 5.1f;
            pose.LeftHand.rotation = new Quaternion(0, float.NaN, 0, 1);
            Assert.That(pose.IsValid, Is.False);
        }

        [Test]
        public void InvalidWorldTransformsAndUnmappedRungsAreRejected()
        {
            bool World(Pose pose, Vector3 scale) => new PoseRelay.WorldTransform(pose, scale).IsValid;
            Assert.That(World(Pose.identity, Vector3.one), Is.True);
            Assert.That(World(new Pose(new Vector3(float.NaN, 0, 0), Quaternion.identity), Vector3.one), Is.False);
            Assert.That(World(new Pose(Vector3.zero, new Quaternion(0, 0, float.PositiveInfinity, 0)), Vector3.one), Is.False);
            Assert.That(World(Pose.identity, new Vector3(1, float.NaN, 1)), Is.False);
            Assert.That(World(Pose.identity, new Vector3(1, 1, float.PositiveInfinity)), Is.False);
            Assert.That(World(Pose.identity, Vector3.zero), Is.False);
            var unit = new PoseRelay.WorldTransform(Pose.identity, Vector3.one);
            var sameRotation = new Pose(Vector3.zero, new Quaternion(0, 0, 0, -1));
            Assert.That(unit.Matches(new PoseRelay.WorldTransform(sameRotation, Vector3.one)), Is.True,
                "equivalent quaternion signs should leave a stationary world unchanged");
            Assert.That(new ClimbSnapshot { LeftHand = 9, RightHand = 8, LeftFoot = 7, RightFoot = 6 }.IsValid(9), Is.True);
            Assert.That(new ClimbSnapshot { RightFoot = 10 }.IsValid(9), Is.False);
            Assert.That(new ClimbSnapshot { Feedback = (ClimbFeedback)250 }.IsValid(9), Is.False);
        }

        [Test]
        public void DashboardKeepsRoofAndRungElevationsSeparateFromTrackedShoeLift()
        {
            var pose = new AvatarPose { FreshFeet = 3, LeftFoot = new Pose(new Vector3(0, 3.255f, 0), Quaternion.identity) };
            pose.Climb = new ClimbSnapshot { OnRoof = true };
            Assert.That(TabletPanel.Motion(pose), Does.Contain("On roof").And.Not.Contain("cm"));
            pose.Climb = new ClimbSnapshot { LeftHand = 9, RightHand = 8, LeftFoot = 6, RightFoot = 5, Feedback = ClimbFeedback.Legal };
            Assert.That(TabletPanel.Motion(pose), Does.Contain("9").And.Contain("8").And.Contain("6").And.Contain("5").And.Contain("Legal"));
            Assert.That(TabletPanel.Motion(pose), Does.Not.Contain("cm"));
            pose.Climb = default;
            Assert.That(TabletPanel.Lift(pose, true), Does.Not.Contain("cm"), "the grip packet can arrive after the high pose");
            pose.LeftFoot.position.y = 0.235f;
            Assert.That(TabletPanel.Lift(pose, true), Is.EqualTo("18 cm"));
        }

        [UnityTest]
        public IEnumerator SimultaneousClimbersKeepTheirOwnRungsAndAFallOnlyResetsItsOwner()
        {
            StartTablet();
            var first = Join(1, 0);
            Join(2, 0);
            var high = PlayerPose(1, true, 0, 0);
            high.Head.position.y = 4.95f;
            high.LeftHand.position.y = 3.20f;
            high.RightHand.position.y = 2.88f;
            high.LeftFoot.position.y = 2.16f;
            high.RightFoot.position.y = 1.81f;
            high.Climb = new ClimbSnapshot { LeftHand = 9, RightHand = 8, LeftFoot = 6, RightFoot = 5, Feedback = ClimbFeedback.Legal };
            var low = PlayerPose(2, true, 0, 0);
            low.Climb = new ClimbSnapshot { RightHand = 5, LeftFoot = 3, RightFoot = 2 };
            samples[1] = high;
            samples[2] = low;

            bool Both(PoseRelay relay) =>
                relay.Roster.TryGet(1, out ulong owner, out var a) && owner == first.LocalClientId &&
                a.Climb.LeftHand == 9 && a.Climb.RightHand == 8 && a.Climb.LeftFoot == 6 && a.Climb.RightFoot == 5 &&
                a.Climb.Feedback == ClimbFeedback.Legal && Mathf.Abs(a.Head.position.y - 4.95f) < 0.001f &&
                relay.Roster.TryGet(2, out _, out var b) && b.Climb.RightHand == 5 && b.Climb.LeftFoot == 3;
            yield return Until(() => relays.TrueForAll(Both), "the tablet and both players receive different four-limb states");
            Join(3, 0);
            yield return Until(() => relays.TrueForAll(Both), "the late observer receives both climbers at their current heights");
            // A headset changing its selected seat while connected cannot modify the other climber.
            var wrongSeat = high;
            wrongSeat.Slot = 2;
            wrongSeat.Climb = new ClimbSnapshot { Feedback = ClimbFeedback.Fell };
            samples[1] = wrongSeat;
            low.RightHand.position.y = 2.10f;
            samples[2] = low;
            yield return Until(() => relays.TrueForAll(r => r.Roster.TryGet(2, out _, out var p) &&
                Mathf.Abs(p.RightHand.position.y - 2.10f) < 0.001f), "the other owner's next pose reached each peer");
            Assert.That(relays.TrueForAll(Both), Is.True, "only a seat's owner updates its four contact points");

            var fallen = PlayerPose(1, true, 0, 0);
            fallen.Climb = new ClimbSnapshot { Feedback = ClimbFeedback.Fell };
            samples[1] = fallen;
            yield return Until(() => relays.TrueForAll(r => r.Roster.TryGet(1, out _, out var a) &&
                a.Climb.Feedback == ClimbFeedback.Fell && a.Climb.LeftHand == 0 && a.Climb.RightHand == 0 &&
                a.Climb.LeftFoot == 0 && a.Climb.RightFoot == 0 && a.Head.position.y < 2 &&
                r.Roster.TryGet(2, out _, out var b) && b.Climb.RightHand == 5 && b.Climb.LeftFoot == 3),
                "one player drops to the floor while the second player stays attached");
            Assert.That(TabletPanel.Motion(fallen), Does.Contain("Fell"));
            fallen.Climb = default;
            samples[1] = fallen;
            yield return Until(() => relays.TrueForAll(r => r.Roster.TryGet(1, out _, out var p) &&
                p.Climb.Feedback == ClimbFeedback.None), "clearing feedback restores ordinary tracking on every display");
            Assert.That(TabletPanel.Motion(fallen), Does.Contain("0 cm"));

            managers[2].Shutdown();
            yield return Until(() => !relays[3].Roster.TryGet(2, out _, out _), "a disconnected climber releases the seat");
            var returning = Join(2, 0.1f);
            yield return Until(() => relays[3].Roster.TryGet(2, out ulong owner, out var p) &&
                owner == returning.LocalClientId && p.Climb.RightHand == 0 && p.Climb.LeftFoot == 0 &&
                p.Climb.Feedback == ClimbFeedback.None, "a new connection starts with no inherited contact or outcome");

            var waiting = PlayerPose(2, true, 0, 0);
            waiting.Climb = new ClimbSnapshot { LeftHand = 4, RightFoot = 2 };
            samples[1] = waiting;
            var current = samples[4].Value;
            current.RightHand.position.y = 2.25f;
            samples[4] = current;
            yield return Until(() => relays[3].Roster.TryGet(2, out ulong owner, out var p) && owner == returning.LocalClientId &&
                p.RightHand.position.y == 2.25f && p.Climb.LeftHand == 0, "the occupied slot keeps its existing owner");
            returning.Shutdown();
            yield return Until(() => relays[3].Roster.TryGet(2, out ulong owner, out var p) && owner == first.LocalClientId &&
                p.Climb.LeftHand == 4 && p.Climb.RightFoot == 2, "the waiting headset gets its own held grips when it acquires the seat");
        }

        [UnityTest]
        public IEnumerator RoofStateSurvivesFeedbackExpiryAndReachesLateJoiners()
        {
            StartTablet();
            Join(1, 0);
            var roof = PlayerPose(1, true, 0, 0);
            roof.Head.position.y = 4.45f;
            roof.LeftFoot.position.y = roof.RightFoot.position.y = 3.30f;
            roof.Climb = new ClimbSnapshot { OnRoof = true, Feedback = ClimbFeedback.Legal };
            samples[1] = roof;
            yield return Until(() => relays.TrueForAll(r => r.Roster.TryGet(1, out _, out var p) &&
                p.Climb.OnRoof && p.Climb.Feedback == ClimbFeedback.Legal), "the owner has dismounted onto the vehicle");
            roof.Climb.Feedback = ClimbFeedback.None;
            samples[1] = roof;
            Join(2, 0);
            yield return Until(() => relays.TrueForAll(r => r.Roster.TryGet(1, out _, out var p) &&
                p.Climb.OnRoof && p.Climb.Feedback == ClimbFeedback.None && p.LeftFoot.position.y > 3 &&
                TabletPanel.Motion(p).Contains("On roof") && !TabletPanel.Motion(p).Contains("cm")),
                "the late player sees the roof stance after the move feedback expires");
            samples[1] = PlayerPose(1, true, 0.18f, 0);
            yield return Until(() => relays.TrueForAll(r => r.Roster.TryGet(1, out _, out var p) &&
                !p.Climb.OnRoof && TabletPanel.Lift(p, true) == "18 cm"), "returning to the floor clears the roof stance");
        }

        [UnityTest]
        public IEnumerator TabletWorldTransformsReachConnectedAndLateBoundClients()
        {
            var server = StartTablet();
            (Transform vehicle, Transform ladder) World(NetworkManager endpoint)
            {
                var vehicle = new GameObject("Vehicle").transform;
                vehicle.SetParent(endpoint.transform, false);
                var ladder = new GameObject("Ladder").transform;
                ladder.SetParent(vehicle, false);
                return (vehicle, ladder);
            }
            var authored = World(server);
            authored.vehicle.SetPositionAndRotation(new Vector3(-3, 0.15f, 1), Quaternion.Euler(0, 37, 0));
            authored.vehicle.localScale = new Vector3(1.1f, 1.1f, 1.1f);
            authored.ladder.localPosition = new Vector3(0.8f, 0.20f, -0.1f);
            authored.ladder.localRotation = Quaternion.Euler(-11, 25, 5);
            authored.ladder.localScale = new Vector3(0.9f, 1.2f, 0.85f);
            relays[0].BindWorld(authored.vehicle, authored.ladder);
            bool Matches(Transform a, Transform b) => (a.position - b.position).sqrMagnitude < 0.0001f &&
                Quaternion.Angle(a.rotation, b.rotation) < 0.05f && (a.localScale - b.localScale).sqrMagnitude < 0.0001f;
            var first = World(Join(1, 0));
            relays[1].BindWorld(first.vehicle, first.ladder);
            yield return Until(() => Matches(first.vehicle, authored.vehicle) && Matches(first.ladder, authored.ladder),
                "the first client receives both authored transforms");
            first.vehicle.position = new Vector3(3, 2, 2);
            yield return Until(() => Matches(first.vehicle, authored.vehicle) && Matches(first.ladder, authored.ladder),
                "the client keeps the tablet's authoring when its local vehicle is moved");
            authored.vehicle.position += Vector3.left * 0.6f;
            authored.ladder.localRotation = Quaternion.Euler(14, 73, 0);
            yield return Until(() => Matches(first.vehicle, authored.vehicle) && Matches(first.ladder, authored.ladder),
                "the server publishes edits to the vehicle and its ladder");
            var late = World(Join(2, 0));
            yield return Until(() => relays[2].Roster.Count == 2, "the late client is receiving the room");
            relays[2].BindWorld(late.vehicle, late.ladder);
            yield return Until(() => Matches(late.vehicle, authored.vehicle) && Matches(late.ladder, authored.ladder),
                "a late scene binding applies the current transform for each object");
            var lastGood = authored.vehicle.position;
            authored.vehicle.position = new Vector3(40, 0, 1);
            var third = World(Join(3, 0));
            relays[3].BindWorld(third.vehicle, third.ladder);
            yield return Until(() => (third.vehicle.position - lastGood).sqrMagnitude < 0.0001f &&
                Matches(third.ladder, first.ladder), "a new observer receives the last valid world during an out-of-room edit");
        }

        NetworkManager StartTablet()
        {
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            NetworkManager tablet = CreateEndpoint(null);
            session = tablet.gameObject.AddComponent<VRNetSession>();
            session.Config.serverPort = port;
            session.Config.discoveryPort = port == ushort.MaxValue ? port - 1 : port + 1;
            session.StartServer();
            Assert.That(tablet.IsServer, Is.True);
            return tablet;
        }

        [UnityTest]
        public IEnumerator ThreeSeatsRelayIndependentFeetToLateJoinersAndReleaseOnDisconnect()
        {
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            NetworkManager tablet = CreateEndpoint(null);
            session = tablet.gameObject.AddComponent<VRNetSession>();
            session.Config.serverPort = port;
            session.Config.discoveryPort = port == ushort.MaxValue ? port - 1 : port + 1;
            session.Config.teamId = "Seated loopback";
            session.StartServer();
            Assert.That(tablet.IsServer, Is.True);

            NetworkManager first = Join(1, 0.21f, SoldierModel.Armored);
            yield return Until(() => relays[0].Roster.TryGet(1, out _, out var p) && p.Model == SoldierModel.Armored,
                "tablet receives seat one's selected armor");
            NetworkManager second = Join(2, 0.32f);
            NetworkManager third = Join(3, 0.43f);
            yield return Until(() => relays[1].Roster.Count == 3 && relays[2].Roster.Count == 3
                && relays[3].Roster.Count == 3, "all three headsets receive the complete room");
            yield return Until(() => relays.TrueForAll(r => r.Roster.TryGet(1, out _, out var p) && p.Model == SoldierModel.Armored &&
                r.Roster.TryGet(2, out _, out var secondSeat) && secondSeat.Model == SoldierModel.Uniform),
                "both late joiners see the choices already in the room");
            AssertPeer(3, 1, first.LocalClientId, 0.21f, SoldierModel.Armored);
            AssertPeer(1, 2, second.LocalClientId, 0.32f);
            AssertPeer(2, 3, third.LocalClientId, 0.43f);

            samples[1] = PlayerPose(1, true, 0.18f, 0);
            yield return Until(() => relays.TrueForAll(endpoint =>
                endpoint.Roster.TryGet(1, out _, out AvatarPose pose) &&
                pose.Model == SoldierModel.Uniform &&
                Mathf.Abs(pose.Head.position.y - SeatedMotion.SittingEyeHeight) < 0.001f &&
                TabletPanel.Lift(pose, true) == "18 cm" && TabletPanel.Lift(pose, false) == "0 cm"),
                "tablet and all headsets see the new seated pose and only the raised left shoe");
            AssertPeer(1, 2, second.LocalClientId, 0.32f);

            samples[1] = PlayerPose(1, true, 0, 0);
            yield return Until(() => relays.TrueForAll(endpoint =>
                endpoint.Roster.TryGet(1, out _, out AvatarPose pose) && pose.FreshFeet == 3 &&
                Mathf.Abs(pose.LeftFoot.position.y - SeatedMotion.ShoeCenterHeight) < 0.001f &&
                Mathf.Abs(pose.RightFoot.position.y - SeatedMotion.ShoeCenterHeight) < 0.001f),
                "the lowered boot reaches the floor on the tablet and all three headsets");
            AssertPeer(1, 2, second.LocalClientId, 0.32f);

            ulong oldId = second.LocalClientId;
            second.Shutdown();
            yield return Until(() => !relays[1].Roster.TryGet(2, out _, out _), "other headsets remove a disconnected seat");
            NetworkManager returning = Join(2, 0.12f, SoldierModel.Armored);
            yield return Until(() => relays[3].Roster.TryGet(2, out ulong owner, out var pose)
                && owner == returning.LocalClientId && pose.Model == SoldierModel.Armored,
                "returning headset holds its original slot under its new connection");
            Assert.That(returning.LocalClientId, Is.Not.EqualTo(oldId));
            AssertPeer(3, 2, returning.LocalClientId, 0.12f, SoldierModel.Armored);
        }

        [UnityTest]
        public IEnumerator OlderHeadsetCanShareTheRoomWithoutAnnouncingAModel()
        {
            StartTablet();
            Join(1, 0, SoldierModel.Armored);
            var legacy = CreateEndpoint(null);
            // This peer speaks only the original two pose messages.
            relays[2].Dispose();
            Connect(legacy, 2);
            yield return Until(() => legacy.IsConnectedClient && relays[0].Roster.Count == 1, "both versions connected");
            const string snapshot = "Blacktheon.Seated.Room.1";
            int bytesReceived = 0;
            ulong soleOwner = 0;
            legacy.CustomMessagingManager.RegisterNamedMessageHandler(snapshot, (_, reader) =>
            {
                int size = reader.Length - reader.Position;
                reader.ReadValueSafe(out byte count);
                if (count == 2) bytesReceived = size;
                if (count == 1) reader.ReadValueSafe(out soleOwner);
            });
            using (var writer = new FastBufferWriter(142, Unity.Collections.Allocator.Temp))
            {
                writer.WriteValueSafe((byte)2);
                writer.WriteValueSafe((byte)3);
                var sample = PlayerPose(2, false, 0, 0.07f);
                foreach (var joint in new[] { sample.Head, sample.LeftHand, sample.RightHand, sample.LeftFoot, sample.RightFoot })
                {
                    writer.WriteValueSafe(joint.position);
                    writer.WriteValueSafe(joint.rotation);
                }
                legacy.CustomMessagingManager.SendNamedMessage("Blacktheon.Seated.Pose.1", NetworkManager.ServerClientId, writer);
            }
            yield return Until(() => bytesReceived != 0 && relays[1].Roster.TryGet(2, out _, out var pose)
                && pose.Model == SoldierModel.Armored, "a new headset sees the original seat-two outfit for a legacy player");
            Assert.That(bytesReceived, Is.EqualTo(301), "legacy snapshot has two 150-byte entries plus its count");
            var high = samples[1].Value;
            high.Head.position.y = 4.90f;
            high.Climb = new ClimbSnapshot { LeftHand = 9 };
            samples[1] = high;
            yield return Until(() => soleOwner == legacy.LocalClientId &&
                relays[1].Roster.TryGet(1, out _, out var pose) && pose.Head.position.y > 4.8f,
                "the old headset still sees its ground-level seat while an updated headset sees the high climber");
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator TabletSceneDisplaysAndSwitchesTheSoldierChosenByEachConnectedHeadset()
        {
            const string scenePath = "Assets/BodyTrackingDemo/Scenes/Tablet.unity";
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            tabletScene = SceneManager.GetSceneByPath(scenePath);
            yield return null;
            session = VRNetSession.Instance;
            Assert.That(session != null && session.IsServerRunning, Is.True);
            port = (ushort)session.Config.serverPort;
            var host = Array.Find(tabletScene.GetRootGameObjects(), go => go.GetComponent<BodyTrackingDemo>() != null).transform;
            var room = host.Find("Seated tracking room");
            var cards = host.Find("Tablet dashboard/Status panel/Seat status");
            var camera = host.Find("Tablet overview").GetComponent<Camera>();
            bool installed = Resources.Load<GameObject>("BodyTrackingCharacters/SoldierUniform") != null;

            Transform VisibleBody(int slot, SoldierModel model)
            {
                foreach (Transform view in room)
                {
                    if (!view.gameObject.activeInHierarchy || view.name != "Seat " + slot) continue;
                    var body = view.Find(installed ? "Soldier" + model + "(Clone)/Face" : "Torso");
                    if (body != null && body.gameObject.activeInHierarchy && body.GetComponent<Renderer>().enabled) return body;
                }
                return null;
            }
            bool Displayed(int slot, SoldierModel model) => VisibleBody(slot, model) != null &&
                cards.Find("Seat " + slot + "/Seat").GetComponent<Text>().text.Contains(model.ToString());
            void AssertInView(Vector3 point)
            {
                var viewport = camera.WorldToViewportPoint(point);
                Assert.That(viewport.z, Is.GreaterThan(0));
                Assert.That(viewport.x, Is.InRange(0f, 1f));
                Assert.That(viewport.y, Is.InRange(0f, 1f));
            }

            Join(1, 0.16f, SoldierModel.Armored);
            Join(2, 0.08f, SoldierModel.Uniform);
            Join(3, 0.00f, SoldierModel.Uniform);
            yield return Until(() => Displayed(1, SoldierModel.Armored) && Displayed(2, SoldierModel.Uniform)
                && Displayed(3, SoldierModel.Uniform), "tablet shows full third-person soldiers and labels all three choices");
            var secondBody = VisibleBody(2, SoldierModel.Uniform);
            var oldFirstBody = VisibleBody(1, SoldierModel.Armored);
            foreach (var sample in samples)
                foreach (var point in new[] { sample.Value.Head.position, sample.Value.LeftFoot.position, sample.Value.RightFoot.position })
                    AssertInView(point);

            samples[0] = PlayerPose(1, true, 0.18f, 0);
            yield return Until(() => Displayed(1, SoldierModel.Uniform), "the tablet swaps the model on the same seat");
            Assert.That(oldFirstBody == null || !oldFirstBody.gameObject.activeInHierarchy, Is.True);
            Assert.That(VisibleBody(2, SoldierModel.Uniform), Is.SameAs(secondBody));
            managers[1].Shutdown();
            managers[2].Shutdown();
            var layoutRoot = Array.Find(tabletScene.GetRootGameObjects(), go => go.GetComponent<ClimbLadder>() != null);
            Assert.That(layoutRoot, Is.Not.Null);
            var ladder = layoutRoot.GetComponent<ClimbLadder>();
            var roof = PlayerPose(1, true, 0, 0);
            Vector3 delta = ladder.RungPoint(ladder.RungCount, true) - roof.LeftFoot.position + ladder.FaceDirection * 0.35f;
            roof.Head.position += delta;
            roof.LeftHand.position += delta;
            roof.RightHand.position += delta;
            roof.LeftFoot.position += delta;
            roof.RightFoot.position += delta;
            roof.Climb = new ClimbSnapshot { OnRoof = true };
            samples[0] = roof;
            var feet = cards.Find("Seat 1/Feet").GetComponent<Text>();
            yield return Until(() => feet.text.Contains("On roof") && VisibleBody(2, SoldierModel.Uniform) == null &&
                VisibleBody(3, SoldierModel.Uniform) == null, "the tablet identifies a single remote player on the vehicle");
            AssertInView(roof.Head.position);
            AssertInView(ladder.RungPoint(1, true));
            AssertInView(ladder.RungPoint(ladder.RungCount, false));
            AssertInView(ladder.TopBar.position);
            AssertInView(ladder.VehicleRoot.position);
            var unmapped = roof;
            unmapped.Climb = new ClimbSnapshot { RightHand = (byte)(ladder.RungCount + 1) };
            unmapped.Head.position.y += 0.1f;
            samples[0] = unmapped;
            yield return Until(() => relays[0].Roster.TryGet(1, out _, out var p) &&
                Mathf.Abs(p.Head.position.y - unmapped.Head.position.y) < 0.001f, "the next head pose reaches the room");
            Assert.That(relays[0].Roster.TryGet(1, out _, out var kept) && kept.Climb.OnRoof && kept.Climb.RightHand == 0,
                Is.True, "the tablet rejects a rung beyond its own mapped ladder and preserves the last valid support");
            managers[0].Shutdown();
            yield return Until(() => VisibleBody(1, SoldierModel.Uniform) == null, "the disconnected soldier disappears");
        }
#endif

        static AvatarPose PlayerPose(byte slot, bool sitting, float leftLift, float rightLift)
        {
            var floor = new Vector3((slot - 2) * 1.55f, 0, slot == 2 ? 0.55f : -0.4f);
            var heading = Quaternion.Euler(0, 15, 0);
            var head = floor + Vector3.up * (sitting ? SeatedMotion.SittingEyeHeight : SeatedMotion.StandingEyeHeight);
            return new AvatarPose
            {
                Slot = slot, FreshFeet = 3,
                Head = new Pose(head, heading),
                LeftHand = new Pose(head + heading * new Vector3(-0.31f, -0.41f, 0.23f), heading),
                RightHand = new Pose(head + heading * new Vector3(0.31f, -0.41f, 0.23f), heading),
                LeftFoot = SeatedMotion.Foot(floor, heading, true, leftLift, sitting),
                RightFoot = SeatedMotion.Foot(floor, heading, false, rightLift, sitting)
            };
        }

        NetworkManager Join(byte slot, float lift, SoldierModel model = SoldierModel.Uniform)
        {
            var sample = PlayerPose(slot, false, 0, lift);
            sample.Model = model;
            NetworkManager manager = CreateEndpoint(sample);
            Connect(manager, slot);
            return manager;
        }

        void Connect(NetworkManager manager, byte slot)
        {
            manager.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                playerGuid = new Guid(slot, 0, 0, new byte[8]).ToString(), playerName = "Seat " + slot,
                teamId = session.Config.teamId, appVersion = session.Config.appVersion
            }.ToBytes();
            Assert.That(manager.StartClient(), Is.True);
        }

        NetworkManager CreateEndpoint(AvatarPose? sample)
        {
            var go = new GameObject(sample.HasValue ? "Headset " + sample.Value.Slot : "Tablet");
            var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
            var manager = go.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport, EnableSceneManagement = false, ConnectionApproval = true
            };
            managers.Add(manager);
            relays.Add(new PoseRelay(manager));
            samples.Add(sample);
            return manager;
        }

        IEnumerator Until(Func<bool> condition, string behavior)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 7;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                for (int i = 0; i < relays.Count; i++)
                    relays[i].Tick(samples[i], Time.unscaledTimeAsDouble);
                yield return null;
            }
            Assert.That(condition(), Is.True, behavior);
        }

        void AssertPeer(int endpoint, int slot, ulong id, float lift, SoldierModel model = SoldierModel.Uniform)
        {
            Assert.That(relays[endpoint].Roster.TryGet(slot, out ulong owner, out AvatarPose pose), Is.True);
            Assert.That(owner, Is.EqualTo(id));
            Assert.That(pose.Model, Is.EqualTo(model));
            Assert.That(pose.Head.position.y, Is.EqualTo(SeatedMotion.StandingEyeHeight).Within(0.001f));
            Assert.That(pose.RightFoot.position.y, Is.EqualTo(SeatedMotion.ShoeCenterHeight + lift).Within(0.001f));
            Assert.That(pose.LeftFoot.position.y, Is.EqualTo(SeatedMotion.ShoeCenterHeight).Within(0.001f));
            Assert.That(pose.FreshFeet, Is.EqualTo(3));
        }

        [UnityTearDown]
        public IEnumerator Shutdown()
        {
            foreach (var relay in relays) relay.Dispose();
            for (int i = managers.Count - 1; i > 0; i--)
                if (managers[i].IsListening) managers[i].Shutdown();
            double deadline = Time.realtimeSinceStartupAsDouble + 8;
            while (managers.Exists(m => !m.IsServer && (m.IsListening || m.ShutdownInProgress)) && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            if (session != null) session.StopSession();
            while (managers.Exists(m => m.IsListening || m.ShutdownInProgress) && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            for (int i = managers.Count - 1; i >= 0; i--) Object.Destroy(managers[i].gameObject);
            if (tabletScene.IsValid() && tabletScene.isLoaded) yield return SceneManager.UnloadSceneAsync(tabletScene);
            yield return null;
            relays.Clear();
            managers.Clear();
            samples.Clear();
        }
    }
}
