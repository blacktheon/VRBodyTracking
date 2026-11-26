using System;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo
{
    public sealed class ClimbLadder : MonoBehaviour
    {
        [SerializeField] Transform vehicleRoot;
        [SerializeField] Transform ladderRoot;
        [SerializeField] Transform topBar;
        [Tooltip("Horizontal rung colliders in order from the floor to the roof.")]
        [SerializeField] CapsuleCollider[] rungs = Array.Empty<CapsuleCollider>();

        public Transform VehicleRoot => vehicleRoot;
        public Transform LadderRoot => ladderRoot;
        public Transform TopBar => topBar;
        public int RungCount => rungs.Length;
        public Vector3 FaceDirection => -ladderRoot.forward;

        public int FindRung(Vector3 handWorld, float reach)
        {
            if (reach < 0f) return 0;
            float bestDistance = reach * reach;
            int nearest = 0;
            for (int i = 0; i < rungs.Length; i++)
            {
                var bar = rungs[i];
                if (bar == null || !bar.enabled || !bar.gameObject.activeInHierarchy) continue;
                Vector3 center = bar.transform.TransformPoint(bar.center);
                Vector3 half = HalfSpan(bar);
                float along = half.sqrMagnitude > 0f
                    ? Mathf.Clamp(Vector3.Dot(handWorld - center, half) / half.sqrMagnitude, -1f, 1f)
                    : 0f;
                float distance = (handWorld - (center + along * half)).sqrMagnitude;
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    nearest = i + 1;
                }
            }
            return nearest;
        }

        public Vector3 RungPoint(int rung, bool left)
        {
            if (rungs.Length == 0) return transform.position;
            var bar = rungs[Mathf.Clamp(rung, 1, rungs.Length) - 1];
            Vector3 half = HalfSpan(bar);
            Vector3 climberRight = Vector3.Cross(ladderRoot.up, FaceDirection);
            if (Vector3.Dot(half, climberRight) < 0f) half = -half;
            Vector3 point = bar.transform.TransformPoint(bar.center) + half * (left ? -0.5f : 0.5f);
            if (rung > 0) return point;

            // Rung zero stands on the common scene floor below the first bar.
            Vector3 floor = transform.InverseTransformPoint(point);
            floor.y = 0f;
            return transform.TransformPoint(floor);
        }

        static Vector3 HalfSpan(CapsuleCollider bar)
        {
            Vector3 axis = bar.direction == 0 ? Vector3.right : bar.direction == 1 ? Vector3.up : Vector3.forward;
            return bar.transform.TransformVector(axis * (bar.height * 0.5f));
        }
    }
}
