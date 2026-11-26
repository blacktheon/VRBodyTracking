using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo
{
    // The imported shirt includes the exposed forearm. Adapt its skin on the spawned avatar.
    internal sealed class SoldierSleeveSkin : MonoBehaviour
    {
        Mesh fittedMesh;

        readonly struct Forearm
        {
            internal readonly int Elbow, Wrist;
            readonly Vector3 elbowPosition, projection;

            internal Forearm(int elbow, int wrist, Matrix4x4[] bindPoses)
            {
                Elbow = elbow;
                Wrist = wrist;
                elbowPosition = bindPoses[elbow].inverse.MultiplyPoint3x4(Vector3.zero);
                Vector3 span = bindPoses[wrist].inverse.MultiplyPoint3x4(Vector3.zero) - elbowPosition;
                projection = span / Mathf.Max(span.sqrMagnitude, 0.000001f);
            }

            internal float HandShare(Vector3 vertex)
            {
                float along = Vector3.Dot(vertex - elbowPosition, projection);
                // Keep the forearm stable and blend into the original hand weights across the wrist.
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.77f, 0.92f, along));
            }
        }

        internal static bool TryFit(SkinnedMeshRenderer shirt, Transform leftElbow, Transform leftWrist,
            Transform rightElbow, Transform rightWrist)
        {
            if (shirt.GetComponent<SoldierSleeveSkin>()) return true;
            var source = shirt.sharedMesh;
            if (!source || !source.isReadable) return false;
            var bones = shirt.bones;
            int leftElbowIndex = Array.IndexOf(bones, leftElbow), leftWristIndex = Array.IndexOf(bones, leftWrist);
            int rightElbowIndex = Array.IndexOf(bones, rightElbow), rightWristIndex = Array.IndexOf(bones, rightWrist);
            if (leftElbowIndex < 0 || leftWristIndex < 0 || rightElbowIndex < 0 || rightWristIndex < 0) return false;
            var bindPoses = source.bindposes;
            var left = new Forearm(leftElbowIndex, leftWristIndex, bindPoses);
            var right = new Forearm(rightElbowIndex, rightWristIndex, bindPoses);
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
                bool hasWrist = false;
                for (int j = read; j < stop; j++)
                    hasWrist |= original[j].boneIndex == leftWristIndex || original[j].boneIndex == rightWristIndex;
                if (!hasWrist)
                {
                    newCounts[i] = counts[i];
                    while (read < stop) weights.Add(original[read++]);
                    continue;
                }
                influences.Clear();
                while (read < stop)
                {
                    var w = original[read++];
                    if (w.boneIndex == leftWristIndex || w.boneIndex == rightWristIndex)
                    {
                        var arm = w.boneIndex == leftWristIndex ? left : right;
                        float wristWeight = w.weight * arm.HandShare(vertices[i]);
                        AddWeight(influences, arm.Elbow, w.weight - wristWeight);
                        AddWeight(influences, arm.Wrist, wristWeight);
                    }
                    else AddWeight(influences, w.boneIndex, w.weight);
                }
                influences.Sort((a, b) => b.weight.CompareTo(a.weight));
                newCounts[i] = (byte)influences.Count;
                weights.AddRange(influences);
            }

            using var boneCounts = new NativeArray<byte>(newCounts, Allocator.Temp);
            using var boneWeights = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp);
            var owner = shirt.gameObject.AddComponent<SoldierSleeveSkin>();
            owner.fittedMesh = Instantiate(source);
            owner.fittedMesh.name = source.name + " (tracked wrists)";
            owner.fittedMesh.SetBoneWeights(boneCounts, boneWeights);
            shirt.sharedMesh = owner.fittedMesh;
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
