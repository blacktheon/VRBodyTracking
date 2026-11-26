using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Blacktheon.BodyTracking.Demo.Editor
{
    public static class ClimbTabletExport
    {
        const string LayoutPath = "Assets/BodyTrackingDemo/Runtime/VehicleLadder.prefab";
        const string MaterialFolder = "Assets/BodyTrackingDemo/Runtime/VehicleLadderMaterials";

        public static void Export()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-bodyTrackingTabletAssets");
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Supply -bodyTrackingTabletAssets followed by the output directory.");
            ExportTo(args[index + 1]);
        }

        // Called in the full authoring project so FBX material remaps and nested prefab overrides resolve.
        public static void ExportTo(string outputDirectory)
        {
            string output = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string assets = Path.GetFullPath(Application.dataPath);
            string project = Directory.GetParent(assets).FullName;
            if (string.Equals(output, project, StringComparison.OrdinalIgnoreCase)
                || string.Equals(output, assets, StringComparison.OrdinalIgnoreCase)
                || output.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Export into a separate Tablet staging directory.");

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPath).GetComponent<ClimbLadder>();
            string scratch = AssetDatabase.GenerateUniqueAssetPath("Assets/TabletClimbExport");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(scratch));
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var projection = new Projection(source, scene, scratch);
                GameObject root = projection.Create();
                string portable = scratch + "/VehicleLadder.prefab";
                // Saving over a copy preserves the local ids addressed by the two scene instances.
                if (!AssetDatabase.CopyAsset(LayoutPath, portable)) throw new IOException("Could not copy the layout.");
                var saved = PrefabUtility.SaveAsPrefabAsset(root, portable, out bool success);
                if (!success || saved == null) throw new IOException("Could not serialize the Tablet layout.");
                if (LocalId(saved) != LocalId(source.gameObject) || LocalId(saved.transform) != LocalId(source.transform)
                    || LocalId(saved.GetComponent<ClimbLadder>()) != LocalId(source))
                    throw new InvalidDataException("The exported layout lost its scene-facing identifiers.");

                foreach (string original in projection.Sources)
                    CopyFile(project, original, original, output);
                foreach (string material in AssetDatabase.FindAssets("t:Material", new[] { scratch + "/Materials" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(material);
                    CopyFile(project, path, MaterialFolder + "/" + Path.GetFileName(path), output);
                }
                string destination = Path.Combine(output, LayoutPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(Path.Combine(project, portable), destination, true);
                File.Copy(Path.Combine(project, LayoutPath + ".meta"), destination + ".meta", true);
                File.Copy(Path.Combine(project, scratch + "/Materials.meta"), Path.Combine(output, MaterialFolder + ".meta"), true);
                Debug.Log($"Exported {saved.GetComponentsInChildren<MeshRenderer>(true).Length} renderers and " +
                    $"{source.RungCount} climbable rungs to {output}.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                AssetDatabase.DeleteAsset(scratch);
            }
        }

        static long LocalId(Object asset)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id);
            return id;
        }

        static void CopyFile(string project, string source, string relative, string output)
        {
            source = Path.Combine(project, source);
            string target = Path.Combine(output, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, true);
            File.Copy(source + ".meta", target + ".meta", true);
        }

        sealed class Projection
        {
            readonly ClimbLadder source;
            readonly Scene scene;
            readonly string materialsPath;
            readonly HashSet<Transform> keep = new();
            readonly Dictionary<Object, Object> copies = new();
            readonly Dictionary<Material, Material> materials = new();
            public readonly HashSet<string> Sources = new();

            public Projection(ClimbLadder source, Scene scene, string scratch)
            {
                this.source = source;
                this.scene = scene;
                materialsPath = scratch + "/Materials";
                AssetDatabase.CreateFolder(scratch, "Materials");
            }

            public GameObject Create()
            {
                foreach (var group in new[] { source.VehicleRoot, source.LadderRoot })
                {
                    Keep(group);
                    foreach (var mesh in group.GetComponentsInChildren<MeshFilter>(true)) Keep(mesh.transform);
                    foreach (var collider in group.GetComponentsInChildren<Collider>(true)) Keep(collider.transform);
                }
                Keep(source.TopBar);
                foreach (var light in source.GetComponentsInChildren<Light>(true))
                    if (light.type == LightType.Directional) Keep(light.transform);

                Transform root = CopyBranch(source.transform, null);
                var layout = root.gameObject.AddComponent<ClimbLadder>();
                var original = new SerializedObject(source);
                var copy = new SerializedObject(layout);
                foreach (string name in new[] { "vehicleRoot", "ladderRoot", "topBar" })
                    copy.FindProperty(name).objectReferenceValue = copies[original.FindProperty(name).objectReferenceValue];
                var rungs = copy.FindProperty("rungs");
                var originalRungs = original.FindProperty("rungs");
                rungs.arraySize = originalRungs.arraySize;
                for (int i = 0; i < rungs.arraySize; i++)
                    rungs.GetArrayElementAtIndex(i).objectReferenceValue =
                        copies[originalRungs.GetArrayElementAtIndex(i).objectReferenceValue];
                copy.ApplyModifiedPropertiesWithoutUndo();
                return root.gameObject;
            }

            void Keep(Transform node)
            {
                while (node != null && keep.Add(node)) node = node.parent;
            }

            Transform CopyBranch(Transform original, Transform parent)
            {
                var go = new GameObject(original.name);
                SceneManager.MoveGameObjectToScene(go, scene);
                var node = go.transform;
                node.SetParent(parent, false);
                node.SetLocalPositionAndRotation(original.localPosition, original.localRotation);
                node.localScale = original.localScale;
                copies.Add(original, node);
                go.layer = original.gameObject.layer;
                go.SetActive(original.gameObject.activeSelf);

                foreach (var component in original.GetComponents<Component>())
                {
                    if (component is MeshFilter filter)
                    {
                        go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                        Include(filter.sharedMesh);
                    }
                    else if (component is MeshRenderer renderer)
                    {
                        var copy = go.AddComponent<MeshRenderer>();
                        EditorUtility.CopySerialized(renderer, copy);
                        copy.probeAnchor = null;
                        copy.lightProbeProxyVolumeOverride = null;
                        copy.sharedMaterials = renderer.sharedMaterials.Select(PortableMaterial).ToArray();
                    }
                    else if (component is Collider collider)
                    {
                        var copy = go.AddComponent(collider.GetType());
                        EditorUtility.CopySerialized(collider, copy);
                        copies.Add(collider, copy);
                        Include(collider.sharedMaterial);
                        if (collider is MeshCollider mesh) Include(mesh.sharedMesh);
                    }
                    else if (component is Light light && light.type == LightType.Directional)
                    {
                        EditorUtility.CopySerialized(light, go.AddComponent<Light>());
                        Include(light.cookie);
                    }
                }
                foreach (Transform child in original)
                    if (keep.Contains(child)) CopyBranch(child, node);
                return node;
            }

            void Include(Object asset)
            {
                if (asset == null) return;
                string path = AssetDatabase.GetAssetPath(asset);
                if (path.StartsWith("Assets/", StringComparison.Ordinal)) Sources.Add(path);
            }

            Material PortableMaterial(Material original)
            {
                if (original == null) throw new InvalidDataException("A visible climb layout mesh has no material.");
                if (materials.TryGetValue(original, out var result)) return result;
                bool physical = original.HasProperty("_BASE_COLOR_MAP");
                var shader = Shader.Find(physical ? "Autodesk Interactive" : "Standard");
                if (shader == null) throw new InvalidDataException("The Unity built-in material shader is unavailable.");
                result = new Material(shader) { name = original.name };
                string name = string.Join("_", original.name.Split(Path.GetInvalidFileNameChars()));
                AssetDatabase.CreateAsset(result, AssetDatabase.GenerateUniqueAssetPath(materialsPath + "/" + name + ".mat"));
                materials.Add(original, result);

                void Texture(string from, string to, string keyword = null)
                {
                    if (!original.HasProperty(from) || original.GetTexture(from) == null) return;
                    var texture = original.GetTexture(from);
                    result.SetTexture(to, texture);
                    result.SetTextureOffset(to, original.GetTextureOffset(from));
                    result.SetTextureScale(to, original.GetTextureScale(from));
                    if (keyword != null) result.EnableKeyword(keyword);
                    Include(texture);
                }
                void Scalar(string from, string to)
                {
                    if (original.HasProperty(from)) result.SetFloat(to, original.GetFloat(from));
                }
                void Color(string from, string to)
                {
                    if (original.HasProperty(from)) result.SetColor(to, original.GetColor(from));
                }
                Color(physical ? "_BASE_COLOR" : original.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", "_Color");
                Texture(physical ? "_BASE_COLOR_MAP" : original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex", "_MainTex");
                Scalar(physical ? "_METALNESS" : "_Metallic", "_Metallic");
                Texture(physical ? "_METALNESS_MAP" : "_MetallicGlossMap", "_MetallicGlossMap", "_METALLICGLOSSMAP");
                Scalar(physical ? "_BUMP_MAP_STRENGTH" : "_BumpScale", "_BumpScale");
                Texture(physical ? "_BUMP_MAP" : "_BumpMap", "_BumpMap", "_NORMALMAP");
                Scalar(physical ? "_REFLECTIONS_ROUGHNESS" : "_Smoothness", "_Glossiness");
                if (physical) Texture("_REFLECTIONS_ROUGHNESS_MAP", "_SpecGlossMap", "_SPECGLOSSMAP");
                else Scalar("_Smoothness", "_GlossMapScale");
                Texture("_OcclusionMap", "_OcclusionMap");
                Color(physical ? "_EMISSION_COLOR" : "_EmissionColor", "_EmissionColor");
                Texture(physical ? "_EMISSION_COLOR_MAP" : "_EmissionMap", "_EmissionMap", "_EMISSION");
                EditorUtility.SetDirty(result);
                AssetDatabase.SaveAssetIfDirty(result);
                return result;
            }
        }
    }
}
