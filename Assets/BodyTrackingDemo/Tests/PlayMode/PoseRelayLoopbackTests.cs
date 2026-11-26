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
using UnityEngine.TestTools;
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

            NetworkManager first = Join(1, 0.21f);
            yield return Until(() => relays[0].Roster.TryGet(1, out _, out _), "tablet receives seat one");
            NetworkManager second = Join(2, 0.32f);
            NetworkManager third = Join(3, 0.43f);
            yield return Until(() => relays[1].Roster.Count == 3 && relays[2].Roster.Count == 3
                && relays[3].Roster.Count == 3, "all three headsets receive the complete room");
            AssertPeer(3, 1, first.LocalClientId, 0.21f);
            AssertPeer(1, 2, second.LocalClientId, 0.32f);
            AssertPeer(2, 3, third.LocalClientId, 0.43f);

            samples[1] = PlayerPose(1, true, 0.18f, 0);
            yield return Until(() => relays.TrueForAll(endpoint =>
                endpoint.Roster.TryGet(1, out _, out AvatarPose pose) &&
                Mathf.Abs(pose.Head.position.y - SeatedMotion.SittingEyeHeight) < 0.001f &&
                TabletPanel.Lift(pose, true) == "18 cm" && TabletPanel.Lift(pose, false) == "0 cm"),
                "tablet and all headsets see the new seated pose and only the raised left shoe");
            AssertPeer(1, 2, second.LocalClientId, 0.32f);

            ulong oldId = second.LocalClientId;
            second.Shutdown();
            yield return Until(() => !relays[1].Roster.TryGet(2, out _, out _), "other headsets remove a disconnected seat");
            NetworkManager returning = Join(2, 0.12f);
            yield return Until(() => relays[3].Roster.TryGet(2, out ulong owner, out _) && owner == returning.LocalClientId,
                "returning headset holds its original slot under its new connection");
            Assert.That(returning.LocalClientId, Is.Not.EqualTo(oldId));
            AssertPeer(3, 2, returning.LocalClientId, 0.12f);
        }

        static AvatarPose PlayerPose(byte slot, bool sitting, float leftLift, float rightLift)
        {
            var floor = new Vector3(slot, 0, 0);
            var heading = Quaternion.Euler(0, 15, 0);
            return new AvatarPose
            {
                Slot = slot, FreshFeet = 3,
                Head = new Pose(floor + Vector3.up * (sitting ? SeatedMotion.SittingEyeHeight : SeatedMotion.StandingEyeHeight), heading),
                LeftHand = Pose.identity, RightHand = Pose.identity,
                LeftFoot = SeatedMotion.Foot(floor, heading, true, leftLift, sitting),
                RightFoot = SeatedMotion.Foot(floor, heading, false, rightLift, sitting)
            };
        }

        NetworkManager Join(byte slot, float lift)
        {
            var sample = PlayerPose(slot, false, 0, lift);
            NetworkManager manager = CreateEndpoint(sample);
            manager.NetworkConfig.ConnectionData = new ConnectionPayload
            {
                playerGuid = new Guid(slot, 0, 0, new byte[8]).ToString(), playerName = "Seat " + slot,
                teamId = session.Config.teamId, appVersion = session.Config.appVersion
            }.ToBytes();
            Assert.That(manager.StartClient(), Is.True);
            return manager;
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

        void AssertPeer(int endpoint, int slot, ulong id, float lift)
        {
            Assert.That(relays[endpoint].Roster.TryGet(slot, out ulong owner, out AvatarPose pose), Is.True);
            Assert.That(owner, Is.EqualTo(id));
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
            yield return null;
            relays.Clear();
            managers.Clear();
            samples.Clear();
        }
    }
}
