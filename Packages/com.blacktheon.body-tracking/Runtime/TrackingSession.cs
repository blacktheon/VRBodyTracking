using System;
using UnityEngine;

namespace Blacktheon.BodyTracking
{
    public enum BodyTrackingMode { SixTrackers, PairedFeet }

    public enum BodyJoint { Head, Chest, Hips, LeftKnee, RightKnee, LeftFoot, RightFoot }

    internal sealed class TrackingSession
    {
        private const string FeetType = "blacktheon.feet.v1";
        private const int JointCount = (int)BodyJoint.RightFoot + 1;
        private const double JointFreshSeconds = 0.5;
        private readonly BodyTrackingMode mode;
        private readonly int slot;
        private readonly string pairedRequest;
        private readonly HeadFrame headFrame = new HeadFrame();
        private readonly FootLiftEstimator leftFoot;
        private readonly FootLiftEstimator rightFoot;
        private readonly string[] trackerIds = new string[JointCount];
        private readonly Pose[] localPoses = new Pose[JointCount];
        private readonly double[] poseTimes = new double[JointCount];
        private readonly bool[] hasPose = new bool[JointCount];
        private bool leftAssigned;
        private bool rightAssigned;

        public bool HasPairReply { get; private set; }
        public double LastReply { get; private set; } = double.NegativeInfinity;
        public string PairError { get; private set; }

        public TrackingSession(BodyTrackingMode mode, int slot, float threshold = 0.7f, float maxLift = 0.35f)
        {
            this.mode = mode;
            this.slot = slot;
            pairedRequest = JsonUtility.ToJson(new SlotRequest { slot = slot });
            leftFoot = new FootLiftEstimator(threshold, maxLift);
            rightFoot = new FootLiftEstimator(threshold, maxLift);
        }

        public string BuildRequest(Transform origin, Transform head)
        {
            if (mode == BodyTrackingMode.PairedFeet)
                return pairedRequest;
            if (!origin || !head)
                return null;

            Vector3 local = origin.InverseTransformPoint(head.position);
            Quaternion rotation = MirrorRotation(Quaternion.Inverse(origin.rotation) * head.rotation);
            // The stock v21.1.0 bridge adds 0.2 m to inbound HMD Y.
            headFrame.x = local.x;
            headFrame.y = local.y - 0.2f;
            headFrame.z = -local.z;
            headFrame.qx = rotation.x;
            headFrame.qy = rotation.y;
            headFrame.qz = rotation.z;
            headFrame.qw = rotation.w;
            return JsonUtility.ToJson(headFrame);
        }

        public void Apply(string json, double receivedAt)
        {
            Incoming frame;
            try
            {
                frame = JsonUtility.FromJson<Incoming>(json);
            }
            catch (ArgumentException)
            {
                return;
            }
            if (frame == null)
                return;

            if (mode == BodyTrackingMode.PairedFeet)
            {
                if (frame.type != FeetType || frame.slot != slot)
                    return;

                HasPairReply = true;
                LastReply = receivedAt;
                PairError = frame.error;
                leftAssigned = !string.IsNullOrEmpty(frame.left?.id);
                rightAssigned = !string.IsNullOrEmpty(frame.right?.id);
                bool duplicateId = frame.left != null && frame.right != null && frame.left.valid && frame.right.valid &&
                                   !string.IsNullOrEmpty(frame.left.id) && frame.left.id == frame.right.id;
                if (!string.IsNullOrEmpty(PairError) || duplicateId)
                {
                    leftFoot.Invalidate();
                    rightFoot.Invalidate();
                    return;
                }
                ApplyFoot(leftFoot, frame.left, receivedAt);
                ApplyFoot(rightFoot, frame.right, receivedAt);
                return;
            }

            if (string.IsNullOrEmpty(frame.tracker_id))
                return;
            if (frame.type == "config")
                Assign(frame.tracker_id, frame.location);
            else if (frame.type == "pos")
                StorePose(frame, receivedAt);
        }

        public bool TryGetJoint(BodyJoint joint, Transform origin, double now, out Pose pose)
        {
            int index = (int)joint;
            if (origin && index >= 0 && index < JointCount && hasPose[index] &&
                now - poseTimes[index] <= JointFreshSeconds)
            {
                Pose local = localPoses[index];
                pose = new Pose(origin.TransformPoint(local.position), origin.rotation * local.rotation);
                return true;
            }
            pose = default;
            return false;
        }

