using NUnit.Framework;

namespace Blacktheon.BodyTracking.Tests
{
    public sealed class FootLiftTests
    {
        private FootLiftEstimator foot;
        private long time;
        private double Now => time * 0.001;

        [SetUp]
        public void CalibrateAtRest()
        {
            foot = new FootLiftEstimator();
            time = 1000;
            Samples(0, 28);
        }

        [Test]
        public void StationaryShoeStaysGroundedWithFreshSamples()
        {
            Samples(0.07f, 250);
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void FlatShoeLiftsBrakesAndHoldsWhileSamplesKeepArriving()
        {
            Rise();
            float held = Read();
            Assert.That(held, Is.InRange(0.025f, 0.20f));
            Samples(0, 200);
            Assert.That(Read(), Is.EqualTo(held).Within(0.003f));
        }

        [Test]
        public void DownwardMotionAndLandingReturnTheFootToNeutral()
        {
            Rise();
            float held = Read();
            Samples(-2.4f, 7);
            Assert.That(Read(), Is.LessThan(held - 0.006f));
            Samples(-2.4f, 2);
            Samples(2.4f, 9);
            Samples(0, 30);
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void ImmediateTurnaroundLowersBeforeLandingOnEachStep()
        {
            for (int step = 0; step < 2; step++)
            {
                Samples(2.4f, 9);
                Samples(-2.4f, 9); // Braking the lift runs directly into the downstroke.
                float apex = Read();
                Assert.That(apex, Is.GreaterThan(0.03f));
                Samples(-2.4f, 7);
                Assert.That(Read(), Is.LessThan(apex - 0.008f));
                Samples(-2.4f, 2);
                Samples(2.4f, 9);
                Samples(0f, 8);
                Assert.That(Read(), Is.Zero);
            }
        }

        [Test]
        public void GentleLoweringMovesBeforeTheShoeComesToRest()
        {
            Rise();
            Samples(0f, 100);
            float held = Read();
            Samples(-0.35f, 15);
            Assert.That(Read(), Is.LessThan(held - 0.008f));
            Samples(0f, 8);
            Samples(0.35f, 15);
            Samples(0f, 8);
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void SoftDownstrokeRetainsWeakReadingsLeadingIntoAPeak()
        {
            Rise();
            float held = Read();
            Sample(-0.21f, 70);
            Sample(-0.22f, 70);
            Sample(-0.21f, 70);
            Sample(-0.19f, 70);
            Sample(-0.35f, 70);
            Assert.That(Read(), Is.LessThan(held - 0.006f));
            Samples(0.35f, 15);
            Samples(0f, 10);
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void HeldShoeDoesNotDropFromASmallSustainedBias()
        {
            Rise();
            float held = Read();
            Samples(-0.21f, 125);
            Samples(0.21f, 30);
            Samples(0f, 20);
            Assert.That(Read(), Is.EqualTo(held).Within(0.003f));
        }

        [Test]
        public void ALandingPeakConfirmsAWeakDownstrokeAfterCoasting()
        {
            Rise();
            for (int i = 0; i < 4; i++)
                Sample(-0.20f, 90);
            for (int i = 0; i < 4; i++)
                Sample(0f, 75);
            Sample(0.22f, 80);
            Sample(0.43f, 70);
            Sample(0.34f, 60);
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void SparseDownstrokeFollowedByLandingPlantsTheShoe()
        {
            Rise();
            Sample(-2f, 105);
            Sample(0f, 65);
            Sample(3.8f, 64);
            Sample(0f, 85);
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void DownstrokeWithASofterImpactAcrossSeveralReadingsPlantsTheShoe()
        {
            Rise();
            Sample(-2f, 70);
            Sample(0f, 60);
            Samples(1f, 4);
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void BrakingAfterTheEstimatedFloorDoesNotStartAnExtraLift()
        {
            Rise();
            Samples(-3.2f, 8);
            Samples(0f, 5); // Coasting downward can reach the estimated floor before the real one.
            Samples(3.2f, 8);
            Samples(0f, 20);
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void HeldShoeRejectsAlternatingNoiseAndAnIsolatedDownwardShock()
        {
            Rise();
            float held = Read();
            for (int i = 0; i < 50; i++)
            {
                Sample(-0.32f, 20);
                Sample(0.32f, 20);
            }
            Sample(-4f, 110);
            Samples(0f, 60);
            Assert.That(Read(), Is.EqualTo(held).Within(0.003f));
        }

        [Test]
        public void BrakingACappedLiftKeepsTheShoeRaised()
        {
            Samples(8f, 18);
            Samples(-8f, 18);
            Samples(0f, 100);
            Assert.That(Read(), Is.GreaterThan(0.32f));
        }

        [Test]
        public void DuplicateSamplesCannotKeepARaisedFootAvailable()
        {
            Rise();
            Assert.That(Read(), Is.GreaterThan(0.025f));
            for (int i = 0; i < 50; i++)
                foot.Accept("shoe-a", true, 0, time, Now + i * 0.02);
            Assert.That(foot.TryGetLift(Now + 1, out float height), Is.False);
            Assert.That(height, Is.Zero);
        }

        [Test]
        public void NewIdentityRequiresAFreshNeutralAndDoesNotInheritHeight()
        {
            Rise();
            Assert.That(Read(), Is.GreaterThan(0.025f));
            Samples(0, 1, "shoe-b");
            Assert.That(foot.TryGetLift(Now, out float height), Is.False);
            Assert.That(height, Is.Zero);
            Samples(0, 28, "shoe-b");
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void LossAndLongSourceGapRequireANewBaseline()
        {
            Rise();
            Assert.That(Read(), Is.GreaterThan(0.025f));
            foot.Accept("shoe-a", false, 0, time, Now);
            Assert.That(foot.TryGetLift(Now, out float lost), Is.False);
            Assert.That(lost, Is.Zero);
            time += 1000;
            Samples(0, 1);
            Assert.That(foot.TryGetLift(Now, out _), Is.False);
            Samples(0, 28);
            Assert.That(Read(), Is.Zero);
            time += 1000;
            Samples(2, 1);
            Assert.That(foot.TryGetLift(Now, out _), Is.False);
        }

        [Test]
        public void RecalibrationLearnsSmallSeatedBiasAndRejectsMotion()
        {
            Rise();
            foot.Calibrate();
            Samples(2.5f, 30);
            Assert.That(foot.TryGetLift(Now, out _), Is.False);
            Samples(0.32f, 30);
            Assert.That(Read(), Is.Zero);
            Samples(0.32f, 150);
            Assert.That(Read(), Is.Zero);
        }

        [Test]
        public void IsolatedShockDoesNotLiftAndSustainedAccelerationIsBounded()
        {
            Samples(12, 1);
            Samples(0, 30);
            Assert.That(Read(), Is.Zero);
            Samples(8, 70);
            Assert.That(Read(), Is.InRange(0.02f, 0.35f));
        }

        [Test]
        public void SparsePairOfReadingsCannotCalibrateAStationaryShoe()
        {
            foot = new FootLiftEstimator();
            foot.Accept("a", true, 0f, 0, 1);
            foot.Accept("a", true, 0f, 380, 1.38);
            Assert.That(foot.TryGetLift(1.38, out _), Is.False);
        }

        [Test]
        public void OneSlowPollContainingAShockCannotRaiseTheFoot()
        {
            time += 40;
            foot.Accept("shoe-a", true, 12f, time, Now);
            Samples(0, 40);
            Assert.That(Read(), Is.Zero);
        }

        private void Rise()
        {
            Samples(2.4f, 9);
            Samples(-2.4f, 9);
            Samples(0, 14);
        }

        private void Samples(float acceleration, int count, string id = "shoe-a")
        {
            for (int i = 0; i < count; i++)
                Sample(acceleration, 20, id);
        }

        private void Sample(float acceleration, int milliseconds, string id = "shoe-a")
        {
            time += milliseconds;
            foot.Accept(id, true, acceleration, time, Now);
        }

        private float Read()
        {
            Assert.That(foot.TryGetLift(Now, out float height), Is.True, "Expected a fresh calibrated shoe");
            return height;
        }
    }
}
