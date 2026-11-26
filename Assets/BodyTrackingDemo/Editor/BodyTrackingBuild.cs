using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blacktheon.BodyTracking.Demo.Editor
{
    public static class BodyTrackingBuild
    {
        // Tools/Build-TestApps.ps1 calls these methods in disposable projects.
        public static void BuildQuest() => Build("Quest");
        public static void BuildTablet() => Build("Tablet");

        static void Build(string role)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string marker = Path.Combine(root, ".bodytracking-build-stage");
            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != role)
                throw new BuildFailedException("Run Tools/Build-TestApps.ps1 to prepare the Android build project.");

            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-bodyTrackingOutput");
            if (index < 0 || index + 1 >= args.Length)
                throw new BuildFailedException("Missing -bodyTrackingOutput APK path.");

            bool tablet = role == "Tablet";
            string output = Path.GetFullPath(args[index + 1]);
            int layout = Array.IndexOf(args, "-bodyTrackingTabletAssets");
            if (!tablet && layout >= 0)
            {
                if (layout + 1 >= args.Length) throw new BuildFailedException("Missing tablet layout export directory.");
                ClimbTabletExport.ExportTo(args[layout + 1]);
            }
            string scene = tablet ? "Assets/BodyTrackingDemo/Scenes/Tablet.unity" : "Assets/BodyTrackingDemo/Scenes/StepOverCable.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                throw new BuildFailedException($"Missing demo scene {scene}.");
            var roots = EditorSceneManager.OpenScene(scene).GetRootGameObjects();
            var demo = roots.SelectMany(go => go.GetComponentsInChildren<BodyTrackingDemo>(true)).SingleOrDefault();
            if (demo == null || new SerializedObject(demo).FindProperty("tablet").boolValue != tablet)
                throw new BuildFailedException($"The {role} scene needs a demo component configured for that device.");
            if (new SerializedObject(demo).FindProperty("cableMission").objectReferenceValue == null)
                throw new BuildFailedException($"The {role} scene needs its floor cable.");
            var ladders = roots.SelectMany(go => go.GetComponentsInChildren<ClimbLadder>()).ToArray();
            if (ladders.Length != 1 || ladders[0].RungCount == 0 || !ladders[0].VehicleRoot
                || !ladders[0].LadderRoot || !ladders[0].TopBar)
                throw new BuildFailedException($"The {role} scene needs one complete vehicle and ladder.");
            if (!tablet)
            {
                var transforms = roots.SelectMany(go => go.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach (string anchor in new[] { "TrackingSpace", "CenterEyeAnchor", "LeftHandAnchor", "RightHandAnchor" })
                    if (!transforms.Any(t => t.name == anchor))
                        throw new BuildFailedException($"Missing Quest tracking anchor {anchor}.");
            }

            PlayerSettings.companyName = "blacktheon";
            PlayerSettings.productName = $"Body Tracking {role}";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.blacktheon.bodytracking." + role.ToLowerInvariant());
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            if (tablet)
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel30;
                PlayerSettings.SetPreloadedAssets(Array.Empty<UnityEngine.Object>());
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{role} build {report.summary.result} with {report.summary.totalErrors} errors.");
            Debug.Log($"Built {role} APK at {output} ({report.summary.totalSize} bytes).");
        }
    }
}
