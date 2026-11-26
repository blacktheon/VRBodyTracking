using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class SeatedAvatarTests
    {
        [Test]
        public void SavedSoldierFollowsTheHeadsetAcrossSeatsAndUpgradesAnOlderInstallation()
        {
            const string roleKey = "Blacktheon.SeatedDemo.role", modelKey = "Blacktheon.SeatedDemo.model";
            bool hadRole = PlayerPrefs.HasKey(roleKey), hadModel = PlayerPrefs.HasKey(modelKey);
            bool hadPc = PlayerPrefs.HasKey("Blacktheon.SeatedDemo.pcOverride"), hadTablet = PlayerPrefs.HasKey("Blacktheon.SeatedDemo.tablet");
            int oldRole = PlayerPrefs.GetInt(roleKey), oldModel = PlayerPrefs.GetInt(modelKey);
            try
            {
                PlayerPrefs.DeleteKey(roleKey);
                PlayerPrefs.DeleteKey(modelKey);
                Assert.That(DemoChoices.Load().Model, Is.EqualTo(SoldierModel.Uniform));
                PlayerPrefs.SetInt(roleKey, 2);
                var choices = DemoChoices.Load();
                Assert.That(choices.Model, Is.EqualTo(SoldierModel.Armored));
                choices.Model = SoldierModel.Uniform;
                choices.Save();
                Assert.That(DemoChoices.Load().Model, Is.EqualTo(SoldierModel.Uniform));
                choices.Model = SoldierModel.Armored;
                choices.Role = 1;
                choices.Save();
                Assert.That(DemoChoices.Load().Model, Is.EqualTo(SoldierModel.Armored));
            }
            finally
            {
                if (hadRole) PlayerPrefs.SetInt(roleKey, oldRole); else PlayerPrefs.DeleteKey(roleKey);
                if (hadModel) PlayerPrefs.SetInt(modelKey, oldModel); else PlayerPrefs.DeleteKey(modelKey);
                if (!hadPc) PlayerPrefs.DeleteKey("Blacktheon.SeatedDemo.pcOverride");
                if (!hadTablet) PlayerPrefs.DeleteKey("Blacktheon.SeatedDemo.tablet");
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void UpgradingUsesTheOldPcAsAnAutoDiscoveryHintAndKeepsAnExplicitOverrideSeparate()
        {
            const string hintKey = "Blacktheon.SeatedDemo.pc", overrideKey = "Blacktheon.SeatedDemo.pcOverride";
            string oldHint = PlayerPrefs.GetString(hintKey), oldOverride = PlayerPrefs.GetString(overrideKey);
            bool hadHint = PlayerPrefs.HasKey(hintKey), hadOverride = PlayerPrefs.HasKey(overrideKey);
            try
            {
                PlayerPrefs.SetString(hintKey, "192.168.1.8");
                PlayerPrefs.DeleteKey(overrideKey);
                Assert.That(DemoChoices.Load().Pc, Is.Empty);
                Assert.That(DemoChoices.LastTrackingPc, Is.EqualTo("192.168.1.8"));

                PlayerPrefs.SetString(overrideKey, "192.168.1.20");
                DemoChoices.LastTrackingPc = "192.168.1.14";
                Assert.That(DemoChoices.Load().Pc, Is.EqualTo("192.168.1.20"));
                Assert.That(DemoChoices.LastTrackingPc, Is.EqualTo("192.168.1.14"));
            }
            finally
            {
                if (hadHint) PlayerPrefs.SetString(hintKey, oldHint); else PlayerPrefs.DeleteKey(hintKey);
                if (hadOverride) PlayerPrefs.SetString(overrideKey, oldOverride); else PlayerPrefs.DeleteKey(overrideKey);
                PlayerPrefs.Save();
            }
        }

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

        [TestCase(false)]
        [TestCase(true)]
        public void SoldierFollowsTrackedEyesWristsAndShoesWhenStandingAndSitting(bool own)
        {
            var model = new GameObject("Soldier");
            try
            {
                var pelvis = Joint(model.transform, "pelvis", 0, 0.97f, 0);
                var spine = Joint(pelvis, "spine_01", 0, 1.24f, 0);
                var neck = Joint(spine, "neck_01", 0, 1.48f, 0);
                var head = Joint(neck, "head", 0, 1.55f, 0);
                var eyeL = Joint(head, "eye_l", -0.03f, 1.65f, 0.06f);
                var eyeR = Joint(head, "eye_r", 0.03f, 1.65f, 0.06f);
                for (int i = 0; i < 2; i++)
                {
                    string side = i == 0 ? "_l" : "_r";
                    int sign = i == 0 ? -1 : 1;
                    var thigh = Joint(pelvis, "thigh" + side, sign * 0.13f, 0.99f, 0);
                    var calf = Joint(thigh, "calf" + side, sign * 0.15f, 0.56f, 0.02f);
                    var foot = Joint(calf, "foot" + side, sign * 0.16f, 0.14f, -0.06f);
                    Joint(foot, "ball" + side, sign * 0.16f, 0.025f, 0.08f);
                    var shoulder = Joint(spine, "upperarm" + side, sign * 0.17f, 1.44f, 0);
                    var elbow = Joint(shoulder, "lowerarm" + side, sign * 0.40f, 1.29f, -0.04f);
                    Joint(elbow, "hand" + side, sign * 0.59f, 1.10f, 0);
                }
                var face = Joint(model.transform, "Face", 0, 0, 0).gameObject.AddComponent<MeshRenderer>();
                var helmet = Joint(model.transform, "Headgear", 0, 0, 0).gameObject.AddComponent<MeshRenderer>();
                var shirt = Joint(model.transform, "Shirt", 0, 0, 0).gameObject.AddComponent<MeshRenderer>();
                var soldier = SoldierAvatar.Bind(model.transform, own);
                Assert.That(soldier, Is.Not.Null);
                Assert.That(face.enabled, Is.EqualTo(!own));
                Assert.That(helmet.enabled, Is.EqualTo(!own));
                Assert.That(shirt.enabled, Is.True);

                var floor = new Vector3(1, 0, -2);
                var body = Quaternion.Euler(0, 117, 0);
                var pose = Sample(2);
                pose.Head = new Pose(floor + Vector3.up * SeatedMotion.StandingEyeHeight, body);
                pose.LeftHand = new Pose(pose.Head.position + body * new Vector3(-0.26f, -0.4f, 0.22f), body);
                pose.RightHand = new Pose(pose.Head.position + body * new Vector3(0.29f, -0.35f, 0.19f), body);
                pose.LeftFoot = SeatedMotion.Foot(floor, body, true, 0, false);
                pose.RightFoot = SeatedMotion.Foot(floor, body, false, 0, false);
                soldier.Draw(pose);
                var leftThigh = pelvis.Find("thigh_l");
                var leftCalf = leftThigh.Find("calf_l");
                var leftFoot = leftCalf.Find("foot_l");
                var rightFoot = pelvis.Find("thigh_r/calf_r/foot_r");
                Vector3 restAnkle = leftFoot.position;
                Assert.That(restAnkle.y, Is.EqualTo(0.14f).Within(0.001f));
                Assert.That(Vector3.Dot((leftCalf.position - leftThigh.position).normalized,
                    (leftFoot.position - leftCalf.position).normalized), Is.GreaterThan(0.9f));

                pose.Head.position += new Vector3(0.12f, -0.45f, -0.08f);
                pose.Head.rotation = body * Quaternion.Euler(57, 21, 7);
                pose.LeftHand.position += Vector3.down * 0.3f;
                pose.RightHand.position += Vector3.down * 0.4f;
                pose.LeftFoot = SeatedMotion.Foot(floor, body, true, 0.27f, true);
                pose.RightFoot = SeatedMotion.Foot(floor, body, false, 0, true);
                soldier.Draw(pose);
                Assert.That(Vector3.Distance((eyeL.position + eyeR.position) * 0.5f, pose.Head.position), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(head.rotation, pose.Head.rotation), Is.LessThan(0.1f));
                Assert.That(Vector3.Distance(spine.Find("upperarm_l/lowerarm_l/hand_l").position,
                    pose.LeftHand.position), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(spine.Find("upperarm_r/lowerarm_r/hand_r").position,
                    pose.RightHand.position), Is.LessThan(0.001f));
                Assert.That(leftFoot.position.y - restAnkle.y, Is.EqualTo(0.27f).Within(0.001f));
                Assert.That(rightFoot.position.y, Is.EqualTo(restAnkle.y).Within(0.001f));
                Assert.That(Vector3.Dot((leftCalf.position - leftThigh.position).normalized,
                    (leftFoot.position - leftCalf.position).normalized), Is.LessThan(0.6f));

                pose.LeftFoot = SeatedMotion.Foot(floor, body, true, 0, true);
                soldier.Draw(pose);
                Assert.That(leftFoot.position.y, Is.EqualTo(restAnkle.y).Within(0.001f), "the lifted boot returns to the floor");
                Assert.That(rightFoot.position.y, Is.EqualTo(restAnkle.y).Within(0.001f));
            }
            finally { Object.DestroyImmediate(model); }
        }

        static Transform Joint(Transform parent, string name, float x, float y, float z)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.position = new Vector3(x, y, z);
            return joint;
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