        public bool TryGetFootLift(bool left, double now, out float metres) =>
            (left ? leftFoot : rightFoot).TryGetLift(now, out metres);

        public void CalibrateFeet()
        {
            leftFoot.Calibrate();
            rightFoot.Calibrate();
        }

        public string Describe(double now)
        {
            if (mode == BodyTrackingMode.SixTrackers)
                return now - LastReply <= JointFreshSeconds ? "Receiving solved body poses" : "Waiting for solved body poses";

            if (!HasPairReply || now - LastReply > 2.5)
                return "Waiting for paired-feet extension on PC";
            if (PairError == "slot_in_use")
                return "Player slot " + slot + " is in use";
            if (!string.IsNullOrEmpty(PairError))
                return PairError;

            bool left = leftFoot.TryGetLift(now, out _);
            bool right = rightFoot.TryGetLift(now, out _);
            if (left && right)
                return "Tracking both feet";
            if (leftFoot.HasFreshSample(now) && rightFoot.HasFreshSample(now))
                return "Calibrating feet — sit with both shoes flat";
            if (leftFoot.HasFreshSample(now))
                return rightAssigned ? "Right shoe needs fresh acceleration" : "Right shoe unavailable";
            if (rightFoot.HasFreshSample(now))
                return leftAssigned ? "Left shoe needs fresh acceleration" : "Left shoe unavailable";
            if (leftAssigned || rightAssigned)
                return "Check shoe acceleration stream";
            return "Both shoes unavailable";
        }

        private static void ApplyFoot(FootLiftEstimator estimator, FootReading reading, double receivedAt)
        {
            if (reading == null)
                estimator.Invalidate();
            else
                estimator.Accept(reading.id, reading.valid, reading.ay, reading.t, receivedAt);
        }

        private void Assign(string id, string location)
        {
            int oldIndex = Array.IndexOf(trackerIds, id);
            int nextIndex = RoleIndex(location);
            if (oldIndex == nextIndex)
                return;
            if (oldIndex >= 0)
            {
                trackerIds[oldIndex] = null;
                hasPose[oldIndex] = false;
            }
            if (nextIndex >= 0)
            {
                trackerIds[nextIndex] = id;
                hasPose[nextIndex] = false;
            }
        }

        private void StorePose(Incoming frame, double receivedAt)
        {
            int index = Array.IndexOf(trackerIds, frame.tracker_id);
            if (index < 0)
                return;
            var rotation = new Quaternion(frame.qx, frame.qy, frame.qz, frame.qw);
            float lengthSquared = Quaternion.Dot(rotation, rotation);
            if (!IsFinite(frame.x) || !IsFinite(frame.y) || !IsFinite(frame.z) ||
                !IsFinite(lengthSquared) || lengthSquared < 0.0001f)
            {
                hasPose[index] = false;
                return;
            }
            localPoses[index] = new Pose(new Vector3(frame.x, frame.y, -frame.z), MirrorRotation(rotation.normalized));
            poseTimes[index] = receivedAt;
            hasPose[index] = true;
            LastReply = receivedAt;
        }

        private static int RoleIndex(string location)
        {
            switch (location)
            {
                case "hmd":
                case "head": return (int)BodyJoint.Head;
                case "chest": return (int)BodyJoint.Chest;
                case "waist": return (int)BodyJoint.Hips;
                case "left_knee": return (int)BodyJoint.LeftKnee;
                case "right_knee": return (int)BodyJoint.RightKnee;
                case "left_foot": return (int)BodyJoint.LeftFoot;
                case "right_foot": return (int)BodyJoint.RightFoot;
                default: return -1;
            }
        }

        private static Quaternion MirrorRotation(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        [Serializable]
        private sealed class SlotRequest
        {
            public string type = FeetType;
            public int slot;
        }

        [Serializable]
        private sealed class HeadFrame
        {
            public string type = "pos";
            public int tracker_id = 0;
            public float x, y, z, qx, qy, qz, qw;
        }

#pragma warning disable CS0649 // Populated by Unity's JSON serializer.
        [Serializable]
        private sealed class Incoming
        {
            public string type, tracker_id, location, error;
            public int slot;
            public float x, y, z, qx, qy, qz, qw;
            public FootReading left, right;
        }

        [Serializable]
        private sealed class FootReading
        {
            public string id;
            public bool valid;
            public float ay;
            public long t;
        }
#pragma warning restore CS0649
    }
}
