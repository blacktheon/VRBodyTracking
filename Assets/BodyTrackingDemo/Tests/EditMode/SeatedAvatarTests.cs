using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class SeatedAvatarTests
    {
        [Test]
        public void HotspotAndWifiRoutesTakePriorityOverEthernetAndMobileData()
        {
            var wireless = System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211;
            var ethernet = System.Net.NetworkInformation.NetworkInterfaceType.Ethernet;
            Assert.That(TabletPanel.LanPriority("swlan0", ethernet), Is.LessThan(TabletPanel.LanPriority("wlan0", wireless)));
            Assert.That(TabletPanel.LanPriority("wlan0", wireless), Is.LessThan(TabletPanel.LanPriority("eth0", ethernet)));
            Assert.That(TabletPanel.LanPriority("rmnet0", ethernet), Is.EqualTo(-1));
            Assert.That(TabletPanel.LanPriority("tun0", System.Net.NetworkInformation.NetworkInterfaceType.Tunnel), Is.EqualTo(-1));
        }

        [TestCase("192.168.001.012", true, "192.168.1.12")]
        [TestCase("192.168.256.1", false, "")]
        [TestCase("192.168.1", false, "")]
        [TestCase("1..3.4", false, "")]
        public void HeadsetKeypadAcceptsOnlyCompleteIpv4Addresses(string entry, bool valid, string result)
        {
            Assert.That(DemoChoices.TryAddress(entry, out string address), Is.EqualTo(valid));
            if (valid) Assert.That(address, Is.EqualTo(result));
        }

        [TestCase(false, 0.06f)]
        [TestCase(true, 0.32f)]
        public void EachTrackedShoeLiftsVerticallyFromItsPostureAndReturnsToTheFloor(bool sitting, float forward)
        {
            var floor = new Vector3(3, 0, 2);
            var heading = Quaternion.Euler(0, 90, 0);
            Pose left = SeatedMotion.Foot(floor, heading, true, 0, sitting);
            Pose lifted = SeatedMotion.Foot(floor, heading, true, 0.23f, sitting);
            Pose right = SeatedMotion.Foot(floor, heading, false, -0.1f, sitting);
            Assert.That(left.position.x, Is.EqualTo(3 + forward).Within(0.001f));
            Assert.That(left.position.z, Is.GreaterThan(2.05f));
            Assert.That(right.position.z, Is.LessThan(1.95f));
            Assert.That(right.position.y, Is.EqualTo(left.position.y).Within(0.001f));
            Assert.That(lifted.position - left.position, Is.EqualTo(new Vector3(0, 0.23f, 0)).Using(Vector3EqualityComparer.Instance));
            Assert.That(SeatedMotion.Foot(floor, heading, true, float.NaN, sitting).position, Is.EqualTo(left.position));
        }

        [Test]
        public void TurningPivotsAroundOffsetHeadAndWalkingFollowsTheNewBodyHeading()
        {
            var rig = new GameObject("Seat");
            try
            {
                var head = new GameObject("Offset head").transform;
                head.SetParent(rig.transform, false);
                head.localPosition = new Vector3(0.7f, 1.22f, -0.4f);
                head.localRotation = Quaternion.Euler(65, -35, 0);
                Vector3 start = head.position;
                Quaternion heading = Quaternion.identity;
                SeatedMotion.Move(rig.transform, head, ref heading, Vector2.zero, 90, 0);
                Assert.That(head.position, Is.EqualTo(start).Using(Vector3EqualityComparer.Instance));
                SeatedMotion.Move(rig.transform, head, ref heading, Vector2.up, 0, 1);
                Assert.That(head.position, Is.EqualTo(start + Vector3.right).Using(Vector3EqualityComparer.Instance));
            }
            finally { Object.DestroyImmediate(rig); }
        }

        [Test]
        public void ThreePhysicalOriginsSpawnAtDifferentVirtualSeatsFacingTheCenter()
        {
            var rig = new GameObject("Arbitrary physical origin");
            try
            {
                var head = new GameObject("Head").transform;
                head.SetParent(rig.transform, false);
                head.localPosition = new Vector3(1.1f, 1.25f, 0.8f);
                head.localRotation = Quaternion.Euler(15, 50, 3);
                var positions = new Vector3[3];
                for (int slot = 1; slot <= 3; slot++)
                {
                    Quaternion heading = SeatedMotion.Place(rig.transform, head, slot);
                    positions[slot - 1] = Vector3.ProjectOnPlane(head.position, Vector3.up);
                    Assert.That(Vector3.Dot(heading * Vector3.forward, -positions[slot - 1].normalized), Is.GreaterThan(0.99f));
                    Assert.That(Vector3.Dot(Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized, heading * Vector3.forward), Is.GreaterThan(0.99f));
                    Assert.That(head.position.y, Is.EqualTo(1.25f).Within(0.001f));
                }
                Assert.That(Vector3.Distance(positions[0], positions[1]), Is.GreaterThan(2));
                Assert.That(Vector3.Distance(positions[0], positions[2]), Is.GreaterThan(2));
                Assert.That(Vector3.Distance(positions[1], positions[2]), Is.GreaterThan(2));
            }
            finally { Object.DestroyImmediate(rig); }
        }

        [Test]
        public void SlotOwnerCannotBeReplacedByAnotherClientUntilLeaveOrTimeout()
        {
            var roster = new PoseRoster();
            AvatarPose pose = Sample(1);
            Assert.That(roster.Put(7, pose, 1), Is.True);
            pose.LeftFoot.position.y = 0.4f;
            Assert.That(roster.Put(8, pose, 1.1), Is.False);
            Assert.That(roster.TryGet(1, out ulong owner, out var stored), Is.True);
            Assert.That(owner, Is.EqualTo(7ul));
            Assert.That(stored.LeftFoot.position.y, Is.EqualTo(0));
            roster.Remove(7);
            Assert.That(roster.Put(8, pose, 1.2), Is.True);
            roster.Expire(10);
            Assert.That(roster.TryGet(1, out _, out _), Is.False);
        }

        [Test]
        public void InvalidPoseCannotCorruptTheLastGoodFrameOrClaimAnotherSlot()
        {
            var roster = new PoseRoster();
            AvatarPose pose = Sample(2);
            Assert.That(roster.Put(11, pose, 0), Is.True);
            pose.Slot = 3;
            Assert.That(roster.Put(11, pose, 0.1), Is.False);
            pose.Slot = 2;
            pose.Head.position.x = float.NaN;
            Assert.That(roster.Put(11, pose, 0.2), Is.False);
            pose.Head = new Pose(Vector3.zero, new Quaternion());
            Assert.That(roster.Put(11, pose, 0.2), Is.False);
            pose.Head = new Pose(new Vector3(100, 0, 0), Quaternion.identity);
            Assert.That(roster.Put(11, pose, 0.2), Is.False);
            Assert.That(roster.TryGet(2, out ulong owner, out var good), Is.True);
            Assert.That(owner, Is.EqualTo(11ul));
            Assert.That(good.Head.position, Is.EqualTo(Vector3.zero));
        }

        static AvatarPose Sample(byte slot) => new AvatarPose
        {
            Slot = slot, Head = Pose.identity, LeftHand = Pose.identity, RightHand = Pose.identity,
            LeftFoot = Pose.identity, RightFoot = Pose.identity
        };

    }
}
