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
            {
                time += 20;
                foot.Accept(id, true, acceleration, time, Now);
            }
        }

        private float Read()
        {
            Assert.That(foot.TryGetLift(Now, out float height), Is.True, "Expected a fresh calibrated shoe");
            return height;
        }
    }
}
