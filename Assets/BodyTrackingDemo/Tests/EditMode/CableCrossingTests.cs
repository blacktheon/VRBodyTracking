using NUnit.Framework;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class CableCrossingTests
    {
        CableCrossing crossing;
        double now;
        [SetUp] public void StartFlat() { crossing = new CableCrossing(); now = 0; }

        CableResult At(float depth, float left = 0, float right = 0, byte fresh = 3, float x = 0)
        {
            now += 0.02;
            return crossing.Sample(new Vector2(x, depth + CableCrossing.DepthAt(x)), fresh, left, right, now);
        }

        [TestCase(1, true)] [TestCase(-1, false)]
        public void IndependentLiftsCountInEitherOrderAndTravelDirection(int direction, bool leftFirst)
        {
            At(-direction);
            At(-0.5f * direction, leftFirst ? 0.1f : 0, leftFirst ? 0 : 0.1f);
            At(-0.3f * direction);
            At(-0.2f * direction, leftFirst ? 0 : 0.1f, leftFirst ? 0.1f : 0);
            Assert.That(At(-0.1f * direction), Is.EqualTo(CableResult.None));
            Assert.That(At(0.18f * direction), Is.EqualTo(CableResult.Passed));
            Assert.That(crossing.Feedback(now + 0.999), Is.EqualTo(CableResult.Passed));
            Assert.That(crossing.Feedback(now + 1), Is.EqualTo(CableResult.None));
        }

        [TestCase(0f, 0f, 3)] [TestCase(0.12f, 0f, 3)] [TestCase(0f, 0.12f, 3)]
        [TestCase(0.079f, 0.079f, 3)] [TestCase(0.15f, 0.15f, 0)]
        public void CrossingWithoutBothTrackedLiftsFailsForFiveSeconds(float left, float right, int fresh)
        {
            At(-0.9f);
            At(-0.25f, left, right, (byte)fresh);
            Assert.That(At(0.2f), Is.EqualTo(CableResult.Missed));
            double finish = now;
            Assert.That(At(-0.3f, 0.2f, 0.2f), Is.EqualTo(CableResult.Missed));
            Assert.That(crossing.Feedback(finish + 4.999), Is.EqualTo(CableResult.Missed));
            Assert.That(crossing.Feedback(finish + 5), Is.EqualTo(CableResult.None));
        }

        [Test]
        public void HoldingFeetUpFromOutsideOrRetreatingRequiresNewLifts()
        {
            At(-1.5f);
            At(-1.1f, 0.18f, 0.18f);
            At(-0.4f, 0.18f, 0.18f);
            Assert.That(At(0.18f, 0.18f, 0.18f), Is.EqualTo(CableResult.Missed));
            now += 5;
            At(0.9f);
            At(0.5f, 0.1f, 0.12f);
            At(1.1f);
            At(0.2f);
            Assert.That(At(-0.2f), Is.EqualTo(CableResult.Missed));
        }

        [Test]
        public void TrackerDropOrRecalibrationClearsEvidence()
        {
            At(-1);
            At(-0.3f, 0.12f, 0.12f);
            At(-0.2f, right: float.NaN);
            Assert.That(At(0.2f), Is.EqualTo(CableResult.Missed));
            now += 5;
            At(0.5f);
            At(0.3f, 0.12f, 0.12f);
            crossing.ResetApproach();
            At(0.2f);
            Assert.That(At(-0.2f), Is.EqualTo(CableResult.Missed));
        }

        [Test]
        public void WalkingAroundAnEndAndJitteringOnTheLineDoNotScore()
        {
            At(-1);
            At(-0.4f, 0.15f, 0.15f, x: 2);
            At(-0.4f, x: 2.5f);
            Assert.That(At(0.4f, x: 2.5f), Is.EqualTo(CableResult.None));
            Assert.That(At(0.4f, x: 2), Is.EqualTo(CableResult.None));
            Assert.That(At(0.02f, x: 2), Is.EqualTo(CableResult.None));
            Assert.That(At(-0.02f, x: 2), Is.EqualTo(CableResult.None));
            Assert.That(At(0.25f, x: 2), Is.EqualTo(CableResult.None));
            Assert.That(At(-0.25f, x: 2), Is.EqualTo(CableResult.Missed));
        }
    }
}
