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
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            var server = CreateEndpoint(null);
            session = server.gameObject.AddComponent<VRNetSession>();
            session.Config.serverPort = port;
            session.Config.discoveryPort = port == ushort.MaxValue ? port - 1 : port + 1;
            session.StartServer();
            Join(1, 0, SoldierModel.Armored);
            var legacy = CreateEndpoint(null);
            Connect(legacy, 2);
            yield return Until(() => legacy.IsConnectedClient && relays[0].Roster.Count == 1, "both versions connected");
            const string snapshot = "Blacktheon.Seated.Room.1";
            int bytesReceived = 0;
            legacy.CustomMessagingManager.UnregisterNamedMessageHandler(snapshot);
            legacy.CustomMessagingManager.RegisterNamedMessageHandler(snapshot, (_, reader) =>
            {
                int size = reader.Length - reader.Position;
                reader.ReadValueSafe(out byte count);
                if (count == 2) bytesReceived = size;
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

            Join(1, 0.16f, SoldierModel.Armored);
            Join(2, 0.08f, SoldierModel.Uniform);
            Join(3, 0.00f, SoldierModel.Uniform);
            yield return Until(() => Displayed(1, SoldierModel.Armored) && Displayed(2, SoldierModel.Uniform)
                && Displayed(3, SoldierModel.Uniform), "tablet shows full third-person soldiers and labels all three choices");
            var secondBody = VisibleBody(2, SoldierModel.Uniform);
            var oldFirstBody = VisibleBody(1, SoldierModel.Armored);
            foreach (var sample in samples)
                foreach (var point in new[] { sample.Value.Head.position, sample.Value.LeftFoot.position, sample.Value.RightFoot.position })
                {
                    var viewport = camera.WorldToViewportPoint(point);
                    Assert.That(viewport.z, Is.GreaterThan(0));
                    Assert.That(viewport.x, Is.InRange(0f, 1f));
                    Assert.That(viewport.y, Is.InRange(0f, 1f));
                }

            samples[0] = PlayerPose(1, true, 0.18f, 0);
            yield return Until(() => Displayed(1, SoldierModel.Uniform), "the tablet swaps the model on the same seat");
            Assert.That(oldFirstBody == null || !oldFirstBody.gameObject.activeInHierarchy, Is.True);
            Assert.That(VisibleBody(2, SoldierModel.Uniform), Is.SameAs(secondBody));
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
