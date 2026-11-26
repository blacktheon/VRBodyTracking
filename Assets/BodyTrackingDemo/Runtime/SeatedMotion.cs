using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Blacktheon.BodyTracking.Demo.EditMode")]
[assembly: InternalsVisibleTo("Blacktheon.BodyTracking.Demo.PlayMode")]

namespace Blacktheon.BodyTracking.Demo
{
    internal static class SeatedMotion
    {
        internal const float StandingEyeHeight = 1.65f;
        internal const float SittingEyeHeight = 1.20f;
        internal const float ShoeCenterHeight = 0.055f;

        // The three virtual chairs face one another. Physical tracking-space origins can differ.
        internal static Quaternion Place(Transform rig, Transform head, int slot)
        {
            var heading = Quaternion.Euler(0, slot == 2 ? 120 : slot == 3 ? -120 : 0, 0);
            float turn = Vector3.SignedAngle(Vector3.ProjectOnPlane(head.forward, Vector3.up),
                heading * Vector3.forward, Vector3.up);
            rig.RotateAround(head.position, Vector3.up, turn);
            Vector3 seat = heading * (Vector3.back * 1.85f);
            rig.position += Vector3.ProjectOnPlane(seat - head.position, Vector3.up);
            return heading;
        }

        internal static void Move(Transform rig, Transform head, ref Quaternion heading,
            Vector2 stick, float turnDegrees, float distance)
        {
            rig.RotateAround(head.position, Vector3.up, turnDegrees);
            heading = Quaternion.AngleAxis(turnDegrees, Vector3.up) * heading;
            stick = Vector2.ClampMagnitude(stick, 1);
            Vector3 delta = heading * new Vector3(stick.x, 0, stick.y) * distance;
            Vector3 next = head.position + delta;
            delta.x = Mathf.Clamp(next.x, -4.5f, 4.5f) - head.position.x;
            delta.z = Mathf.Clamp(next.z, -4.5f, 4.5f) - head.position.z;
            rig.position += delta;
        }

        internal static Pose Foot(Vector3 floorUnderHead, Quaternion heading, bool left, float lift, bool sitting)
        {
            lift = float.IsFinite(lift) ? Mathf.Clamp(lift, 0, 0.65f) : 0;
            Vector3 rest = heading * new Vector3(left ? -0.16f : 0.16f, 0, sitting ? 0.32f : 0.06f);
            return new Pose(floorUnderHead + rest + Vector3.up * (ShoeCenterHeight + lift), heading);
        }

        internal static Vector3 Knee(Vector3 hip, Vector3 ankle, Quaternion heading)
        {
            // Two equal leg segments meet in the forward-facing knee plane.
            const float segmentLength = 0.50f;
            Vector3 span = ankle - hip;
            Vector3 forward = Vector3.ProjectOnPlane(heading * Vector3.forward, span).normalized;
            float bend = Mathf.Sqrt(Mathf.Max(0f, segmentLength * segmentLength - 0.25f * span.sqrMagnitude));
            return (hip + ankle) * 0.5f + forward * bend;
        }
    }
}
