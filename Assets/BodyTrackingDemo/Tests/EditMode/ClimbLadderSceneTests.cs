using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class ClimbLadderSceneTests
    {
        [TestCase("StepOverCable")]
        [TestCase("Tablet")]
        public void VehicleAndNineBarsShareOneAuthoredLayout(string sceneName)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/BodyTrackingDemo/Scenes/" + sceneName + ".unity");
            try
            {
                var layout = scene.GetRootGameObjects().SingleOrDefault(go => go.name == "Vehicle Ladder");
                Assert.That(layout, Is.Not.Null, "The scene must instantiate the shared vehicle and ladder.");
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(layout),
                    Is.EqualTo("Assets/BodyTrackingDemo/Runtime/VehicleLadder.prefab"));
                var climb = layout.GetComponent<ClimbLadder>();
                Assert.That(climb.RungCount, Is.EqualTo(9));
                Assert.That(climb.TopBar.IsChildOf(climb.LadderRoot), Is.True);
                Assert.That(climb.VehicleRoot.GetComponentsInChildren<BoxCollider>()
                    .Any(c => c.enabled && c.bounds.max.y > 3f), Is.True, "The vehicle roof must support the climber.");
                Assert.That(Vector3.Dot(climb.FaceDirection, Vector3.left), Is.GreaterThan(0.99f));

                CheckGrips(climb);
                climb.LadderRoot.SetPositionAndRotation(new Vector3(3f, 1f, -4f), Quaternion.Euler(0f, -42f, 0f));
                climb.LadderRoot.localScale = new Vector3(1.4f, 0.8f, 1.2f);
                CheckGrips(climb);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void CheckGrips(ClimbLadder climb)
        {
            var bars = climb.LadderRoot.GetComponentsInChildren<MeshFilter>()
                .Where(mesh => mesh.name.StartsWith("LadderHold_")).OrderBy(mesh => mesh.name).ToArray();
            Assert.That(bars.Length, Is.EqualTo(9));
            Vector3 right = Vector3.Cross(climb.LadderRoot.up, climb.FaceDirection);
            for (int i = 0; i < bars.Length; i++)
            {
                var mesh = bars[i];
                Assert.That(mesh.name, Is.EqualTo("LadderHold_" + (i + 1)));
                Bounds bounds = mesh.sharedMesh.bounds;
                Vector3 center = mesh.transform.TransformPoint(bounds.center);
                Vector3 leftFoot = climb.RungPoint(i + 1, true);
                Vector3 rightFoot = climb.RungPoint(i + 1, false);
                Assert.That(Vector3.Distance(center, (leftFoot + rightFoot) / 2f), Is.LessThan(0.003f), mesh.name);
                Assert.That(Vector3.Dot(rightFoot - leftFoot, right), Is.GreaterThan(0.1f), mesh.name);
                var capsule = mesh.GetComponentInChildren<CapsuleCollider>();
                Assert.That(Vector3.Distance(center, capsule.transform.TransformPoint(capsule.center)), Is.LessThan(0.003f));
                Vector3 end = mesh.transform.TransformPoint(bounds.center + Vector3.right * bounds.extents.x);
                Assert.That(climb.FindRung(end + climb.FaceDirection * 0.035f, 0.04f), Is.EqualTo(i + 1), mesh.name);
                Assert.That(climb.FindRung(end + (end - center).normalized * 0.08f, 0.04f), Is.Zero);
                if (i > 0)
                {
                    // Halfway up the rail the hand is between two horizontal bars.
                    Vector3 below = bars[i - 1].transform.TransformPoint(bars[i - 1].sharedMesh.bounds.center);
                    Assert.That(climb.FindRung((center + below) / 2f + (end - center), 0.04f), Is.Zero);
                    Assert.That(climb.FindRung(Vector3.Lerp(below, center, 0.7f), 1f), Is.EqualTo(i + 1));
                }
            }
            Assert.That(climb.RungPoint(0, true).y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(climb.RungPoint(0, false).y, Is.EqualTo(0f).Within(0.001f));
        }
    }
}
