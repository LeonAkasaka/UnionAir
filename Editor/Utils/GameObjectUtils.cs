using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Shared helpers for locating and identifying GameObjects used by write handlers.
    /// </summary>
    internal static class GameObjectUtils
    {
        /// <summary>
        /// Finds a GameObject in the active scene by its slash-separated hierarchy path
        /// (e.g. "Canvas/Panel/Button"). Returns null if not found.
        /// </summary>
        public static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var scene = EditorSceneManager.GetActiveScene();
            var parts = path.Split('/');

            GameObject current = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == parts[0]) { current = root; break; }
            }

            if (current == null) return null;

            for (int i = 1; i < parts.Length; i++)
            {
                var child = current.transform.Find(parts[i]);
                if (child == null) return null;
                current = child.gameObject;
            }

            return current;
        }

        /// <summary>
        /// Returns the slash-separated hierarchy path of a GameObject
        /// (e.g. "Canvas/Panel/Button").
        /// </summary>
        public static string GetPath(GameObject go)
        {
            if (go == null) return string.Empty;

            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
