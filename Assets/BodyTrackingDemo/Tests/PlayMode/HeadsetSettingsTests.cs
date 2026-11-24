using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public class HeadsetSettingsTests
    {
        [UnityTest]
        public IEnumerator LeaningInTheRealChairLeavesBothShoesAtTheSameVirtualFloorPositions()
        {
            DemoChoices previous = DemoChoices.Load();
            bool hadChoices = DemoChoices.Saved;
            var rig = new GameObject("Test camera rig");
            var space = new GameObject("TrackingSpace").transform;
            space.SetParent(rig.transform, false);
            var head = new GameObject("CenterEyeAnchor").transform;
            head.SetParent(space, false);
            head.localPosition = new Vector3(0.65f, 1.2f, -0.3f);
            var demo = new GameObject("Test demo");
            try
            {
                new DemoChoices { Role = 1, Pc = "", Tablet = "127.0.0.1" }.Save();
                demo.AddComponent<BodyTrackingDemo>();
                yield return null;
                yield return null;
                var avatar = demo.transform.Find("Seated tracking room/My Seat 1");
                Assert.That(avatar, Is.Not.Null);
                var left = avatar.Find("Left shoe");
                var right = avatar.Find("Right shoe");
                Vector3 startLeft = left.position, startRight = right.position;
                Assert.That(startLeft.y - left.lossyScale.y * 0.5f, Is.EqualTo(0).Within(0.001f));
                head.localPosition += new Vector3(0.22f, -0.12f, 0.1f);
                yield return null;
                yield return null;
                Assert.That(Vector3.Distance(left.position, startLeft), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(right.position, startRight), Is.LessThan(0.001f));
            }
            finally
            {
                Object.Destroy(demo);
                Object.Destroy(rig);
                if (hadChoices) previous.Save();
                else
                {
                    foreach (string key in new[] { "role", "pc", "tablet" }) PlayerPrefs.DeleteKey("Blacktheon.SeatedDemo." + key);
                    PlayerPrefs.Save();
                }
            }
        }

        [UnityTearDown]
        public IEnumerator WaitForDemoShutdown()
        {
            yield return null;
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (NetworkManager.Singleton != null && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        }

        [Test]
        public void DirectionalInputEntersAPcAddressAndSelectsAMultiplayerSeat()
        {
            var owner = new GameObject("Headset menu test");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                DemoChoices? entered = null;
                var panel = new QuestPanel(owner.transform, choices => entered = choices);
                var input = new DemoControls();
                panel.OpenSettings(new DemoChoices { Role = 0, Pc = "", Tablet = "" }, owner.transform);
                void Press(Key button)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(button));
                    InputSystem.Update();
                    input.Read();
                    panel.Handle(input);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    InputSystem.Update();
                    input.Read();
                }

                Press(Key.RightArrow); // solo -> seat 1
                Press(Key.DownArrow);
                Press(Key.Enter); // PC keypad, focused on 1
                Press(Key.Enter);
                Press(Key.UpArrow); // wrap to the period on the bottom row
                Press(Key.Enter);
                Press(Key.RightArrow); Press(Key.Enter); // 0
                Press(Key.LeftArrow); Press(Key.Enter);  // .
                Press(Key.RightArrow); Press(Key.Enter); // 0
                Press(Key.LeftArrow); Press(Key.Enter);  // .
                Press(Key.DownArrow); Press(Key.Enter);  // 1
                Press(Key.UpArrow); Press(Key.LeftArrow); Press(Key.LeftArrow); Press(Key.Enter); // Save
                Press(Key.DownArrow);
                Press(Key.DownArrow);
                Press(Key.Enter);

                Assert.That(entered.HasValue, Is.True);
                Assert.That(entered.Value.Role, Is.EqualTo(1));
                Assert.That(entered.Value.Pc, Is.EqualTo("1.0.0.1"));
                Assert.That(entered.Value.Tablet, Is.Empty);
                Assert.That(panel.Open, Is.False);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                Object.DestroyImmediate(owner);
            }
        }
    }
}
