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

        private enum Motion { Grounded, Rising, Held, Falling }

        private readonly float threshold;
        private readonly float maxHeight;
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
        private bool braked;
        private bool onsetArmed;

        public FootLiftEstimator(float motionThreshold = 0.7f, float maximumLift = 0.35f)
        {
            threshold = Mathf.Clamp(motionThreshold, 0.25f, 4f);
            maxHeight = Mathf.Clamp(maximumLift, 0.03f, 0.75f);
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
                // Let the first gesture finish braking before its opposite can be interpreted as a new gesture.
                if (!onsetArmed && quietTime >= 0.06f)
                    onsetArmed = true;
                float direction = motion == Motion.Grounded ? 1f : -1f;
                if (!onsetArmed || direction * acceleration < threshold)
                {
                    onsetSpeed = onsetTime = 0f;
                    onsetSamples = 0;
                    return;
                }

                onsetTime += dt;
                onsetSamples++;
                onsetSpeed += acceleration * dt;
                if (onsetSamples < 2 || onsetTime < OnsetSeconds)
                    return;

                motion = direction > 0f ? Motion.Rising : Motion.Falling;
                velocity = Mathf.Clamp(onsetSpeed, -MaxSpeed, MaxSpeed);
                height = Mathf.Clamp(height + 0.5f * velocity * onsetTime, 0f, maxHeight);
                motionTime = onsetTime;
                quietTime = 0f;
                braked = false;
                return;
            }

            float sign = motion == Motion.Rising ? 1f : -1f;
            braked |= sign * acceleration < -threshold * 0.6f;
            float nextVelocity = Mathf.Clamp(velocity + acceleration * dt, -MaxSpeed, MaxSpeed);
            height = Mathf.Clamp(height + 0.5f * (velocity + nextVelocity) * dt, 0f, maxHeight);
            velocity = nextVelocity;

            bool stopped = (braked && sign * velocity <= 0.025f) ||
                           quietTime >= (braked ? 0.10f : 0.25f) || motionTime >= 0.9f;
            if (motion == Motion.Rising && (stopped || height >= maxHeight))
                Rest(height >= 0.012f ? Motion.Held : Motion.Grounded);
            else if (motion == Motion.Falling && (stopped || height <= 0.003f))
                Rest(Motion.Grounded);
        }

        private void Rest(Motion resting)
        {
            motion = resting;
            if (resting == Motion.Grounded)
                height = 0f;
            velocity = 0f;
            motionTime = 0f;
            onsetTime = 0f;
            onsetSamples = 0;
            onsetSpeed = 0f;
            braked = false;
            // A landing may still have positive acceleration. Wait for it to settle before another lift.
            onsetArmed = !calibrated || quietTime >= 0.06f;
            quietTime = 0f;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
