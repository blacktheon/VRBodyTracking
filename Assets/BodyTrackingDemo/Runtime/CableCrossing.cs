using UnityEngine;

namespace Blacktheon.BodyTracking.Demo
{
    internal enum CableResult { None, Passed, Missed }

    // One instance belongs to one local player. Coordinates are metres in the cable's XZ plane.
    internal sealed class CableCrossing
    {
        internal const float HalfLength = 2.05f;
        const float ApproachDistance = 0.75f, FarSideClearance = 0.15f;
        const float MinimumLift = 0.08f, GroundedHeight = 0.035f;
        int approachSide, groundedFeet, liftedFeet;
        Vector2 previous;
        bool hasPrevious;
        CableResult result;
        double feedbackUntil;

        // The insulated cord bows gently between its two ends.
        internal static float DepthAt(float x)
        {
            float t = Mathf.Clamp(x / HalfLength, -1, 1);
            return 0.16f * (1 - t * t);
        }

        internal CableResult Feedback(double now) => now < feedbackUntil ? result : CableResult.None;
        internal int LiftedFeet => liftedFeet;

        internal void ResetApproach()
        {
            hasPrevious = false;
            approachSide = groundedFeet = liftedFeet = 0;
        }

        int ReadFoot(int bit, bool fresh, float height)
        {
            if (!fresh || !float.IsFinite(height))
            {
                groundedFeet &= ~bit;
                liftedFeet &= ~bit;
            }
            else if (height <= GroundedHeight)
                groundedFeet |= bit;
            else if (height >= MinimumLift && (groundedFeet & bit) != 0)
            {
                groundedFeet &= ~bit;
                return bit;
            }
            return 0;
        }

        internal CableResult Sample(Vector2 position, byte freshFeet, float left, float right, double now)
        {
            int raised = ReadFoot(1, (freshFeet & 1) != 0, left) | ReadFoot(2, (freshFeet & 2) != 0, right);
            Vector2 start = hasPrevious ? previous : position;
            previous = position;
            hasPrevious = true;

            // Clip the travelled segment at the cable ends. Walking round a plug starts a new approach.
            float xMin = Mathf.Min(start.x, position.x), xMax = Mathf.Max(start.x, position.x);
            if (xMin <= HalfLength && xMax >= -HalfLength)
            {
                if (Mathf.Abs(start.x) > HalfLength)
                    Observe(AtEnd(start, position, start.x), 0, now);
                if (Mathf.Abs(position.x) <= HalfLength)
                    Observe(position, raised, now);
                else
                    Observe(AtEnd(start, position, position.x), 0, now);
            }
            if (Mathf.Abs(position.x) > HalfLength)
                approachSide = liftedFeet = 0;
            return Feedback(now);
        }

        static Vector2 AtEnd(Vector2 start, Vector2 end, float outsideX)
        {
            float x = Mathf.Sign(outsideX) * HalfLength;
            return Vector2.Lerp(start, end, (x - start.x) / (end.x - start.x));
        }

        void Observe(Vector2 point, int raised, double now)
        {
            float depth = point.y - DepthAt(point.x);
            int side = Mathf.Abs(depth) < FarSideClearance ? 0 : depth > 0 ? 1 : -1;
            if (Feedback(now) != CableResult.None)
            {
                approachSide = side;
                liftedFeet = 0;
                return;
            }
            if (approachSide == 0) approachSide = side;
            bool near = Mathf.Abs(depth) <= ApproachDistance;
            if (near && approachSide != 0) liftedFeet |= raised;
            if (side != 0 && side != approachSide)
            {
                result = liftedFeet == 3 ? CableResult.Passed : CableResult.Missed;
                feedbackUntil = now + (result == CableResult.Passed ? 1d : 5d);
                approachSide = side;
                liftedFeet = 0;
            }
            if (!near) liftedFeet = 0;
        }
    }
}
