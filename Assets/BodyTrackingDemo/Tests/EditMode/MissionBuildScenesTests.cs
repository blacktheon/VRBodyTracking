using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Blacktheon.BodyTracking.Demo.Tests
{
    public sealed class MissionBuildScenesTests
    {
        [Test]
        public void ProjectListsExactlyTheClientAndTabletMissionScenes()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path);
            Assert.That(scenes, Is.EquivalentTo(new[] {
                "Assets/BodyTrackingDemo/Scenes/StepOverCable.unity",
                "Assets/BodyTrackingDemo/Scenes/Tablet.unity"
            }));
        }
    }
}
