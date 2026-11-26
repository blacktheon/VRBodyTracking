using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blacktheon.BodyTracking.Demo
{
    // The prefab stands on its root's floor plane and faces +Z. Bone axes come from its bind pose.
    internal sealed class SoldierAvatar
    {
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
                arms[i] = Limb.Bind(model, Bone("upperarm" + side), Bone("lowerarm" + side), Bone("hand" + side), Bone("middle_01" + side), false);
                if (legs[i] == null || arms[i] == null) return null;
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
                legs[i].Place(i == 0 ? pose.LeftFoot : pose.RightFoot, body, i);
                arms[i].Place(i == 0 ? pose.LeftHand : pose.RightHand, body, i);
            }
        }

        sealed class Limb
        {
            readonly Transform upper, lower, end;
            readonly Quaternion upperRotation, lowerRotation, endRotation;
            readonly Vector3 upperDirection, lowerDirection, targetOffset;
            readonly float upperLength, lowerLength;
            readonly bool leg;

            internal static Limb Bind(Transform model, Transform upper, Transform lower, Transform end, Transform tip, bool leg)
            {
                if (upper == null || lower == null || end == null || !lower.IsChildOf(upper) || !end.IsChildOf(lower)
                    || Vector3.Distance(upper.position, lower.position) < 0.001f
                    || Vector3.Distance(lower.position, end.position) < 0.001f) return null;
                return new Limb(model, upper, lower, end, tip, leg);
            }

            Limb(Transform model, Transform upper, Transform lower, Transform end, Transform tip, bool leg)
            {
                this.upper = upper;
                this.lower = lower;
                this.end = end;
                this.leg = leg;
                Quaternion inverse = Quaternion.Inverse(model.rotation);
                upperRotation = inverse * upper.rotation;
                lowerRotation = inverse * lower.rotation;
                upperDirection = inverse * (lower.position - upper.position);
                lowerDirection = inverse * (end.position - lower.position);
                upperLength = upperDirection.magnitude;
                lowerLength = lowerDirection.magnitude;
                if (leg)
                {
                    // Network feet are shoe centers. Preserve the boot's bind-pose ankle height and toe direction.
                    float ankleHeight = (inverse * (end.position - model.position)).y;
                    Vector3 toe = tip != null ? inverse * (tip.position - end.position) : Vector3.forward * 0.14f;
                    targetOffset = Vector3.up * (ankleHeight - SeatedMotion.ShoeCenterHeight) - 0.5f * Vector3.ProjectOnPlane(toe, Vector3.up);
                    endRotation = inverse * end.rotation;
                }
                else
                {
                    Vector3 fingers = tip != null ? tip.position - end.position : end.position - lower.position;
                    endRotation = Quaternion.Inverse(Quaternion.LookRotation(fingers, model.up)) * end.rotation;
                }
            }

            internal void Place(Pose target, Quaternion body, int side)
            {
                Vector3 goal = target.position + target.rotation * targetOffset;
                Vector3 start = upper.position;
                Vector3 span = goal - start;
                float distance = span.magnitude;
                Vector3 joint;
                if (distance >= upperLength + lowerLength)
                {
                    // A tracked wrist or planted shoe can exceed the model's reach when the player leans.
                    joint = start + span * (upperLength / (upperLength + lowerLength));
                }
                else
                {
                    Vector3 axis = distance > 0.0001f ? span / distance : body * Vector3.down;
                    Vector3 bend = leg ? Vector3.Cross(axis, body * Vector3.right)
                        : Vector3.ProjectOnPlane(body * new Vector3(side == 0 ? -1 : 1, -0.65f, -0.5f), axis);
                    if (bend.sqrMagnitude < 0.00001f) bend = Vector3.ProjectOnPlane(body * Vector3.forward, axis);
                    float along = distance > 0.0001f ? Mathf.Clamp((upperLength * upperLength - lowerLength * lowerLength
                        + distance * distance) / (2 * distance), -upperLength, upperLength) : 0;
                    float height = Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
                    joint = start + axis * along + bend.normalized * height;
                }
                upper.rotation = Quaternion.FromToRotation(body * upperDirection, joint - start) * body * upperRotation;
                lower.SetPositionAndRotation(joint,
                    Quaternion.FromToRotation(body * lowerDirection, goal - joint) * body * lowerRotation);
                end.SetPositionAndRotation(goal, target.rotation * endRotation);
            }
        }
    }
}
