#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class CableFeedbackTests
    {
        [UnityTest]
        public IEnumerator SharedCableRendersAndOnlyTheCrossingPlayerGetsFullScreenFeedback()
        {
            var parent = new GameObject("Cable test");
            var room = new DemoRoom(parent.transform);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BodyTrackingDemo/Runtime/FloorCable/FloorCable.prefab");
            var mission = Object.Instantiate(prefab, parent.transform).GetComponent<StepOverCableMission>();
            var other = new GameObject("Other player's cable state").AddComponent<StepOverCableMission>();
            other.transform.SetParent(parent.transform);
            var head = new GameObject("Crossing player's view").AddComponent<Camera>();
            head.transform.SetParent(parent.transform);
            head.transform.position = new Vector3(0.58f, 1.33f, -2.6f);
            head.transform.LookAt(new Vector3(0, 0.04f, 0.12f));
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = new Color32(27, 43, 58, 255);
            head.enabled = false;
            var otherHead = new GameObject("Other headset").transform;
            otherHead.SetParent(parent.transform);
            otherHead.position = Vector3.right * 30;
            var target = RenderTexture.GetTemporary(960, 540, 24);
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            head.targetTexture = target;
            void Capture()
            {
                head.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
            }
            void CheckTint(bool green)
            {
                Capture();
                foreach (var x in new[] { 4, image.width / 2, image.width - 5 })
                    foreach (var y in new[] { 4, image.height / 2, image.height - 5 })
                    {
                        Color pixel = image.GetPixel(x,y);
                        Assert.That(green ? pixel.g : pixel.r, Is.GreaterThan((green ? pixel.r : pixel.g) * 1.3f), $"pixel ({x}, {y})");
                    }
            }
            try
            {
                mission.BeginLocal(head.transform);
                other.BeginLocal(otherHead);
                var feedback = head.transform.Find("Cable feedback").GetComponent<MeshRenderer>();
                var otherFeedback = otherHead.Find("Cable feedback").GetComponent<MeshRenderer>();
                yield return null;
                Capture();
                int orange = Array.FindAll(image.GetPixels32(), c => c.r > 170 && c.r > 1.5f * c.g && c.g > 1.4f * c.b).Length;
                Assert.That(orange, Is.GreaterThan(450), "The untreated camera view should show the orange cord.");
                string capture = Environment.GetEnvironmentVariable("BODYTRACKING_CABLE_CAPTURE");
                if (!string.IsNullOrEmpty(capture)) File.WriteAllBytes(capture, image.EncodeToPNG());

                Vector3 near = new(0, 0, -0.16f), across = new(0, 0, 0.55f);
                mission.Tick(near, 3, 0, 0, true, 0);
                mission.Tick(near, 3, 0.12f, 0, true, 0.1);
                mission.Tick(near, 3, 0, 0.11f, true, 0.2);
                mission.Tick(across, 3, 0, 0, true, 0.3);
                Assert.That(mission.Cue, Is.EqualTo("Cable cleared"));
                CheckTint(true);
                Assert.That(otherFeedback.enabled, Is.False);
                mission.Tick(across, 3, 0, 0, false, 1.3);
                Assert.That(feedback.enabled, Is.False);
                mission.Tick(across, 3, 0, 0, true, 2);
                mission.Tick(near, 0, 0, 0, true, 2.2);
                CheckTint(false);
                Assert.That(otherFeedback.enabled, Is.False);
                mission.Tick(near, 0, 0, 0, false, 7.19);
                Assert.That(feedback.enabled, Is.True);
                mission.Tick(near, 0, 0, 0, false, 7.2);
                Assert.That(feedback.enabled, Is.False);
            }
            finally
            {
                head.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(image);
                room.Dispose();
                Object.Destroy(parent);
            }
        }
    }
}
#endif
