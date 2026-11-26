using NUnit.Framework;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class LadderClimbTests
    {
        const int Rungs = 10;
        LadderClimb climb;

        [SetUp]
        public void StartOnFloor() => climb = new LadderClimb();

        [TestCase(true)]
        [TestCase(false)]
        public void EitherHandCanStartOnFiveAndAdvancePastTheOtherHandOnSix(bool first)
        {
            Assert.That(climb.Grab(first, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.Grab(first, 11, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.Holding, Is.False);

            Assert.That(climb.Grab(first, 5, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.Holding, Is.True);
            Assert.That(climb.Grab(!first, 5, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.HandRung(!first), Is.EqualTo(5));
            climb.Release(!first);

            Assert.That(climb.Grab(!first, 7, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.HandRung(!first), Is.Zero);
            Assert.That(climb.Grab(!first, 6, Rungs), Is.EqualTo(LadderResult.Legal));
            Assert.That(climb.Grab(first, 7, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.HandRung(first), Is.EqualTo(5));
            climb.Release(first);
            Assert.That(climb.Grab(first, 7, Rungs), Is.EqualTo(LadderResult.Legal));
            Assert.That(climb.HandRung(!first), Is.EqualTo(6));
            Assert.That(climb.HandRung(first), Is.EqualTo(7));
        }

        [Test]
        public void RegripsAndSkippedRungsDoNotChangeTheTwoHandStepStreak()
        {
            climb.Grab(false, 6, Rungs);
            Assert.That(climb.Grab(true, 5, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.HandRung(true), Is.EqualTo(5));
            climb.Release(true);
            Assert.That(climb.Grab(true, 7, Rungs), Is.EqualTo(LadderResult.Legal));
            climb.Release(true);
            Assert.That(climb.Grab(true, 7, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.HandRung(true), Is.EqualTo(7));
            climb.Release(false);
            Assert.That(climb.Grab(false, 8, Rungs), Is.EqualTo(LadderResult.Legal));
            climb.Release(true);
            Assert.That(climb.Grab(true, 10, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.Grab(true, 9, Rungs), Is.EqualTo(LadderResult.Fell));
            Assert.That(climb.Holding, Is.False);
            Assert.That(climb.HandRung(true), Is.Zero);
            Assert.That(climb.HandRung(false), Is.Zero);
            climb.Grab(false, 2, Rungs);
            Assert.That(climb.Grab(true, 3, Rungs), Is.EqualTo(LadderResult.Legal));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LowerShoeStepsOnePastItsPartnerOnReturningToThePhysicalFloor(bool first)
        {
            climb.Grab(true, 5, Rungs);
            Assert.That(LiftAndReturn(first), Is.EqualTo(LadderResult.Legal));
            Assert.That(climb.FootRung(first), Is.EqualTo(1));
            Assert.That(climb.FootRung(!first), Is.Zero);
            Assert.That(LiftAndReturn(first), Is.EqualTo(LadderResult.None));
            Assert.That(LiftAndReturn(!first), Is.EqualTo(LadderResult.Legal));
            Assert.That(climb.FootRung(!first), Is.EqualTo(2));
            Assert.That(climb.FootRung(first), Is.EqualTo(1));
        }

        [Test]
        public void FootNeedsALowHighLowSequenceWhileHeldAndEachLandingScoresOnlyOnce()
        {
            climb.SampleFeet(3, 0, 0, Rungs);
            climb.SampleFeet(3, 0.15f, 0, Rungs);
            climb.Grab(true, 5, Rungs);
            Assert.That(climb.SampleFeet(3, 0.15f, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.SampleFeet(3, 0, 0, Rungs), Is.EqualTo(LadderResult.None));
            climb.SampleFeet(3, 0.079f, 0, Rungs);
            Assert.That(climb.SampleFeet(3, 0, 0, Rungs), Is.EqualTo(LadderResult.None));

            Assert.That(climb.SampleFeet(3, 0.08f, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.SampleFeet(3, 0.08f, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.SampleFeet(3, 0.031f, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.SampleFeet(3, 0.03f, 0, Rungs), Is.EqualTo(LadderResult.Legal));
            Assert.That(climb.SampleFeet(3, 0.03f, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.FootRung(true), Is.EqualTo(1));
        }

        [TestCase(2, 0f)]
        [TestCase(3, float.NaN)]
        [TestCase(3, float.PositiveInfinity)]
        public void LossOfMovingShoeRequiresANewGroundedSampleAndLift(int fresh, float reading)
        {
            climb.Grab(true, 5, Rungs);
            climb.SampleFeet(3, 0, 0, Rungs);
            climb.SampleFeet(3, 0.12f, 0, Rungs);
            Assert.That(climb.SampleFeet((byte)fresh, reading, 0, Rungs), Is.EqualTo(LadderResult.None));
            climb.SampleFeet(1, 0.12f, 0, Rungs);
            Assert.That(climb.SampleFeet(1, 0, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.FootRung(true), Is.Zero);
            climb.SampleFeet(1, 0.12f, 0, Rungs);
            Assert.That(climb.SampleFeet(1, 0, 0, Rungs), Is.EqualTo(LadderResult.Legal));
        }

        [Test]
        public void ReleasingTheLastHandInterruptsALiftEvenBetweenShoeSamples()
        {
            climb.Grab(true, 5, Rungs);
            climb.SampleFeet(3, 0, 0, Rungs);
            climb.SampleFeet(3, 0.12f, 0, Rungs);
            climb.Release(true);
            climb.Grab(false, 5, Rungs);
            Assert.That(climb.SampleFeet(3, 0, 0, Rungs), Is.EqualTo(LadderResult.None));

            climb.SampleFeet(3, 0.12f, 0, Rungs);
            climb.Grab(true, 5, Rungs);
            climb.Release(false);
            Assert.That(climb.SampleFeet(3, 0, 0, Rungs), Is.EqualTo(LadderResult.Legal));
        }

        [Test]
        public void LandingBothShoesTogetherFromTheFloorAdvancesOneFoot()
        {
            climb.Grab(true, 5, Rungs);
            climb.SampleFeet(3, 0, 0, Rungs);
            climb.SampleFeet(3, 0.12f, 0.12f, Rungs);
            Assert.That(climb.SampleFeet(3, 0, 0, Rungs), Is.EqualTo(LadderResult.Legal));
            Assert.That(climb.SampleFeet(3, 0, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.FootRung(true) + climb.FootRung(false), Is.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void APartnerAlreadyInTheAirNeedsANewLiftAfterTheFirstShoeLands(bool first)
        {
            climb.Grab(true, 5, Rungs);
            climb.SampleFeet(3, 0, 0, Rungs);
            climb.SampleFeet(3, 0.12f, 0.12f, Rungs);
            Assert.That(climb.SampleFeet(3, first ? 0 : 0.12f, first ? 0.12f : 0, Rungs),
                Is.EqualTo(LadderResult.Legal));
            Assert.That(climb.SampleFeet(3, 0, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.FootRung(first), Is.EqualTo(1));
            Assert.That(climb.FootRung(!first), Is.Zero);
            Assert.That(LiftAndReturn(!first), Is.EqualTo(LadderResult.Legal));
        }

        [Test]
        public void SwitchingBetweenHandsAndFeetRestartsTheTwoStepAllowance()
        {
            climb.Grab(true, 5, Rungs);
            Assert.That(climb.Grab(false, 6, Rungs), Is.EqualTo(LadderResult.Legal));
            Assert.That(LiftAndReturn(true), Is.EqualTo(LadderResult.Legal));
            Assert.That(LiftAndReturn(false), Is.EqualTo(LadderResult.Legal));
            climb.Release(true);
            Assert.That(climb.Grab(true, 7, Rungs), Is.EqualTo(LadderResult.Legal));
            climb.Release(false);
            Assert.That(climb.Grab(false, 8, Rungs), Is.EqualTo(LadderResult.Legal));
            Assert.That(LiftAndReturn(true), Is.EqualTo(LadderResult.Legal));
            Assert.That(climb.FootRung(true), Is.EqualTo(3));
            Assert.That(LiftAndReturn(false), Is.EqualTo(LadderResult.Legal));
            Assert.That(climb.FootRung(false), Is.EqualTo(4));
            Assert.That(LiftAndReturn(true), Is.EqualTo(LadderResult.Fell));
            Assert.That(climb.Holding, Is.False);
            Assert.That(climb.FootRung(true), Is.Zero);
            Assert.That(climb.FootRung(false), Is.Zero);
        }

        [Test]
        public void TheHighestFootholdAndResetRespectTheLadderEndpoints()
        {
            climb.Grab(true, 1, 1);
            Assert.That(LiftAndReturn(true, 1), Is.EqualTo(LadderResult.Legal));
            Assert.That(LiftAndReturn(false, 1), Is.EqualTo(LadderResult.None));
            Assert.That(climb.FootRung(true), Is.EqualTo(1));
            Assert.That(climb.FootRung(false), Is.Zero);
            climb.Reset();
            Assert.That(climb.Holding, Is.False);
            Assert.That(climb.FootRung(true), Is.Zero);
            climb.Grab(false, 1, 1);
            Assert.That(LiftAndReturn(false, 1), Is.EqualTo(LadderResult.Legal));
        }

        [Test]
        public void CancellingShoeGesturesKeepsTheCurrentSupportAndStepStreak()
        {
            climb.Grab(true, 5, Rungs);
            Assert.That(LiftAndReturn(true), Is.EqualTo(LadderResult.Legal));
            climb.SampleFeet(3, 0, 0.12f, Rungs);
            climb.CancelFootGestures();
            Assert.That(climb.SampleFeet(3, 0, 0, Rungs), Is.EqualTo(LadderResult.None));
            Assert.That(climb.FootRung(true), Is.EqualTo(1));
            Assert.That(LiftAndReturn(false), Is.EqualTo(LadderResult.Legal));
            Assert.That(LiftAndReturn(true), Is.EqualTo(LadderResult.Fell));
        }

        LadderResult LiftAndReturn(bool left, int rungCount = Rungs)
        {
            climb.SampleFeet(3, 0, 0, rungCount);
            climb.SampleFeet(3, left ? 0.12f : 0, left ? 0 : 0.12f, rungCount);
            return climb.SampleFeet(3, 0, 0, rungCount);
        }
    }
}
