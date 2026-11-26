using UnityEngine;

namespace Blacktheon.BodyTracking
{
    // Integrate individual seated movement events. Quiet acceleration on its own has no height information.
    internal sealed class FootLiftEstimator
    {
        internal const double FreshSeconds = 0.40;
        private const float NeutralSeconds = 0.35f;
        private const float OnsetSeconds = 0.028f;
        private const float Deadband = 0.16f;
        private const float MaxSpeed = 1.5f;
        private const float LandingWindow = 0.55f;

        private enum Motion { Grounded, Rising, Held, Falling }

        private readonly float threshold;
        private readonly float maxHeight;
        private readonly float loweringThreshold;
        private readonly float minimumLoweringImpulse;
        private string identity;
        private long lastSource;
        private double lastReceipt;
        private bool hasSample;
        private bool calibrated;
        private float neutralTime;
        private int neutralSamples;
        private float bias;
        private Motion motion;
        private float height;
        private float velocity;
        private float motionTime;
        private float quietTime;
        private float onsetTime;
        private int onsetSamples;
        private float onsetSpeed;
        private float onsetGap;
        private float landingImpulse;
        private bool hasMotionPeak;
        private bool braked;
        private bool liftArmed;

        public FootLiftEstimator(float motionThreshold = 0.7f, float maximumLift = 0.35f)
        {
            threshold = Mathf.Clamp(motionThreshold, 0.25f, 4f);
            maxHeight = Mathf.Clamp(maximumLift, 0.03f, 0.75f);
            loweringThreshold = Mathf.Max(0.24f, threshold * 0.4f);
            minimumLoweringImpulse = Mathf.Max(0.05f, threshold * 0.08f);
        }

        public void Accept(string id, bool valid, float acceleration, long sourceMs, double receivedAt)
        {
            if (!valid || string.IsNullOrEmpty(id) || !IsFinite(acceleration))
            {
                Invalidate();
                return;
            }

            if (id != identity)
            {
                identity = id;
                hasSample = false;
                Calibrate();
            }

            if (hasSample && sourceMs <= lastSource)
                return;

            double sourceStep = hasSample ? ((double)sourceMs - lastSource) * 0.001 : 0;
            if (sourceStep > FreshSeconds || receivedAt - lastReceipt > FreshSeconds)
            {
                Calibrate();
                sourceStep = 0;
            }

            hasSample = true;
            lastSource = sourceMs;
            lastReceipt = receivedAt;
            float dt = (float)sourceStep;

            if (!calibrated)
            {
                LearnNeutral(acceleration, dt);
                return;
            }

            float corrected = Mathf.Clamp(acceleration - bias, -18f, 18f);
            Advance(Mathf.Abs(corrected) < Deadband ? 0f : corrected, dt);
        }

        public bool TryGetLift(double now, out float metres)
        {
            bool available = calibrated && hasSample && now - lastReceipt <= FreshSeconds;
            metres = available ? height : 0f;
            return available;
        }

        public bool HasFreshSample(double now) => hasSample && now - lastReceipt <= FreshSeconds;

        public void Invalidate()
        {
            hasSample = false;
            Calibrate();
        }

        public void Calibrate()
        {
            calibrated = false;
            neutralTime = 0f;
            neutralSamples = 0;
            bias = 0f;
            Rest(Motion.Grounded);
        }

        private void LearnNeutral(float acceleration, float dt)
        {
            if (Mathf.Abs(acceleration) > 0.70f)
            {
                neutralTime = 0f;
                neutralSamples = 0;
                bias = 0f;
                return;
            }

            if (dt <= 0f)
                return;
            neutralTime += dt;
            neutralSamples++;
            bias += (acceleration - bias) * dt / neutralTime;
            calibrated = neutralSamples >= 6 && neutralTime >= NeutralSeconds;
        }

