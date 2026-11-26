using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class SoldierWristTests
    {
        [TestCase("TestRig", true)]
        [TestCase("Uniform", true)]
        [TestCase("Armored", false)]
        public void TurningAtTheWristKeepsTheElbowStillAndTheGripInsideThePalm(string style, bool own)
        {
            GameObject model = MakeModel(style);
            try
            {
                Transform[] bones = model.GetComponentsInChildren<Transform>(true);
                var soldier = SoldierAvatar.Bind(model.transform, own);
                Assert.That(soldier, Is.Not.Null);
                var heading = Quaternion.Euler(0, 71, 0);
                var floor = new Vector3(1.4f, 0.45f, -2.1f);
                var pose = new AvatarPose
                {
                    Head = new Pose(floor + Vector3.up * SeatedMotion.StandingEyeHeight, heading),
                    LeftHand = new Pose(floor + heading * new Vector3(-0.28f, 1.25f, 0.24f), heading),
                    RightHand = new Pose(floor + heading * new Vector3(0.28f, 1.25f, 0.24f), heading),
                    LeftFoot = SeatedMotion.Foot(floor, heading, true, 0, false),
                    RightFoot = SeatedMotion.Foot(floor, heading, false, 0, false)
                };

                foreach (bool left in new[] { true, false })
                {
                    string side = left ? "_l" : "_r";
                    Transform Bone(string name) => bones.Single(b => b.name == name + side);
                    Transform shoulder = Bone("upperarm"), elbow = Bone("lowerarm"), wrist = Bone("hand");
                    Transform middle = Bone("middle_01"), index = Bone("index_01"), pinky = Bone("pinky_01");
                    float palmRadius = Vector3.Distance(wrist.position, middle.position) * 0.5f;
                    Vector3 wristPoint = floor + heading * new Vector3(left ? -0.27f : 0.27f, 1.24f, 0.23f);
                    var neutral = heading * Quaternion.Euler(-18, left ? 12 : -12, 0);

                    void DrawHand(Quaternion turn)
                    {
                        // A physical controller describes an arc around a resting wrist.
                        var grip = new Pose(wristPoint + turn * Vector3.forward * palmRadius, turn);
                        if (left) pose.LeftHand = grip; else pose.RightHand = grip;
                        soldier.Draw(pose);
                        Vector3 along = (middle.position - wrist.position).normalized;
                        Vector3 towardIndex = Vector3.ProjectOnPlane(index.position - pinky.position, along).normalized;
                        Assert.That(Vector3.Distance(wrist.position, wristPoint), Is.LessThan(0.001f), side + " wrist pivot");
                        Assert.That(Vector3.Distance((wrist.position + middle.position) * 0.5f, grip.position),
                            Is.LessThan(0.001f), side + " palm center");
                        Assert.That(Vector3.Angle(along, turn * Vector3.forward), Is.LessThan(0.2f), side + " fingers");
                        Assert.That(Vector3.Angle(towardIndex, turn * Vector3.up), Is.LessThan(0.2f), side + " thumb side");
                    }

                    DrawHand(neutral);
                    Vector3 restElbow = elbow.position;
                    Quaternion upperRotation = shoulder.rotation, forearmRotation = elbow.rotation;
                    foreach (Vector3 turn in new[] { new Vector3(0, 0, 90), new Vector3(40, 0, 0),
                        new Vector3(-28, 22, -55), Vector3.zero })
                    {
                        DrawHand(neutral * Quaternion.Euler(turn));
                        Assert.That(Vector3.Distance(elbow.position, restElbow), Is.LessThan(0.001f), side + " elbow");
                        Assert.That(Quaternion.Angle(elbow.rotation, forearmRotation), Is.LessThan(0.15f), side + " forearm roll");
                        Assert.That(Quaternion.Angle(shoulder.rotation, upperRotation), Is.LessThan(0.15f), side + " shoulder");
                    }
                    wristPoint += heading * new Vector3(0, 0.2f, 0.15f);
                    DrawHand(neutral);
                    Assert.That(Vector3.Distance(elbow.position, restElbow), Is.GreaterThan(0.04f), side + " reach");

                    var rung = left ? pose.LeftHand : pose.RightHand;
                    rung.rotation *= Quaternion.Euler(25, 10, 30);
                    if (left) pose.LeftHand = rung; else pose.RightHand = rung;
                    soldier.Draw(pose);
                    Assert.That(Vector3.Distance((wrist.position + middle.position) * 0.5f, rung.position),
                        Is.LessThan(0.001f), side + " palm contact while turning on a rung");
                }
            }
            finally { Object.DestroyImmediate(model); }
        }

        [Test]
        public void ReachingWithABentArmPreservesItsBindPoseElbowHinge()
        {
            var model = MakeModel("TestRig");
            try
            {
                var bones = model.GetComponentsInChildren<Transform>();
                var shoulder = bones.Single(b => b.name == "upperarm_r");
                var elbow = bones.Single(b => b.name == "lowerarm_r");
                var wrist = bones.Single(b => b.name == "hand_r");
                Vector3 Hinge() => Vector3.Cross(elbow.position - shoulder.position, wrist.position - elbow.position).normalized;
                Vector3 localHinge = elbow.InverseTransformDirection(Hinge());
                var soldier = SoldierAvatar.Bind(model.transform, false);
                var pose = new AvatarPose
                {
                    Head = new Pose(Vector3.up * SeatedMotion.StandingEyeHeight, Quaternion.identity),
                    LeftHand = new Pose(new Vector3(-0.3f, 1.4f, 0.28f), Quaternion.identity),
                    LeftFoot = Pose.identity, RightFoot = Pose.identity
                };
                foreach (Vector3 grip in new[] { new Vector3(0.2f, 1.24f, 0.32f), new Vector3(0.22f, 1.72f, 0.2f) })
                {
                    pose.RightHand = new Pose(grip, Quaternion.identity);
                    soldier.Draw(pose);
                    Assert.That(Vector3.Angle(elbow.TransformDirection(localHinge), Hinge()), Is.LessThan(0.2f),
                        "forearm must bend in the same plane as the upper arm");
                }
            }
            finally { Object.DestroyImmediate(model); }
        }

        [TestCase("Uniform", "_l")]
        [TestCase("Armored", "_r")]
        public void APhysicalWristWaveMovesTheHandMeshAndKeepsTheSleeveStable(string style, string side)
        {
            var model = MakeModel(style);
            var baked = new Mesh();
            try
            {
                var bones = model.GetComponentsInChildren<Transform>();
                var elbow = bones.Single(b => b.name == "lowerarm" + side);
                var wrist = bones.Single(b => b.name == "hand" + side);
                var middle = bones.Single(b => b.name == "middle_01" + side);
                var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
                var sleeve = renderers.Single(r => r.name == "Shirt");
                var hands = renderers.Single(r => r.name == "Hands");
                var source = sleeve.sharedMesh;
                var vertices = source.vertices;
                var weights = source.boneWeights;
                int wristIndex = System.Array.IndexOf(sleeve.bones, wrist);
                Vector3 forearm = wrist.position - elbow.position;
                var wristSeam = Enumerable.Range(0, vertices.Length).Where(i =>
                    weights[i].boneIndex0 == wristIndex && weights[i].weight0 > 0.999f &&
                    Vector3.Dot(sleeve.transform.TransformPoint(vertices[i]) - elbow.position, forearm) > 0.98f * forearm.sqrMagnitude).ToArray();
                Assert.That(wristSeam.Length, Is.GreaterThan(10));
                var soldier = SoldierAvatar.Bind(model.transform, false);
                Vector3[] Vertices(SkinnedMeshRenderer renderer)
                {
                    renderer.BakeMesh(baked);
                    // BakeMesh includes the posed skeleton's scale. Apply only the renderer's rigid transform.
                    return System.Array.ConvertAll(baked.vertices, v => renderer.transform.position + renderer.transform.rotation * v);
                }
                bool left = side == "_l";
                float radius = Vector3.Distance(wrist.position, middle.position) * 0.5f;
                var pivot = new Vector3(left ? -0.28f : 0.28f, 1.22f, 0.31f);
                var pose = new AvatarPose
                {
                    Head = new Pose(Vector3.up * SeatedMotion.StandingEyeHeight, Quaternion.identity),
                    LeftFoot = Pose.identity, RightFoot = Pose.identity,
                    LeftHand = new Pose(new Vector3(-0.3f, 1.23f, 0.3f), Quaternion.identity),
                    RightHand = new Pose(new Vector3(0.3f, 1.23f, 0.3f), Quaternion.identity)
                };
                void Wave(Quaternion turn)
                {
                    var grip = new Pose(pivot + turn * Vector3.forward * radius, turn);
                    if (left) pose.LeftHand = grip; else pose.RightHand = grip;
                    soldier.Draw(pose);
                }
                Wave(Quaternion.identity);
                var sleeveAtRest = Vertices(sleeve);
                var handAtRest = Vertices(hands);
                // Inspect the middle of the actual skinned sleeve, clear of the wrist cuff.
                Vector3 midpoint = (elbow.position + wrist.position) * 0.5f;
                var midSleeve = Enumerable.Range(0, sleeveAtRest.Length)
                    .Where(i => Vector3.Distance(sleeveAtRest[i], midpoint) < 0.075f).ToArray();
                Assert.That(midSleeve.Length, Is.GreaterThan(10));
                var wave = Quaternion.Euler(-40, 20, 65);
                Wave(wave);
                var sleeveAfter = Vertices(sleeve);
                var handAfter = Vertices(hands);
                float sleeveTravel = midSleeve.Max(i => Vector3.Distance(sleeveAtRest[i], sleeveAfter[i]));
                float handTravel = Enumerable.Range(0, handAfter.Length).Max(i => Vector3.Distance(handAtRest[i], handAfter[i]));
                Assert.That(sleeveTravel, Is.LessThan(0.002f), "middle of the sleeve");
                Assert.That(handTravel, Is.GreaterThan(0.03f), "visible hand");
                float seamGap = wristSeam.Max(i => Vector3.Distance(pivot + wave * (sleeveAtRest[i] - pivot), sleeveAfter[i]));
                Assert.That(seamGap, Is.LessThan(0.002f), "skin at the wrist seam follows the hand");
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(model); }
        }

        static GameObject MakeModel(string style)
        {
            if (style != "TestRig")
            {
                var prefab = Resources.Load<GameObject>("BodyTrackingCharacters/Soldier" + style);
                if (!prefab) Assert.Ignore("The optional soldier character pack is not installed.");
                return Object.Instantiate(prefab);
            }

            var model = new GameObject("Test soldier");
            Transform Joint(Transform parent, string name, Vector3 position)
            {
                var joint = new GameObject(name).transform;
                joint.SetParent(parent, false);
                joint.position = position;
                return joint;
            }

            var pelvis = Joint(model.transform, "pelvis", Vector3.up);
            Joint(pelvis, "head", new Vector3(0, 1.56f, 0));
            foreach (int sign in new[] { -1, 1 })
            {
                string side = sign < 0 ? "_l" : "_r";
                Transform Part(Transform parent, string bone, float x, float y, float z) =>
                    Joint(parent, bone + side, new Vector3(sign * x, y, z));
                var thigh = Part(pelvis, "thigh", 0.14f, 0.98f, 0);
                var calf = Part(thigh, "calf", 0.14f, 0.54f, 0.02f);
                Part(calf, "foot", 0.14f, 0.10f, 0);
                var arm = Part(pelvis, "upperarm", 0.16f, 1.46f, -0.09f);
                var elbow = Part(arm, "lowerarm", 0.37f, 1.26f, -0.11f);
                var wrist = Part(elbow, "hand", 0.58f, 1.11f, 0);
                Part(wrist, "middle_01", 0.65f, 1.04f, 0.04f);
                Part(wrist, "index_01", 0.63f, 1.04f, 0.07f);
                Part(wrist, "pinky_01", 0.64f, 1.03f, 0);
            }
            return model;
        }
    }
}
