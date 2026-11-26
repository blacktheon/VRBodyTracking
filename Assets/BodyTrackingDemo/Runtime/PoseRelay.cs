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
        const string ClimbUpload = "Blacktheon.Seated.Climb.1";
        const string ClimbRoom = "Blacktheon.Seated.Climbers.1";
        const string World = "Blacktheon.Seated.World.1";
        const int PoseBytes = 2 + 5 * (3 + 4) * sizeof(float);
        const int EntryBytes = sizeof(ulong) + PoseBytes;
        const int ClimbBytes = 5;
        const byte RoofFlag = 0x80;
        const int ClimbRoomBytes = 3 * (sizeof(ulong) + ClimbBytes);
        const int WorldBytes = 2 * (3 + 4 + 3) * sizeof(float);
        readonly NetworkManager manager;
        readonly Dictionary<ulong, SoldierModel> models = new();
        // A reliable first upload also advertises support, including for a headset still choosing a seat.
        readonly Dictionary<ulong, (byte slot, ClimbSnapshot climb)> uploads = new();
        readonly (ulong owner, ClimbSnapshot climb)[] roomClimbs = new (ulong, ClimbSnapshot)[3];
        readonly (ulong owner, ClimbSnapshot climb)[] incomingClimbs = new (ulong, ClimbSnapshot)[3];
        SoldierModel? announcedModel;
        ClimbSnapshot? announcedClimb;
        byte announcedSlot;
        bool newClimbPeer;
        Transform vehicleRoot, ladderRoot;
        int rungCount = byte.MaxValue;
        (WorldTransform vehicle, WorldTransform ladder)? lastWorld;
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

        internal void BindWorld(Transform vehicle, Transform ladder)
        {
            vehicleRoot = vehicle;
            ladderRoot = ladder;
            var layout = ladder != null ? ladder.GetComponentInParent<ClimbLadder>() : null;
            rungCount = layout != null ? Mathf.Clamp(layout.RungCount, 0, byte.MaxValue) : byte.MaxValue;
            if (!manager.IsServer) ApplyWorld();
        }

        void StartServer()
        {
            messages = manager.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(Upload, ReceivePose);
            messages.RegisterNamedMessageHandler(Appearance, ReceiveModel);
            messages.RegisterNamedMessageHandler(ClimbUpload, ReceiveClimb);
        }

        void StartClient()
        {
            messages = manager.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(Snapshot, ReceiveRoom);
            messages.RegisterNamedMessageHandler(ClimbRoom, ReceiveClimbers);
            messages.RegisterNamedMessageHandler(World, ReceiveWorld);
        }

        void Stop(bool _)
        {
            messages = null;
            Roster.Clear();
            incoming.Clear();
            models.Clear();
            uploads.Clear();
            Array.Clear(roomClimbs, 0, roomClimbs.Length);
            announcedModel = null;
            announcedClimb = null;
            announcedSlot = 0;
            newClimbPeer = false;
            lastWorld = null;
            nextSend = 0;
        }

        void Leave(ulong clientId)
        {
            if (manager.IsServer)
            {
                Roster.Remove(clientId);
                models.Remove(clientId);
                uploads.Remove(clientId);
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
                if (uploads.Count != 0)
                {
                    SendClimbers();
                    SendWorld();
                    newClimbPeer = false;
                }
                foreach (ulong client in manager.ConnectedClientsIds) SendRoom(client);
            }
            else if (manager.IsConnectedClient)
            {
                ApplyWorld();
                if (!local.HasValue || !local.Value.IsValid)
                {
                    if (!local.HasValue || !announcedClimb.HasValue) SendClimb(announcedSlot, default);
                    return;
                }
                SendClimb(local.Value.Slot, local.Value.Climb);
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

        void SendRoom(ulong client)
        {
            bool includeModels = models.ContainsKey(client);
            bool allHeights = uploads.ContainsKey(client);
            using var writer = new FastBufferWriter(1 + 3 * (EntryBytes + 1), Allocator.Temp);
            int count = Roster.Count;
            if (!allHeights)
                for (int slot = 1; slot <= 3; slot++)
                    if (Roster.TryGet(slot, out _, out var pose) && !pose.FitsLegacyHeight) count--;
            writer.WriteValueSafe((byte)count);
            for (int slot = 1; slot <= 3; slot++)
            {
                if (!Roster.TryGet(slot, out ulong owner, out var pose)) continue;
                if (!allHeights && !pose.FitsLegacyHeight) continue;
                writer.WriteValueSafe(owner);
                WritePose(writer, pose);
                if (includeModels) writer.WriteValueSafe((byte)pose.Model);
            }
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
            if (uploads.TryGetValue(sender, out var upload) && upload.slot == pose.Slot) pose.Climb = upload.climb;
            else if (Roster.TryGet(pose.Slot, out ulong owner, out var previous) && sender == owner) pose.Climb = previous.Climb;
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
                if (pose.Slot >= 1 && pose.Slot <= 3 && roomClimbs[pose.Slot - 1].owner == owner)
                    pose.Climb = roomClimbs[pose.Slot - 1].climb;
                if (!incoming.Put(owner, pose, Time.unscaledTimeAsDouble)) return;
            }
            if (incoming.Count != count) return;
            (Roster, incoming) = (incoming, Roster);
        }

        void SendClimb(byte slot, ClimbSnapshot climb)
        {
            if (!climb.IsValid(rungCount) || (announcedSlot == slot && announcedClimb.HasValue && announcedClimb.Value.Equals(climb))) return;
            using var writer = new FastBufferWriter(1 + ClimbBytes, Allocator.Temp);
            writer.WriteValueSafe(slot);
            WriteClimb(writer, climb);
            messages.SendNamedMessage(ClimbUpload, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            announcedSlot = slot;
            announcedClimb = climb;
        }

        void ReceiveClimb(ulong sender, FastBufferReader reader)
        {
            if (!manager.IsServer || !manager.ConnectedClients.ContainsKey(sender) || reader.Length - reader.Position != 1 + ClimbBytes) return;
            reader.ReadValueSafe(out byte slot);
            var climb = ReadClimb(reader);
            if (slot > 3 || (slot == 0 && climb.Active) || !climb.IsValid(rungCount)) return;
            if (!uploads.ContainsKey(sender)) newClimbPeer = true;
            // A grip may precede its pose or the release of a seat. Only the matching owner's pose can use it.
            uploads[sender] = (slot, climb);
            Roster.SetClimb(slot, sender, climb);
        }

        void SendClimbers()
        {
            bool changed = false, any = false;
            for (int i = 0; i < roomClimbs.Length; i++)
            {
                (ulong owner, ClimbSnapshot climb) entry = Roster.TryGet(i + 1, out ulong owner, out var pose) && pose.Climb.Active
                    ? (owner, pose.Climb) : default;
                any |= entry.climb.Active;
                changed |= roomClimbs[i].owner != entry.owner || !roomClimbs[i].climb.Equals(entry.climb);
                roomClimbs[i] = entry;
            }
            if (!changed && !(newClimbPeer && any)) return;
            using var writer = new FastBufferWriter(ClimbRoomBytes, Allocator.Temp);
            foreach (var entry in roomClimbs)
            {
                writer.WriteValueSafe(entry.owner);
                WriteClimb(writer, entry.climb);
            }
            SendToClimbPeers(ClimbRoom, writer);
        }

        void ReceiveClimbers(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || reader.Length - reader.Position != ClimbRoomBytes) return;
            for (int i = 0; i < incomingClimbs.Length; i++)
            {
                reader.ReadValueSafe(out ulong owner);
                var climb = ReadClimb(reader);
                if (!climb.IsValid(rungCount)) return;
                incomingClimbs[i] = (owner, climb);
            }
            Array.Copy(incomingClimbs, roomClimbs, roomClimbs.Length);
            for (int i = 0; i < roomClimbs.Length; i++)
                if (Roster.TryGet(i + 1, out ulong owner, out _))
                    Roster.SetClimb(i + 1, owner, roomClimbs[i].owner == owner ? roomClimbs[i].climb : default);
        }

        static void WriteClimb(FastBufferWriter writer, ClimbSnapshot climb)
        {
            writer.WriteValueSafe(climb.LeftHand);
            writer.WriteValueSafe(climb.RightHand);
            writer.WriteValueSafe(climb.LeftFoot);
            writer.WriteValueSafe(climb.RightFoot);
            writer.WriteValueSafe((byte)((byte)climb.Feedback | (climb.OnRoof ? RoofFlag : 0)));
        }

        static ClimbSnapshot ReadClimb(FastBufferReader reader)
        {
            var climb = new ClimbSnapshot();
            reader.ReadValueSafe(out climb.LeftHand);
            reader.ReadValueSafe(out climb.RightHand);
            reader.ReadValueSafe(out climb.LeftFoot);
            reader.ReadValueSafe(out climb.RightFoot);
            reader.ReadValueSafe(out byte flags);
            climb.Feedback = (ClimbFeedback)(flags & ~RoofFlag);
            climb.OnRoof = (flags & RoofFlag) != 0;
            return climb;
        }

        internal readonly struct WorldTransform
        {
            readonly Pose pose;
            readonly Vector3 scale;

            internal WorldTransform(Transform root) : this(new Pose(root.position, root.rotation), root.localScale) { }

            internal WorldTransform(Pose pose, Vector3 scale)
            {
                this.pose = pose;
                this.scale = scale;
            }

            internal bool IsValid => Mathf.Abs(pose.position.x) <= 7 && Mathf.Abs(pose.position.y) <= 7 && Mathf.Abs(pose.position.z) <= 7 &&
                AvatarPose.ValidRotation(pose.rotation) && ValidScale(scale.x) && ValidScale(scale.y) && ValidScale(scale.z);

            static bool ValidScale(float axis) => Mathf.Abs(axis) >= 0.001f && Mathf.Abs(axis) <= 32;
            internal bool Matches(WorldTransform other) => pose.position == other.pose.position && scale == other.scale &&
                Mathf.Abs(Quaternion.Dot(pose.rotation, other.pose.rotation)) > 0.999999f;

            internal void Apply(Transform root)
            {
                root.localScale = scale;
                root.SetPositionAndRotation(pose.position, pose.rotation);
            }

            internal void Write(FastBufferWriter writer)
            {
                WriteJoint(writer, pose);
                writer.WriteValueSafe(scale);
            }

            internal static WorldTransform Read(FastBufferReader reader)
            {
                var pose = ReadJoint(reader);
                reader.ReadValueSafe(out Vector3 scale);
                return new WorldTransform(pose, scale);
            }
        }

        void SendWorld()
        {
            if (vehicleRoot == null || ladderRoot == null) return;
            var vehicle = new WorldTransform(vehicleRoot);
            var ladder = new WorldTransform(ladderRoot);
            if (vehicle.IsValid && ladder.IsValid)
            {
                if (!newClimbPeer && lastWorld.HasValue && vehicle.Matches(lastWorld.Value.vehicle) && ladder.Matches(lastWorld.Value.ladder)) return;
                lastWorld = (vehicle, ladder);
            }
            else
            {
                if (!newClimbPeer || !lastWorld.HasValue) return;
                (vehicle, ladder) = lastWorld.Value;
            }
            using var writer = new FastBufferWriter(WorldBytes, Allocator.Temp);
            vehicle.Write(writer);
            ladder.Write(writer);
            SendToClimbPeers(World, writer);
        }

        void ReceiveWorld(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || reader.Length - reader.Position != WorldBytes) return;
            var vehicle = WorldTransform.Read(reader);
            var ladder = WorldTransform.Read(reader);
            if (!vehicle.IsValid || !ladder.IsValid) return;
            lastWorld = (vehicle, ladder);
            ApplyWorld();
        }

        void ApplyWorld()
        {
            if (!lastWorld.HasValue || vehicleRoot == null || ladderRoot == null) return;
            var world = lastWorld.Value;
            if (!world.vehicle.Matches(new WorldTransform(vehicleRoot))) world.vehicle.Apply(vehicleRoot);
            if (!world.ladder.Matches(new WorldTransform(ladderRoot))) world.ladder.Apply(ladderRoot);
        }

        void SendToClimbPeers(string name, FastBufferWriter writer)
        {
            foreach (ulong client in uploads.Keys)
                messages.SendNamedMessage(name, client, writer, NetworkDelivery.ReliableSequenced);
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
                    messages.UnregisterNamedMessageHandler(ClimbUpload);
                }
                if (manager.IsClient)
                {
                    messages.UnregisterNamedMessageHandler(Snapshot);
                    messages.UnregisterNamedMessageHandler(ClimbRoom);
                    messages.UnregisterNamedMessageHandler(World);
                }
            }
            Stop(false);
        }
    }
}
