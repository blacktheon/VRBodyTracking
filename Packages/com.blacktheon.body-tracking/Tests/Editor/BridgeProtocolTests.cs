using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blacktheon.BodyTracking.Tests
{
    public sealed class BridgeProtocolTests
    {
        private const string LeftConfig = "{\"type\":\"config\",\"tracker_id\":\"SlimeVR Tracker 8\",\"location\":\"left_foot\"}";
        private const string PoseFrame = "{\"type\":\"pos\",\"tracker_id\":\"SlimeVR Tracker 8\",\"x\":1,\"y\":0.25,\"z\":-2,\"qx\":0.7071068,\"qy\":0,\"qz\":0,\"qw\":0.7071068}";
        private GameObject origin;
        private GameObject head;

        [SetUp]
        public void CreateRig()
        {
            origin = new GameObject("Floor origin");
            origin.transform.SetPositionAndRotation(new Vector3(10, 0.5f, 20), Quaternion.Euler(0, 90, 0));
            head = new GameObject("HMD");
            head.transform.SetParent(origin.transform, false);
            head.transform.localPosition = new Vector3(1, 1.55f, 2);
            head.transform.localRotation = Quaternion.Euler(0, 90, 0);
        }

        [TearDown]
        public void DestroyRig() => Object.DestroyImmediate(origin);

        [Test]
        public void SixTrackerRequestUndoesOriginAndStockBridgeHeadOffset()
        {
            var session = new TrackingSession(BodyTrackingMode.SixTrackers, 1);
            string json = session.BuildRequest(origin.transform, head.transform);
            var request = JsonUtility.FromJson<Request>(json);
            Assert.That(request.type, Is.EqualTo("pos"));
            Assert.That(request.tracker_id, Is.Zero);
            Assert.That(request.x, Is.EqualTo(1).Within(0.0001f));
            Assert.That(request.y, Is.EqualTo(1.35f).Within(0.0001f));
            Assert.That(request.z, Is.EqualTo(-2).Within(0.0001f));
            Assert.That(request.qy, Is.EqualTo(-0.7071068f).Within(0.0001f));
            Assert.That(request.qw, Is.EqualTo(0.7071068f).Within(0.0001f));
        }

        [Test]
        public void PairedRequestContainsOnlyTheChosenSlotEvenWithoutAHead()
        {
            var session = new TrackingSession(BodyTrackingMode.PairedFeet, 3);
            string json = session.BuildRequest(null, null);
            var request = JsonUtility.FromJson<Request>(json);
            Assert.That(request.type, Is.EqualTo("blacktheon.feet.v1"));
            Assert.That(request.slot, Is.EqualTo(3));
            StringAssert.DoesNotContain("tracker_id", json);
            StringAssert.DoesNotContain("qw", json);
        }

        [Test]
        public void SolvedPoseUsesAnnouncedRoleAndCurrentOriginUntilStale()
        {
            var session = new TrackingSession(BodyTrackingMode.SixTrackers, 1);
            session.Apply(PoseFrame, 1);
            Assert.That(session.TryGetJoint(BodyJoint.LeftFoot, origin.transform, 1, out _), Is.False);
            session.Apply(LeftConfig, 1);
            session.Apply(PoseFrame, 1.1);
            Assert.That(session.TryGetJoint(BodyJoint.LeftFoot, origin.transform, 1.11, out Pose pose), Is.True);
            AssertVector(new Vector3(12, 0.75f, 19), pose.position);
            AssertVector(Vector3.left, pose.rotation * Vector3.up);
            origin.transform.position += 2 * Vector3.forward;
            Assert.That(session.TryGetJoint(BodyJoint.LeftFoot, origin.transform, 1.2, out pose), Is.True);
            AssertVector(new Vector3(12, 0.75f, 21), pose.position);
            Assert.That(session.TryGetJoint(BodyJoint.LeftFoot, origin.transform, 2.2, out _), Is.False);
        }

        [Test]
        public void StockHmdAnnouncementExposesTheSolvedHead()
        {
            var session = new TrackingSession(BodyTrackingMode.SixTrackers, 1);
            session.Apply(LeftConfig.Replace("left_foot", "hmd"), 1);
            session.Apply(PoseFrame, 1.01);
            Assert.That(session.TryGetJoint(BodyJoint.Head, origin.transform, 1.02, out Pose pose), Is.True);
            AssertVector(new Vector3(12, 0.75f, 19), pose.position);
        }

        [Test]
        public void AnnouncedReassignmentClearsOldRoleAndWaistMeansHips()
        {
            var session = new TrackingSession(BodyTrackingMode.SixTrackers, 1);
            session.Apply(LeftConfig, 1);
            session.Apply(PoseFrame, 1);
            session.Apply(LeftConfig.Replace("left_foot", "waist"), 1.01);
            session.Apply(PoseFrame, 1.02);
            Assert.That(session.TryGetJoint(BodyJoint.LeftFoot, origin.transform, 1.03, out _), Is.False);
            Assert.That(session.TryGetJoint(BodyJoint.Hips, origin.transform, 1.03, out _), Is.True);
            session.Apply(LeftConfig.Replace("left_foot", "unknown_role"), 1.04);
            Assert.That(session.TryGetJoint(BodyJoint.Hips, origin.transform, 1.05, out _), Is.False);
        }

        [Test]
        public void PairedErrorsAndMissingSidesCannotReuseOldHeightOrAnotherPlayersData()
        {
            var session = new TrackingSession(BodyTrackingMode.PairedFeet, 2);
            for (int i = 1; i <= 80; i++)
            {
                string ay = i > 30 && i < 42 ? "2.4" : i >= 42 && i < 53 ? "-2.4" : "0";
                string frame = "{\"type\":\"blacktheon.feet.v1\",\"slot\":2,\"error\":\"\",\"left\":{\"id\":\"a\",\"valid\":true,\"ay\":" + ay +
                    ",\"t\":" + i * 20 + "},\"right\":{\"id\":\"b\",\"valid\":true,\"ay\":0,\"t\":" + i * 20 + "}}";
                session.Apply(frame, i * 0.02);
            }
            Assert.That(session.TryGetFootLift(true, 1.61, out float left), Is.True);
            Assert.That(left, Is.GreaterThan(0.025f));
            Assert.That(session.TryGetFootLift(false, 1.61, out float right), Is.True);
            Assert.That(right, Is.Zero);
            session.Apply("{\"type\":\"blacktheon.feet.v1\",\"slot\":1,\"error\":\"occupied\"}", 1.62);
            Assert.That(session.TryGetFootLift(true, 1.63, out _), Is.True);
            session.Apply("{\"type\":\"blacktheon.feet.v1\",\"slot\":2,\"error\":\"occupied\"}", 1.64);
            Assert.That(session.PairError, Is.EqualTo("occupied"));
            Assert.That(session.TryGetFootLift(true, 1.65, out _), Is.False);
            Assert.That(session.TryGetFootLift(false, 1.65, out _), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDuplicatedIdentityOnlyEnablesUnambiguousShoes(bool duplicate)
        {
            var session = new TrackingSession(BodyTrackingMode.PairedFeet, 1);
            for (int i = 1; i <= 35; i++)
            {
                string left = "{\"id\":\"shoe-a\",\"valid\":true,\"ay\":0,\"t\":" + (i * 20) + "}";
                string right = duplicate ? left : "null";
                session.Apply("{\"type\":\"blacktheon.feet.v1\",\"slot\":1,\"left\":" + left + ",\"right\":" + right + "}", i * 0.02);
            }
            Assert.That(session.TryGetFootLift(true, 0.71, out _), Is.EqualTo(!duplicate));
            Assert.That(session.TryGetFootLift(false, 0.71, out float rightHeight), Is.False);
            Assert.That(rightHeight, Is.Zero);
        }

        [Test]
        public void AssignedShoesWithoutFreshAccelerationExplainTheMissingSignal()
        {
            var session = new TrackingSession(BodyTrackingMode.PairedFeet, 1);
            const string reply = "{\"type\":\"blacktheon.feet.v1\",\"slot\":1,\"left\":{\"id\":\"L\",\"valid\":false},\"right\":{\"id\":\"R\",\"valid\":false}}";
            session.Apply(reply, 1);
            Assert.That(session.Describe(1.05), Does.Contain("acceleration"));
            Assert.That(session.TryGetFootLift(true, 1.05, out _), Is.False);
        }

        private static void AssertVector(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.0002f));

#pragma warning disable CS0649 // Populated by JsonUtility.
        [Serializable]
        private sealed class Request
        {
            public string type;
            public int tracker_id, slot;
            public float x, y, z, qy, qw;
        }
#pragma warning restore CS0649
    }
}
