using System;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo
{
    internal enum SoldierModel : byte
    {
        Uniform,
        Armored
    }

    internal struct AvatarPose
    {
        internal byte Slot;
        internal byte FreshFeet; // bit 0 is the left shoe, bit 1 the right shoe.
        internal SoldierModel Model;
        internal Pose Head, LeftHand, RightHand, LeftFoot, RightFoot;

        internal bool IsValid => Slot >= 1 && Slot <= 3 && FreshFeet <= 3
            && (Model == SoldierModel.Uniform || Model == SoldierModel.Armored)
            && Valid(Head) && Valid(LeftHand) && Valid(RightHand) && Valid(LeftFoot) && Valid(RightFoot);

        static bool Valid(Pose pose)
        {
            Vector3 p = pose.position;
            float length = Quaternion.Dot(pose.rotation, pose.rotation);
            return Mathf.Abs(p.x) <= 7 && Mathf.Abs(p.z) <= 7 && p.y >= -0.5f && p.y <= 4
                && length >= 0.98f && length <= 1.02f;
        }
    }

    internal sealed class PoseRoster
    {
        struct Entry
        {
            internal ulong Owner;
            internal AvatarPose Pose;
            internal double Seen;
        }

        readonly Entry[] entries = new Entry[3];
        internal int Count { get; private set; }

        internal bool Put(ulong sender, AvatarPose pose, double now)
        {
            if (!pose.IsValid) return false;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry existing = entries[i];
                if (existing.Pose.Slot == 0) continue;
                if ((existing.Owner == sender) != (existing.Pose.Slot == pose.Slot)) return false;
            }
            int index = pose.Slot - 1;
            if (entries[index].Pose.Slot == 0) Count++;
            entries[index] = new Entry { Owner = sender, Pose = pose, Seen = now };
            return true;
        }

        internal bool TryGet(int slot, out ulong owner, out AvatarPose pose)
        {
            Entry entry = slot >= 1 && slot <= entries.Length ? entries[slot - 1] : default;
            owner = entry.Owner;
            pose = entry.Pose;
            return pose.Slot != 0;
        }

        internal void Remove(ulong owner)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].Pose.Slot != 0 && entries[i].Owner == owner) RemoveAt(i);
        }

        internal void SetModel(ulong owner, SoldierModel model)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].Pose.Slot != 0 && entries[i].Owner == owner)
                {
                    entries[i].Pose.Model = model;
                    return;
                }
        }

        internal void Expire(double now)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].Pose.Slot != 0 && now - entries[i].Seen > 1.5) RemoveAt(i);
        }

        void RemoveAt(int index)
        {
            entries[index] = default;
            Count--;
        }

        internal void Clear()
        {
            Array.Clear(entries, 0, entries.Length);
            Count = 0;
        }
    }
}
