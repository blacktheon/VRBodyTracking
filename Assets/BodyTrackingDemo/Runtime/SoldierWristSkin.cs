using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo
{
    // The exposed forearm and the hand/glove are separate meshes that overlap at the wrist.
    internal sealed class SoldierWristSkin : MonoBehaviour
    {
        Mesh fittedMesh;

        readonly struct Forearm
        {
            internal readonly int Elbow, Wrist;
            readonly Vector3 heel, projection;

            internal Forearm(int elbow, int wrist, Vector3 wristPosition, Vector3 middleKnuckle)
            {
                Elbow = elbow;
                Wrist = wrist;
                heel = Vector3.Lerp(wristPosition, middleKnuckle, SoldierAvatar.PalmHeelFraction);
                Vector3 span = middleKnuckle - wristPosition;
                projection = span / span.sqrMagnitude;
            }

            internal float HandShare(Vector3 vertex)
            {
                float along = Vector3.Dot(vertex - heel, projection);
                // The approximately 2 cm blend straddles the palm heel on both meshes.
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.11f, 0.11f, along));
            }

            internal bool Contains(int bone) => bone == Elbow || bone == Wrist;
        }

        internal static bool TryFit(SkinnedMeshRenderer skin, Func<string, Transform> bone)
        {
            if (skin.GetComponent<SoldierWristSkin>()) return true;
            var source = skin.sharedMesh;
            if (!source || !source.isReadable) return false;
            var bones = skin.bones;
            bool BindSide(string side, out Forearm arm)
            {
                arm = default;
                Transform wrist = bone("hand" + side), knuckle = bone("middle_01" + side);
                if (!wrist || !knuckle || (knuckle.position - wrist.position).sqrMagnitude < 0.000001f) return false;
                int elbowIndex = Array.IndexOf(bones, bone("lowerarm" + side)), wristIndex = Array.IndexOf(bones, wrist);
                if (elbowIndex < 0 || wristIndex < 0) return false;
                arm = new Forearm(elbowIndex, wristIndex, wrist.position, knuckle.position);
                return true;
            }
            if (!BindSide("_l", out var left) || !BindSide("_r", out var right)) return false;
            var armByBone = new Forearm?[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!bones[i]) continue;
                if (left.Contains(i) || bones[i].IsChildOf(bones[left.Wrist])) armByBone[i] = left;
                else if (right.Contains(i) || bones[i].IsChildOf(bones[right.Wrist])) armByBone[i] = right;
            }
            var toWorld = skin.transform.localToWorldMatrix;
            var vertices = source.vertices;
            var counts = source.GetBonesPerVertex();
            var original = source.GetAllBoneWeights();
            var newCounts = new byte[vertices.Length];
            var weights = new List<BoneWeight1>(original.Length);
            var influences = new List<BoneWeight1>(8);
            int read = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                int stop = read + counts[i];
                bool armVertex = false;
                for (int j = read; j < stop; j++)
                    armVertex |= armByBone[original[j].boneIndex].HasValue;
                if (!armVertex)
                {
                    newCounts[i] = counts[i];
                    while (read < stop) weights.Add(original[read++]);
                    continue;
                }
                Vector3 position = toWorld.MultiplyPoint3x4(vertices[i]);
                influences.Clear();
                while (read < stop)
                {
                    var w = original[read++];
                    if (armByBone[w.boneIndex] is Forearm arm)
                    {
                        // Some cuff vertices are weighted to the thumb. Keep finger weights beyond
                        // the heel, and give their proximal share to the same forearm as the shirt.
                        float movingWeight = w.weight * arm.HandShare(position);
                        AddWeight(influences, arm.Elbow, w.weight - movingWeight);
                        AddWeight(influences, w.boneIndex == arm.Elbow ? arm.Wrist : w.boneIndex, movingWeight);
                    }
                    else AddWeight(influences, w.boneIndex, w.weight);
                }
                influences.Sort((a, b) => b.weight.CompareTo(a.weight));
                newCounts[i] = (byte)influences.Count;
                weights.AddRange(influences);
            }

            using var boneCounts = new NativeArray<byte>(newCounts, Allocator.Temp);
            using var boneWeights = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp);
            var owner = skin.gameObject.AddComponent<SoldierWristSkin>();
            owner.fittedMesh = Instantiate(source);
            owner.fittedMesh.name = source.name + " (tracked wrists)";
            owner.fittedMesh.SetBoneWeights(boneCounts, boneWeights);
            skin.sharedMesh = owner.fittedMesh;
            return true;
        }

        static void AddWeight(List<BoneWeight1> influences, int bone, float weight)
        {
            if (weight <= 0) return;
            for (int i = 0; i < influences.Count; i++)
            {
                var current = influences[i];
                if (current.boneIndex != bone) continue;
                current.weight += weight;
                influences[i] = current;
                return;
            }
            influences.Add(new BoneWeight1 { boneIndex = bone, weight = weight });
        }

        void OnDestroy()
        {
            if (!fittedMesh) return;
            if (Application.isPlaying) Destroy(fittedMesh);
            else DestroyImmediate(fittedMesh);
        }
    }
}
