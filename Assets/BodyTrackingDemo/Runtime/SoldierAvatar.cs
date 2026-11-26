using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blacktheon.BodyTracking.Demo
{
    // The prefab stands on its root's floor plane and faces +Z. Bone axes come from its bind pose.
    internal sealed class SoldierAvatar
    {
        // The imported hand bone sits inside the distal forearm; the palm heel is a fifth of the way to the knuckles.
        internal const float PalmHeelFraction = 0.20f;
        readonly Transform model, head;
        readonly Vector3 restHead, headToEyes;
        readonly Quaternion headRotation;
        readonly Limb[] legs, arms;

        SoldierAvatar(Transform model, Transform head, Vector3 headToEyes, Limb[] legs, Limb[] arms)
        {
            this.model = model;
            this.head = head;
            this.headToEyes = headToEyes;
            this.legs = legs;
            this.arms = arms;
            Quaternion inverse = Quaternion.Inverse(model.rotation);
            restHead = inverse * (head.position - model.position);
            headRotation = inverse * head.rotation;
        }

        internal static SoldierAvatar Load(Transform parent, SoldierModel model, bool own)
        {
            string path = model == SoldierModel.Armored ? "BodyTrackingCharacters/SoldierArmored" : "BodyTrackingCharacters/SoldierUniform";
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return null;
            var instance = Object.Instantiate(prefab, parent, false);
            var soldier = Bind(instance.transform, own);
            if (soldier == null)
            {
                Debug.LogWarning("Missing soldier skeleton in " + path + ". Using the standard seat avatar.");
                instance.SetActive(false);
                Object.Destroy(instance);
            }
            return soldier;
        }

        internal static SoldierAvatar Bind(Transform model, bool own)
        {
            var pelvis = Array.Find(model.GetComponentsInChildren<Transform>(true),
                joint => string.Equals(joint.name, "pelvis", StringComparison.OrdinalIgnoreCase));
            if (pelvis == null) return null;
            var bones = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var bone in pelvis.GetComponentsInChildren<Transform>(true))
                bones.TryAdd(bone.name, bone);
            Transform Bone(string name) => bones.TryGetValue(name, out var bone) ? bone : null;
            Transform head = Bone("head");
            if (head == null) return null;

            Quaternion inverse = Quaternion.Inverse(model.rotation);
            Transform eyeL = Bone("eye_l"), eyeR = Bone("eye_r");
            Vector3 headToEyes = eyeL != null && eyeR != null
                ? inverse * ((eyeL.position + eyeR.position) * 0.5f - head.position)
                : new Vector3(0, 0.09f, 0.075f);
            float eyeHeight = (inverse * (head.position - model.position)).y + headToEyes.y;
            if (eyeHeight <= 0.01f) return null;
            float scale = SeatedMotion.StandingEyeHeight / eyeHeight;
            model.localScale *= scale;

            var legs = new Limb[2];
            var arms = new Limb[2];
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "_l" : "_r";
                legs[i] = Limb.Bind(model, Bone("thigh" + side), Bone("calf" + side), Bone("foot" + side), Bone("ball" + side), true);
                arms[i] = Limb.Bind(model, Bone("upperarm" + side), Bone("lowerarm" + side), Bone("hand" + side),
                    Bone("middle_01" + side), false, i == 0, Bone("index_01" + side), Bone("pinky_01" + side));
                if (legs[i] == null || arms[i] == null) return null;
            }
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.name != "Shirt" && skin.name != "Hands") continue;
                if (!SoldierWristSkin.TryFit(skin, Bone) && skin.name == "Shirt")
                    foreach (var arm in arms) arm.BindRigidCuff(skin);
            }
            foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            if (own)
            {
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    for (var part = renderer.transform; part != model && part != null; part = part.parent)
                        if (part.name == "Face" || part.name == "Eyes" || part.name == "Headgear" || part.name == "Head")
                        {
                            renderer.enabled = false;
                            break;
                        }
            }
            return new SoldierAvatar(model, head, headToEyes * scale, legs, arms);
        }

        internal void Draw(AvatarPose pose)
        {
            Quaternion body = pose.LeftFoot.rotation;
            Vector3 skull = pose.Head.position - pose.Head.rotation * headToEyes;
            model.SetPositionAndRotation(skull - body * restHead, body);
            head.rotation = pose.Head.rotation * headRotation;
            for (int i = 0; i < 2; i++)
            {
                legs[i].Place(i == 0 ? pose.LeftFoot : pose.RightFoot, body);
                arms[i].Place(i == 0 ? pose.LeftHand : pose.RightHand, body);
            }
        }

        sealed class Limb
        {
            readonly Transform upper, lower, end;
            readonly Quaternion upperRotation, lowerRotation, endRotation;
            readonly Vector3 upperDirection, lowerDirection, targetOffset, elbowPole, heelToHandBone;
            readonly float upperLength, lowerLength;
            readonly bool leg;
            Transform cuff;
            readonly Vector3 cuffToHeel;

            internal static Limb Bind(Transform model, Transform upper, Transform lower, Transform end, Transform tip,
                bool leg, bool left = false, Transform index = null, Transform pinky = null)
            {
                if (upper == null || lower == null || end == null || !lower.IsChildOf(upper) || !end.IsChildOf(lower)
                    || Vector3.Distance(upper.position, lower.position) < 0.001f
                    || Vector3.Distance(lower.position, end.position) < 0.001f) return null;
                return new Limb(model, upper, lower, end, tip, leg, left, index, pinky);
            }

            Limb(Transform model, Transform upper, Transform lower, Transform end, Transform tip,
                bool leg, bool left, Transform index, Transform pinky)
            {
                this.upper = upper;
                this.lower = lower;
                this.end = end;
                this.leg = leg;
                Quaternion inverse = Quaternion.Inverse(model.rotation);
                upperDirection = inverse * (lower.position - upper.position);
                lowerDirection = inverse * (end.position - lower.position);
                upperLength = upperDirection.magnitude;
                if (leg)
                {
                    upperRotation = inverse * upper.rotation;
                    lowerRotation = inverse * lower.rotation;
                    // Network feet are shoe centers. Preserve the boot's bind-pose ankle height and toe direction.
                    float ankleHeight = (inverse * (end.position - model.position)).y;
                    Vector3 toe = tip != null ? inverse * (tip.position - end.position) : Vector3.forward * 0.14f;
                    targetOffset = Vector3.up * (ankleHeight - SeatedMotion.ShoeCenterHeight) - 0.5f * Vector3.ProjectOnPlane(toe, Vector3.up);
                    endRotation = inverse * end.rotation;
                }
                else
                {
                    Vector3 fingers = tip != null ? tip.position - end.position : Vector3.zero;
                    if (fingers.sqrMagnitude < 0.000001f) fingers = end.position - lower.position;
                    Vector3 thumbSide = index && pinky ? Vector3.ProjectOnPlane(index.position - pinky.position, fingers) : Vector3.zero;
                    bool hasPalm = tip && thumbSide.sqrMagnitude > 0.000001f;
                    Quaternion palm = Quaternion.LookRotation(fingers, hasPalm ? thumbSide : model.up);
                    endRotation = Quaternion.Inverse(palm) * end.rotation;
                    if (hasPalm)
                    {
                        Vector3 heel = Vector3.Lerp(end.position, tip.position, PalmHeelFraction);
                        Vector3 grip = (end.position + tip.position) * 0.5f;
                        lowerDirection = inverse * (heel - lower.position);
                        targetOffset = Quaternion.Inverse(palm) * (heel - grip);
                        heelToHandBone = Quaternion.Inverse(palm) * (end.position - heel);
                        cuffToHeel = end.InverseTransformPoint(heel);
                    }
                    elbowPole = new Vector3(left ? -1 : 1, -0.65f, -0.5f);
                    Vector3 hinge = Vector3.Cross(upperDirection.normalized, lowerDirection.normalized);
                    if (hinge.sqrMagnitude < 0.0001f)
                        hinge = Vector3.Cross(ArmBend(upperDirection.normalized, elbowPole, Vector3.forward), upperDirection);
                    upperRotation = Quaternion.Inverse(Quaternion.LookRotation(upperDirection, hinge)) * inverse * upper.rotation;
                    lowerRotation = Quaternion.Inverse(Quaternion.LookRotation(lowerDirection, hinge)) * inverse * lower.rotation;
                }
                lowerLength = lowerDirection.magnitude;
            }

            static Vector3 ArmBend(Vector3 axis, Vector3 pole, Vector3 fallback)
            {
                Vector3 bend = Vector3.ProjectOnPlane(pole, axis);
                return bend.sqrMagnitude < 0.00001f ? Vector3.ProjectOnPlane(fallback, axis) : bend;
            }

            internal void BindRigidCuff(SkinnedMeshRenderer shirt)
            {
                // On a non-readable mesh the sleeve can still follow the forearm through its wrist slot.
                var bones = shirt.bones;
                int wrist = Array.IndexOf(bones, end);
                if (wrist < 0) return;
                if (cuff == null)
                {
                    cuff = new GameObject(end.name + " cuff").transform;
                    cuff.SetParent(end.parent, false);
                    cuff.SetLocalPositionAndRotation(end.localPosition, end.localRotation);
                    cuff.localScale = end.localScale;
                }
                bones[wrist] = cuff;
                shirt.bones = bones;
            }

            internal void Place(Pose target, Quaternion body)
            {
                Vector3 goal = target.position + target.rotation * targetOffset;
                Vector3 start = upper.position;
                Vector3 span = goal - start;
                float distance = span.magnitude;
                Vector3 axis = distance > 0.0001f ? span / distance : body * Vector3.down;
                Vector3 bend = leg ? Vector3.Cross(axis, body * Vector3.right)
                    : ArmBend(axis, body * elbowPole, body * Vector3.forward);
                Vector3 joint;
                if (distance >= upperLength + lowerLength)
                {
                    // A tracked wrist or planted shoe can exceed the model's reach when the player leans.
                    joint = start + span * (upperLength / (upperLength + lowerLength));
                }
                else
                {
                    if (bend.sqrMagnitude < 0.00001f) bend = Vector3.ProjectOnPlane(body * Vector3.forward, axis);
                    float along = distance > 0.0001f ? Mathf.Clamp((upperLength * upperLength - lowerLength * lowerLength
                        + distance * distance) / (2 * distance), -upperLength, upperLength) : 0;
                    float height = Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
                    joint = start + axis * along + bend.normalized * height;
                }
                if (leg)
                {
                    upper.rotation = Quaternion.FromToRotation(body * upperDirection, joint - start) * body * upperRotation;
                    lower.SetPositionAndRotation(joint,
                        Quaternion.FromToRotation(body * lowerDirection, goal - joint) * body * lowerRotation);
                }
                else
                {
                    // The elbow hinge also fixes forearm roll when the arm is fully extended.
                    Vector3 hinge = Vector3.Cross(bend, axis);
                    upper.rotation = Quaternion.LookRotation(joint - start, hinge) * upperRotation;
                    lower.SetPositionAndRotation(joint, Quaternion.LookRotation(goal - joint, hinge) * lowerRotation);
                }
                end.SetPositionAndRotation(goal + target.rotation * heelToHandBone, target.rotation * endRotation);
                if (cuff) cuff.position = goal - cuff.TransformVector(cuffToHeel);
            }
        }
    }
}
