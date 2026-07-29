using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    internal sealed class LoadedSceneAssetConflict
    {
        internal string path;
        internal string name;
        internal bool isDirty;
        internal bool isActive;
    }

    /// <summary>
    /// Finds loaded scenes targeted by an asset or recursive folder operation.
    /// </summary>
    internal static class LoadedSceneAssetSafety
    {
        internal static List<LoadedSceneAssetConflict> FindLoadedSceneConflicts(
            string assetPath,
            bool recursive)
        {
            var conflicts = new List<LoadedSceneAssetConflict>();
            var activeScene = EditorSceneManager.GetActiveScene();

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded ||
                    string.IsNullOrEmpty(scene.path) ||
                    !IsScenePathTargeted(assetPath, recursive, scene.path))
                    continue;

                conflicts.Add(new LoadedSceneAssetConflict
                {
                    path = scene.path,
                    name = scene.name,
                    isDirty = scene.isDirty,
                    isActive = scene == activeScene,
                });
            }

            return conflicts;
        }

        internal static bool IsScenePathTargeted(
            string assetPath,
            bool recursive,
            string scenePath)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(scenePath))
                return false;

            var normalizedAssetPath = assetPath.Replace('\\', '/').TrimEnd('/');
            var normalizedScenePath = scenePath.Replace('\\', '/');
            if (!normalizedScenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(
                    normalizedAssetPath,
                    normalizedScenePath,
                    StringComparison.OrdinalIgnoreCase))
                return true;

            return recursive &&
                   normalizedScenePath.StartsWith(
                       normalizedAssetPath + "/",
                       StringComparison.OrdinalIgnoreCase);
        }

        internal static void AppendLoadedScenesJson(
            StringBuilder sb,
            IReadOnlyList<LoadedSceneAssetConflict> loadedScenes)
        {
            sb.Append("[");
            for (var i = 0; i < loadedScenes.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var scene = loadedScenes[i];
                sb.Append("{\"path\":\"");
                sb.Append(RestResponse.EscapeJson(scene.path));
                sb.Append("\",\"name\":\"");
                sb.Append(RestResponse.EscapeJson(scene.name));
                sb.Append("\",\"isDirty\":");
                sb.Append(RestResponse.FormatBool(scene.isDirty));
                sb.Append(",\"isActive\":");
                sb.Append(RestResponse.FormatBool(scene.isActive));
                sb.Append("}");
            }
            sb.Append("]");
        }
    }
}
