using UnityEngine;
using UnityEngine.Rendering;

namespace Blacktheon.BodyTracking.Demo
{
    public sealed class StepOverCableMission : MonoBehaviour
    {
        readonly CableCrossing crossing = new();
        MeshRenderer overlay;
        Material tint;
        CableResult cableResult, shown;

        internal void BeginLocal(Transform head)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Cable feedback";
            quad.transform.SetParent(head, false);
            quad.transform.localPosition = Vector3.forward * 0.7f;
            var collider = quad.GetComponent<MeshCollider>();
            collider.enabled = false;
            Destroy(collider);
            overlay = quad.GetComponent<MeshRenderer>();
            overlay.enabled = false;
            overlay.shadowCastingMode = ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            overlay.allowOcclusionWhenDynamic = false;
            tint = new Material(Resources.Load<Shader>("CableFeedback"));
            overlay.sharedMaterial = tint;
        }

        internal void ResetApproach() => crossing.ResetApproach();

        internal void Tick(Vector3 floor, byte freshFeet, float left, float right, bool playing, double now,
            LadderResult ladderFeedback = LadderResult.None)
        {
            if (overlay == null) return;
            if (!playing) crossing.ResetApproach();
            Vector3 local = transform.InverseTransformPoint(floor);
            cableResult = playing ? crossing.Sample(new Vector2(local.x, local.z), freshFeet, left, right, now)
                                  : crossing.Feedback(now);
            var next = ladderFeedback == LadderResult.Fell ? CableResult.Missed :
                ladderFeedback == LadderResult.Legal ? CableResult.Passed : cableResult;
            if (next == shown) return;
            shown = next;
            overlay.enabled = shown != CableResult.None;
            if (overlay.enabled) tint.color = shown == CableResult.Passed
                ? new Color(0.015f, 0.95f, 0.12f, 0.8f) : new Color(0.96f, 0.012f, 0.022f, 0.8f);
        }

        internal string Cue
        {
            get
            {
                if (cableResult == CableResult.Passed) return "Cable cleared";
                if (cableResult == CableResult.Missed) return "Cable missed";
                return crossing.LiftedFeet switch
                {
                    1 => "Left counted. Lift your right foot.",
                    2 => "Right counted. Lift your left foot.",
                    3 => "Both counted. Cross the cable.",
                    _ => "Lift each foot near the orange cable."
                };
            }
        }

        void OnDestroy()
        {
            if (overlay != null) Destroy(overlay.gameObject);
            if (tint != null) Destroy(tint);
        }
    }
}