        private void Advance(float acceleration, float dt)
        {
            motionTime += dt;
            quietTime = acceleration == 0f ? quietTime + dt : 0f;

            if (motion == Motion.Grounded || motion == Motion.Held)
            {
                DetectMotion(acceleration, dt);
                return;
            }

            float sign = motion == Motion.Rising ? 1f : -1f;
            braked |= sign * acceleration < -(motion == Motion.Rising ? threshold * 0.6f : loweringThreshold);
            // Bound the displayed speed while retaining the impulse needed to brake a fast lift.
            float nextVelocity = velocity + acceleration * dt;
            float meanSpeed = Mathf.Clamp(0.5f * (velocity + nextVelocity), -MaxSpeed, MaxSpeed);
            height = Mathf.Clamp(height + meanSpeed * dt, 0f, maxHeight);
            velocity = nextVelocity;

            bool stopped = (braked && sign * velocity <= 0.025f) ||
                           quietTime >= (braked ? 0.10f : 0.25f) || motionTime >= 0.9f;
            // The displayed foot can reach the floor early. Finish the downstroke before a landing
            // impulse can be mistaken for a second lift.
            if (stopped)
                Rest(motion == Motion.Rising && height >= 0.012f ? Motion.Held : Motion.Grounded);
        }

        private void DetectMotion(float acceleration, float dt)
        {
            bool lowering = motion == Motion.Held;
            if (!lowering && !liftArmed)
            {
                liftArmed = quietTime >= 0.06f;
                return;
            }

            if (lowering && onsetSamples > 0)
            {
                onsetGap += dt;
                if (onsetGap > LandingWindow)
                    ClearOnset();
            }
            bool moving = lowering ? -acceleration >= Deadband : acceleration >= threshold;
            if (!moving)
            {
                // A slow receiver may capture a single downstroke. Opposing impulse confirms
                // contact, even if the landing spans several readings or follows a brief coast.
                if (lowering && -onsetSpeed >= minimumLoweringImpulse)
                {
                    hasMotionPeak |= acceleration >= loweringThreshold;
                    landingImpulse += Mathf.Max(acceleration, 0f) * dt;
                    if (hasMotionPeak && landingImpulse >= minimumLoweringImpulse)
                        Rest(Motion.Grounded);
                }
                else if (!lowering || acceleration > 0f)
                    ClearOnset();
                return;
            }

            onsetGap = 0f;
            landingImpulse = 0f;
            onsetTime += dt;
            onsetSamples++;
            onsetSpeed += acceleration * dt;
            if (lowering)
            {
                hasMotionPeak |= -acceleration >= loweringThreshold;
                // Keep the gentle start of a downstroke. A small sustained bias alone cannot
                // trigger movement or accumulate an arbitrarily large initial velocity.
                if (!hasMotionPeak)
                {
                    onsetSpeed = Mathf.Max(onsetSpeed, -minimumLoweringImpulse);
                    onsetTime = Mathf.Min(onsetTime, LandingWindow);
                    return;
                }
                if (-onsetSpeed < minimumLoweringImpulse)
                    return;
            }
            if (onsetSamples < 2 || onsetTime < OnsetSeconds)
                return;

            motion = lowering ? Motion.Falling : Motion.Rising;
            velocity = onsetSpeed;
            height = Mathf.Clamp(height + 0.5f * Mathf.Clamp(velocity, -MaxSpeed, MaxSpeed) * onsetTime, 0f, maxHeight);
            motionTime = onsetTime;
            quietTime = 0f;
            braked = false;
        }

        private void ClearOnset()
        {
            onsetGap = onsetTime = onsetSpeed = landingImpulse = 0f;
            onsetSamples = 0;
            hasMotionPeak = false;
        }

        private void Rest(Motion resting)
        {
            motion = resting;
            if (resting == Motion.Grounded)
                height = 0f;
            velocity = 0f;
            motionTime = 0f;
            ClearOnset();
            braked = false;
            // Only a landing needs a quiet interval to distinguish a new lift from its impact.
            liftArmed = !calibrated;
            quietTime = 0f;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
