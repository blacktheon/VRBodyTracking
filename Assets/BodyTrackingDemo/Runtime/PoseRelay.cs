using System;
using System.Collections.Generic;
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
        const string Appearance = "Blacktheon.Seated.Soldier.1";
        const int PoseBytes = 2 + 5 * (3 + 4) * sizeof(float);
        const int EntryBytes = sizeof(ulong) + PoseBytes;
        readonly NetworkManager manager;
        readonly Dictionary<ulong, SoldierModel> models = new();
        SoldierModel? announcedModel;
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
            messages.RegisterNamedMessageHandler(Appearance, ReceiveModel);
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
            models.Clear();
            announcedModel = null;
            nextSend = 0;
        }

        void Leave(ulong clientId)
        {
            if (manager.IsServer)
            {
                Roster.Remove(clientId);
                models.Remove(clientId);
            }
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
                // A choice announcement also tells the server that this headset reads model IDs.
                if (models.Count < manager.ConnectedClientsIds.Count) SendRoom(false);
                if (models.Count > 0) SendRoom(true);
            }
            else if (manager.IsConnectedClient && local.HasValue && local.Value.IsValid)
            {
                if (announcedModel != local.Value.Model)
                {
                    using var choice = new FastBufferWriter(1, Allocator.Temp);
                    choice.WriteValueSafe((byte)local.Value.Model);
                    messages.SendNamedMessage(Appearance, NetworkManager.ServerClientId, choice, NetworkDelivery.ReliableSequenced);
                    announcedModel = local.Value.Model;
                }
                using var writer = new FastBufferWriter(PoseBytes, Allocator.Temp);
                WritePose(writer, local.Value);
                messages.SendNamedMessage(Upload, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
            }
        }

        void SendRoom(bool includeModels)
        {
            using var writer = new FastBufferWriter(1 + 3 * (EntryBytes + 1), Allocator.Temp);
            writer.WriteValueSafe((byte)Roster.Count);
            for (int slot = 1; slot <= 3; slot++)
            {
                if (!Roster.TryGet(slot, out ulong owner, out var pose)) continue;
                writer.WriteValueSafe(owner);
                WritePose(writer, pose);
                if (includeModels) writer.WriteValueSafe((byte)pose.Model);
            }
            foreach (ulong client in manager.ConnectedClientsIds)
                if (models.ContainsKey(client) == includeModels)
                    messages.SendNamedMessage(Snapshot, client, writer, NetworkDelivery.UnreliableSequenced);
        }

        void ReceiveModel(ulong sender, FastBufferReader reader)
        {
            if (!manager.IsServer || !manager.ConnectedClients.ContainsKey(sender) || reader.Length - reader.Position != 1) return;
            reader.ReadValueSafe(out byte value);
            if (value > (byte)SoldierModel.Armored) return;
            var model = (SoldierModel)value;
            models[sender] = model;
            Roster.SetModel(sender, model);
        }

        void ReceivePose(ulong sender, FastBufferReader reader)
        {
            if (!manager.IsServer || !manager.ConnectedClients.ContainsKey(sender) || reader.Length - reader.Position != PoseBytes) return;
            var pose = ReadPose(reader);
            if (models.TryGetValue(sender, out var model)) pose.Model = model;
            Roster.Put(sender, pose, Time.unscaledTimeAsDouble);
        }

        void ReceiveRoom(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || reader.Length - reader.Position < 1) return;
            reader.ReadValueSafe(out byte count);
            int remaining = reader.Length - reader.Position;
            bool includesModels = remaining == count * (EntryBytes + 1);
            if (count > 3 || (!includesModels && remaining != count * EntryBytes)) return;
            incoming.Clear();
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ulong owner);
                var pose = ReadPose(reader);
                if (includesModels)
                {
                    reader.ReadValueSafe(out byte model);
                    pose.Model = (SoldierModel)model;
                }
                if (!incoming.Put(owner, pose, Time.unscaledTimeAsDouble)) return;
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
                Slot = slot, Model = DemoChoices.LegacyModel(slot), FreshFeet = fresh, Head = ReadJoint(reader), LeftHand = ReadJoint(reader),
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
                if (manager.IsServer)
                {
                    messages.UnregisterNamedMessageHandler(Upload);
                    messages.UnregisterNamedMessageHandler(Appearance);
                }
                if (manager.IsClient) messages.UnregisterNamedMessageHandler(Snapshot);
            }
            Stop(false);
        }
    }
}
