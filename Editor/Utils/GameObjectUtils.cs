using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        /// <param name="path">Slash-separated hierarchy path in the active scene.</param>
        /// <returns>The matching GameObject, or null when no object exists at the path.</returns>
        public static GameObject FindByPath(string path)
        {
            var scene = EditorSceneManager.GetActiveScene();
            return FindByPath(scene, path);
        }

        /// <summary>
        /// Finds a GameObject in the given scene by its slash-separated hierarchy path.
        /// </summary>
        /// <param name="scene">Loaded scene to search.</param>
        /// <param name="path">Slash-separated hierarchy path in the scene.</param>
        /// <returns>The matching GameObject, or null when no object exists at the path.</returns>
        public static GameObject FindByPath(Scene scene, string path)
        {
            if (string.IsNullOrEmpty(path) || !scene.IsValid() || !scene.isLoaded) return null;

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
        /// <param name="go">GameObject whose hierarchy path should be returned.</param>
        /// <returns>Slash-separated hierarchy path, or an empty string when <paramref name="go"/> is null.</returns>
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

        /// <summary>
        /// Applies position/rotation/scale from a JSON body's "transform" object to the given Transform.
        /// Each axis is optional; missing axes are left unchanged. Expects localPosition, localEulerAngles,
        /// and localScale semantics.
        /// </summary>
        public static void ApplyTransformFromBody(Transform t, string body)
        {
            var transformJson = RequestBodyReader.GetObject(body, "transform");
            if (transformJson == null) return;

            var posJson = RequestBodyReader.GetObject(transformJson, "position");
            if (posJson != null)
            {
                var x = RequestBodyReader.GetFloat(posJson, "x");
                var y = RequestBodyReader.GetFloat(posJson, "y");
                var z = RequestBodyReader.GetFloat(posJson, "z");
                t.localPosition = new Vector3(
                    x ?? t.localPosition.x,
                    y ?? t.localPosition.y,
                    z ?? t.localPosition.z);
            }

            var rotJson = RequestBodyReader.GetObject(transformJson, "rotation");
            if (rotJson != null)
            {
                var x = RequestBodyReader.GetFloat(rotJson, "x");
                var y = RequestBodyReader.GetFloat(rotJson, "y");
                var z = RequestBodyReader.GetFloat(rotJson, "z");
                t.localEulerAngles = new Vector3(
                    x ?? t.localEulerAngles.x,
                    y ?? t.localEulerAngles.y,
                    z ?? t.localEulerAngles.z);
            }

            var scaleJson = RequestBodyReader.GetObject(transformJson, "scale");
            if (scaleJson != null)
            {
                var x = RequestBodyReader.GetFloat(scaleJson, "x");
                var y = RequestBodyReader.GetFloat(scaleJson, "y");
                var z = RequestBodyReader.GetFloat(scaleJson, "z");
                t.localScale = new Vector3(
                    x ?? t.localScale.x,
                    y ?? t.localScale.y,
                    z ?? t.localScale.z);
            }
        }

    }
}
