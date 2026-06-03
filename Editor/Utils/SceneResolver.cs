using System;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Resolves loaded Unity scenes from API query and body fields.
    /// </summary>
    internal static class SceneResolver
    {
        public static bool TryResolveFromRequest(
            HttpListenerRequest request,
            HttpListenerResponse response,
            string body,
            out Scene scene)
        {
            var scenePath = request.QueryString["scenePath"];
            if (string.IsNullOrEmpty(scenePath) && !string.IsNullOrEmpty(body))
                scenePath = RequestBodyReader.GetString(body, "scenePath");

            return TryResolveOptional(scenePath, response, out scene);
        }

        public static bool TryResolveOptional(
            string scenePathOrName,
            HttpListenerResponse response,
            out Scene scene)
        {
            if (string.IsNullOrEmpty(scenePathOrName))
            {
                scene = EditorSceneManager.GetActiveScene();
                return true;
            }

            return TryResolveRequired(scenePathOrName, response, out scene);
        }

        public static bool TryResolveRequired(
            string pathOrName,
            HttpListenerResponse response,
            out Scene scene)
        {
            var status = ResolveLoaded(pathOrName, out scene, out var error);
            if (status == ResolveStatus.Found) return true;

            RestResponse.SendError(response, error, status == ResolveStatus.Ambiguous ? 409 : 404);
            return false;
        }

        public static ResolveStatus ResolveLoaded(string pathOrName, out Scene scene, out string error)
        {
            scene = default(Scene);
            error = null;

            if (string.IsNullOrEmpty(pathOrName))
            {
                error = "Missing scene path or name.";
                return ResolveStatus.NotFound;
            }

            Scene match = default(Scene);
            int matches = 0;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var candidate = SceneManager.GetSceneAt(i);
                if (!candidate.isLoaded) continue;

                if (string.Equals(candidate.path, pathOrName, StringComparison.OrdinalIgnoreCase))
                {
                    scene = candidate;
                    return ResolveStatus.Found;
                }

                if (string.Equals(candidate.name, pathOrName, StringComparison.OrdinalIgnoreCase))
                {
                    match = candidate;
                    matches++;
                }
            }

            if (matches == 1)
            {
                scene = match;
                return ResolveStatus.Found;
            }

            if (matches > 1)
            {
                error = $"Scene name is ambiguous: {pathOrName}. Use scenePath with an asset path.";
                return ResolveStatus.Ambiguous;
            }

            error = $"Loaded scene not found: {pathOrName}";
            return ResolveStatus.NotFound;
        }

        public static string GetIdentifier(Scene scene)
            => string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;

        public static bool HasDirtyLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.isDirty) return true;
            }

            return false;
        }

        public static void AppendScene(StringBuilder sb, Scene scene, bool active)
        {
            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(scene.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(scene.path)}\",");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(AssetDatabase.AssetPathToGUID(scene.path))}\",");
            sb.Append($"\"buildIndex\":{scene.buildIndex},");
            sb.Append($"\"isDirty\":{RestResponse.FormatBool(scene.isDirty)},");
            sb.Append($"\"isLoaded\":{RestResponse.FormatBool(scene.isLoaded)},");
            sb.Append($"\"isActive\":{RestResponse.FormatBool(active)},");
            sb.Append($"\"rootCount\":{scene.rootCount}");
            sb.Append("}");
        }

    }

    internal enum ResolveStatus
    {
        Found,
        NotFound,
        Ambiguous
    }
}
