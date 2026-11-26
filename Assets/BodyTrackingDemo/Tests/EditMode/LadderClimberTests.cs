using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public sealed class LadderClimberTests
    {
        GameObject world, rig;
        ClimbLadder ladder;
        LadderClimber climber;
        Transform left, right;
        double time;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BodyTrackingDemo/Runtime/VehicleLadder.prefab");
            Assert.That(prefab, Is.Not.Null, "load the actual mission layout");
            world = Object.Instantiate(prefab);
            ladder = world.GetComponentInChildren<ClimbLadder>();
            climber = new LadderClimber(ladder);
            rig = new GameObject("Tracked rig");
            left = new GameObject("Left controller").transform;
            right = new GameObject("Right controller").transform;
            left.SetParent(rig.transform);
            right.SetParent(rig.transform);
            time = 1;
        }

        [TearDown]
        public void TearDown()
        {
            if (world) Object.DestroyImmediate(world);
            if (rig) Object.DestroyImmediate(rig);
        }

        void Frame(bool l, bool r, float shoeL = 0, float shoeR = 0, byte fresh = 3)
        {
            time += 0.02;
            climber.Tick(rig.transform, rig.transform, left, right, l, r, fresh, shoeL, shoeR, true, time);
        }

        [Test]
        public void PullingTheGripRaisesTheRigAndThirdHandAdvanceDropsItForOneSecond()
        {
            left.position = ladder.RungPoint(3, true);
            Frame(true, false);
            Assert.That(climber.Snapshot.LeftHand, Is.EqualTo(3));
            Assert.That(climber.Feedback, Is.EqualTo(LadderResult.None));
            left.position -= Vector3.up * 0.22f;
            Frame(true, false);
            Assert.That(rig.transform.position.y, Is.EqualTo(0.22f).Within(0.002f));
            AvatarPose pose = default;
            climber.ApplyPose(ref pose, 0, 0);
            Assert.That(Vector3.Distance(pose.LeftHand.position, ladder.RungPoint(3, true)), Is.LessThan(0.001f));

            right.position = ladder.RungPoint(4, false);
            Frame(true, true);
            Assert.That(climber.Feedback, Is.EqualTo(LadderResult.Legal));
            Frame(false, true);
            left.position = ladder.RungPoint(5, true);
            Frame(true, true);
            Assert.That(climber.Snapshot.LeftHand, Is.EqualTo(5));
            Frame(true, false);
            right.position = ladder.RungPoint(6, false);
            Frame(true, true);
            Assert.That(climber.Feedback, Is.EqualTo(LadderResult.Fell));
            Assert.That(climber.Holding, Is.False);
            Assert.That(rig.transform.position.y, Is.EqualTo(0).Within(0.001f));
            Assert.That(climber.Snapshot.LeftFoot, Is.Zero);
            time += 0.90;
            Frame(true, true);
            Assert.That(climber.Feedback, Is.EqualTo(LadderResult.Fell));
            time += 0.10;
            Frame(true, true);
            Assert.That(climber.Feedback, Is.EqualTo(LadderResult.None));
            Assert.That(climber.Holding, Is.False, "a held grip cannot catch the ladder again after a fall");
            Frame(false, false);
            left.position = ladder.RungPoint(3, true);
            Frame(true, false);
            Assert.That(climber.Holding, Is.True);
        }

        [Test]
        public void APhysicalLiftAndLandingPlantsTheCorrectBootAndExpiresGreenAfterOneSecond()
        {
            right.position = ladder.RungPoint(4, false);
            Frame(false, true);
            Frame(false, true, 0.13f);
            Assert.That(climber.Snapshot.LeftFoot, Is.Zero);
            Frame(false, true);
            Assert.That(climber.Feedback, Is.EqualTo(LadderResult.Legal));
            AvatarPose pose = default;
            climber.ApplyPose(ref pose, 0, 0);
            Assert.That(pose.Climb.LeftFoot, Is.EqualTo(1));
            Assert.That(pose.LeftFoot.position.y, Is.EqualTo(ladder.RungPoint(1, true).y + SeatedMotion.ShoeCenterHeight).Within(0.002f));
            Assert.That(pose.RightFoot.position.y, Is.EqualTo(ladder.RungPoint(0, false).y + SeatedMotion.ShoeCenterHeight).Within(0.002f));
            Assert.That(rig.transform.position.y, Is.GreaterThan(0.15f));
            time += 0.94;
            Frame(false, true);
            Assert.That(climber.Feedback, Is.EqualTo(LadderResult.Legal));
            time += 0.05;
            Frame(false, true);
            Assert.That(climber.Feedback, Is.EqualTo(LadderResult.None));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ReleasingBothGripsAfterTheTopStepPlacesThePlayerOnTheAuthoredVehicleRoof(int footStepsPerHand)
        {
            bool lg = true, rg = false;
            left.position = ladder.RungPoint(5, true);
            Frame(lg, rg);
            void Foot(bool isLeft)
            {
                Frame(lg, rg, isLeft ? 0.13f : 0, isLeft ? 0 : 0.13f);
                Frame(lg, rg);
            }
            int nextFoot = 0;
            for (int rung = 6; rung <= 9; rung++)
            {
                for (int i = 0; i < footStepsPerHand; i++) Foot((nextFoot++ % 2) == 0);
                bool useLeft = (rung % 2) != 0;
                if (useLeft) lg = false; else rg = false;
                Frame(lg, rg);
                (useLeft ? left : right).position = ladder.RungPoint(rung, useLeft);
                if (useLeft) lg = true; else rg = true;
                Frame(lg, rg);
                Assert.That(climber.Feedback, Is.EqualTo(LadderResult.Legal));
            }
            Assert.That(climber.Snapshot.LeftHand, Is.EqualTo(9));
            // The controller can reach the top while the leading boot is still on rung four.
            Assert.That(climber.Snapshot.RightFoot, Is.EqualTo(4 * footStepsPerHand));
            Frame(false, false);
            Assert.That(climber.OnRoof, Is.True);
            Assert.That(climber.Holding, Is.False);
            Assert.That(climber.Snapshot.OnRoof, Is.True);
            Assert.That(rig.transform.position.y, Is.EqualTo(climber.PlatformHeight).Within(0.002f));
            Assert.That(climber.PlatformHeight, Is.GreaterThan(ladder.RungPoint(9, true).y));
            float roof = rig.transform.position.y;
            rig.transform.position += ladder.FaceDirection * 0.1f;
            Frame(false, false);
            Assert.That(rig.transform.position.y, Is.EqualTo(roof).Within(0.002f));
        }
    }
}
