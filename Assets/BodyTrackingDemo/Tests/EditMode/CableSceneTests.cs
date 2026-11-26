using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class CableSceneTests
    {
        [TestCase("StepOverCable")]
        [TestCase("Tablet")]
        public void MissionSceneContainsOneVisibleCableLinkedToTheDemo(string sceneName)
        {
            string path = "Assets/BodyTrackingDemo/Scenes/" + sceneName + ".unity";
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null, path);
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var roots = scene.GetRootGameObjects();
                var demo = roots.SelectMany(r => r.GetComponentsInChildren<BodyTrackingDemo>(true)).Single();
                var field = new SerializedObject(demo).FindProperty("cableMission");
                Assert.That(field, Is.Not.Null, "The demo must reference the playable obstacle.");
                var cable = roots.SelectMany(r => r.GetComponentsInChildren<StepOverCableMission>(true)).Single();
                Assert.That(field.objectReferenceValue, Is.EqualTo(cable));
                Assert.That(cable.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(100));
                Assert.That(cable.GetComponent<MeshRenderer>().sharedMaterial.shader.isSupported, Is.True);
                Assert.That(cable.GetComponent<Collider>(), Is.Null, "Players must be able to cross.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
