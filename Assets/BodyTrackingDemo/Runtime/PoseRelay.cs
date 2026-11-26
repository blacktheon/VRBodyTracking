using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo
{
    // Each headset submits one pose. The tablet broadcasts all occupied seats together so a
    // missed packet, a late join or a departure is repaired by the next complete snapshot.
    internal sealed class PoseRelay : IDisposable
    {
        const string Upload = "Blacktheon.Seated.Pose.1";
        const string Snapshot = "Blacktheon.Seated.Room.1";
        const int PoseBytes = 2 + 5 * (3 + 4) * sizeof(float);
        const int EntryBytes = sizeof(ulong) + PoseBytes;
        readonly NetworkManager manager;
        CustomMessagingManager messages;
        PoseRoster incoming = new();
        double nextSend;
        internal PoseRoster Roster { get; private set; } = new();

        internal PoseRelay(NetworkManager manager)
        {
            this.manager = manager;
            manager.OnServerStarted += StartServer;
            manager.OnClientStarted += StartClient;
            manager.OnServerStopped += Stop;
            manager.OnClientStopped += Stop;
            manager.OnClientDisconnectCallback += Leave;
        }

        void StartServer()
        {
            messages = manager.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(Upload, ReceivePose);
        }

        void StartClient()
        {
            messages = manager.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(Snapshot, ReceiveRoom);
        }

        void Stop(bool _)
        {
            messages = null;
            Roster.Clear();
            incoming.Clear();
            nextSend = 0;
        }

        void Leave(ulong clientId)
        {
            if (manager.IsServer) Roster.Remove(clientId);
            else Roster.Clear();
        }

        internal void Tick(AvatarPose? local, double now)
        {
            Roster.Expire(now);
            if (messages == null || manager.ShutdownInProgress || now < nextSend) return;
            nextSend = now + 0.05;
            if (manager.IsServer)
            {
                if (manager.ConnectedClientsIds.Count == 0) return;
                using var writer = new FastBufferWriter(1 + 3 * EntryBytes, Allocator.Temp);
                writer.WriteValueSafe((byte)Roster.Count);
                for (int slot = 1; slot <= 3; slot++)
                {
                    if (!Roster.TryGet(slot, out ulong owner, out AvatarPose pose)) continue;
                    writer.WriteValueSafe(owner);
                    WritePose(writer, pose);
                }
                messages.SendNamedMessage(Snapshot, manager.ConnectedClientsIds, writer, NetworkDelivery.UnreliableSequenced);
            }
            else if (manager.IsConnectedClient && local.HasValue && local.Value.IsValid)
            {
                using var writer = new FastBufferWriter(PoseBytes, Allocator.Temp);
                WritePose(writer, local.Value);
                messages.SendNamedMessage(Upload, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
            }
        }

        void ReceivePose(ulong sender, FastBufferReader reader)
        {
            if (!manager.IsServer || !manager.ConnectedClients.ContainsKey(sender) || reader.Length - reader.Position != PoseBytes) return;
            Roster.Put(sender, ReadPose(reader), Time.unscaledTimeAsDouble);
        }

        void ReceiveRoom(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || reader.Length - reader.Position < 1) return;
            reader.ReadValueSafe(out byte count);
            if (count > 3 || reader.Length - reader.Position != count * EntryBytes) return;
            incoming.Clear();
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ulong owner);
                if (!incoming.Put(owner, ReadPose(reader), Time.unscaledTimeAsDouble)) return;
            }
            if (incoming.Count != count) return;
            (Roster, incoming) = (incoming, Roster);
        }

        static void WritePose(FastBufferWriter writer, AvatarPose pose)
        {
            writer.WriteValueSafe(pose.Slot);
            writer.WriteValueSafe(pose.FreshFeet);
            WriteJoint(writer, pose.Head);
            WriteJoint(writer, pose.LeftHand);
            WriteJoint(writer, pose.RightHand);
            WriteJoint(writer, pose.LeftFoot);
            WriteJoint(writer, pose.RightFoot);
        }

        static AvatarPose ReadPose(FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte slot);
            reader.ReadValueSafe(out byte fresh);
            return new AvatarPose
            {
                Slot = slot, FreshFeet = fresh, Head = ReadJoint(reader), LeftHand = ReadJoint(reader),
                RightHand = ReadJoint(reader), LeftFoot = ReadJoint(reader), RightFoot = ReadJoint(reader)
            };
        }

        static void WriteJoint(FastBufferWriter writer, Pose joint)
        {
            writer.WriteValueSafe(joint.position);
            writer.WriteValueSafe(joint.rotation);
        }

        static Pose ReadJoint(FastBufferReader reader)
        {
            reader.ReadValueSafe(out Vector3 position);
            reader.ReadValueSafe(out Quaternion rotation);
            return new Pose(position, rotation);
        }

        public void Dispose()
        {
            if (manager == null) return;
            manager.OnServerStarted -= StartServer;
            manager.OnClientStarted -= StartClient;
            manager.OnServerStopped -= Stop;
            manager.OnClientStopped -= Stop;
            manager.OnClientDisconnectCallback -= Leave;
            if (messages != null)
            {
                if (manager.IsServer) messages.UnregisterNamedMessageHandler(Upload);
                if (manager.IsClient) messages.UnregisterNamedMessageHandler(Snapshot);
            }
            Stop(false);
        }
    }
}
