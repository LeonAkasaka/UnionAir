using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Shared scene-traversal and serialized-property utilities used by search handlers.
    /// </summary>
    internal static class SceneUtils
    {
        /// <summary>
        /// Returns a flat list of every GameObject in the scene together with its
        /// root-relative slash-separated path (e.g. "Canvas/Panel/Button").
        /// </summary>
        /// <param name="scene">Scene to traverse.</param>
        /// <returns>All GameObjects in the scene with their hierarchy paths.</returns>
        public static List<(GameObject go, string path)> GetAllGameObjects(Scene scene)
        {
            var result = new List<(GameObject, string)>();
            foreach (var root in scene.GetRootGameObjects())
                CollectRecursive(root, root.name, result);
            return result;
        }

        private static void CollectRecursive(
            GameObject go, string path, List<(GameObject, string)> result)
        {
            result.Add((go, path));
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i).gameObject;
                CollectRecursive(child, path + "/" + child.name, result);
            }
        }

        /// <summary>
        /// Scans all serialized properties of <paramref name="component"/> and returns
        /// the names of every <see cref="SerializedPropertyType.ObjectReference"/> property
        /// whose referenced asset GUID matches <paramref name="assetGuid"/>.
        /// Returns an empty list when no match is found or on serialization errors.
        /// </summary>
        /// <param name="component">Component whose serialized object references should be inspected.</param>
        /// <param name="assetGuid">Asset GUID to search for.</param>
        /// <returns>Serialized property names that reference the asset.</returns>
        public static List<string> FindAssetRefsInComponent(Component component, string assetGuid)
        {
            var matches = new List<string>();
            if (component == null || string.IsNullOrEmpty(assetGuid)) return matches;

            try
            {
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                bool enterChildren = true;

                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (prop.objectReferenceValue == null) continue;

                    var refPath = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                    if (string.IsNullOrEmpty(refPath)) continue;

                    var refGuid = AssetDatabase.AssetPathToGUID(refPath);
                    if (refGuid == assetGuid)
                        matches.Add(prop.name);
                }
            }
            catch
            {
                // Ignore serialization errors for exotic component types
            }

            return matches;
        }

        /// <summary>
        /// Returns true if the component has any serialized ObjectReference property
        /// pointing to the asset identified by <paramref name="assetGuid"/>.
        /// Cheaper than <see cref="FindAssetRefsInComponent"/> when only presence matters.
        /// </summary>
        /// <param name="component">Component whose serialized object references should be inspected.</param>
        /// <param name="assetGuid">Asset GUID to search for.</param>
        /// <returns>True when at least one serialized property references the asset.</returns>
        public static bool ComponentReferencesAsset(Component component, string assetGuid)
        {
            if (component == null || string.IsNullOrEmpty(assetGuid)) return false;

            try
            {
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                bool enterChildren = true;

                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (prop.objectReferenceValue == null) continue;

                    var refPath = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                    if (string.IsNullOrEmpty(refPath)) continue;

                    if (AssetDatabase.AssetPathToGUID(refPath) == assetGuid)
                        return true;
                }
            }
            catch { /* ignored */ }

            return false;
        }
    }
}
