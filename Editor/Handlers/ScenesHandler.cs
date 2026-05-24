using System;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles loaded scene listing and scene lifecycle operations.
    /// </summary>
    internal sealed class ScenesHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.Url.AbsolutePath == "/api/scenes" ||
               request.Url.AbsolutePath == "/api/scenes/new" ||
               request.Url.AbsolutePath == "/api/scenes/open" ||
               request.Url.AbsolutePath == "/api/scenes/unload" ||
               request.Url.AbsolutePath == "/api/scenes/active";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/scenes")
            {
                HandleList(response);
                return;
            }

            if (request.HttpMethod != "POST")
            {
                RestResponse.SendError(response, "Method not allowed", 405);
                return;
            }

            switch (request.Url.AbsolutePath)
            {
                case "/api/scenes/new":
                    HandleNew(request, response);
                    break;
                case "/api/scenes/open":
                    HandleOpen(request, response);
                    break;
                case "/api/scenes/unload":
                    HandleUnload(request, response);
                    break;
                case "/api/scenes/active":
                    HandleActive(request, response);
                    break;
                default:
                    RestResponse.SendNotFound(response, "Endpoint not found.");
                    break;
            }
        }

        private static void HandleList(HttpListenerResponse response)
        {
            var active = EditorSceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"activeScene\":\"{RestResponse.EscapeJson(SceneResolver.GetIdentifier(active))}\",");
            sb.Append("\"scenes\":[");

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (i > 0) sb.Append(",");
                var scene = SceneManager.GetSceneAt(i);
                SceneResolver.AppendScene(sb, scene, scene == active);
            }

            sb.Append($"],\"count\":{SceneManager.sceneCount}");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void HandleNew(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var mode = RequestBodyReader.GetString(body, "mode") ?? "single";
            var setup = RequestBodyReader.GetString(body, "setup") ?? "default";
            var discardUnsaved = RequestBodyReader.GetBool(body, "discardUnsaved") == true;

            if (!TryParseNewSceneMode(mode, out var newSceneMode))
            {
                RestResponse.SendError(response, "Invalid mode. Expected single or additive.", 400);
                return;
            }

            if (!TryParseNewSceneSetup(setup, out var newSceneSetup))
            {
                RestResponse.SendError(response, "Invalid setup. Expected default or empty.", 400);
                return;
            }

            if (newSceneMode == NewSceneMode.Single && !discardUnsaved && SceneResolver.HasDirtyLoadedScenes())
            {
                RestResponse.SendError(response,
                    "Cannot create a single new scene while loaded scenes have unsaved changes. Pass discardUnsaved: true to override.",
                    409);
                return;
            }

            var scene = EditorSceneManager.NewScene(newSceneSetup, newSceneMode);
            SendSceneResponse(response, "created", scene, 201);
        }

        private static void HandleOpen(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var path = RequestBodyReader.GetString(body, "path");
            var mode = RequestBodyReader.GetString(body, "mode") ?? "single";
            var discardUnsaved = RequestBodyReader.GetBool(body, "discardUnsaved") == true;

            if (string.IsNullOrEmpty(path))
            {
                RestResponse.SendError(response, "Missing required field: path", 400);
                return;
            }

            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                RestResponse.SendError(response, "path must point to a .unity scene asset.", 400);
                return;
            }

            if (AssetDatabase.GetMainAssetTypeAtPath(path) == null)
            {
                RestResponse.SendNotFound(response, $"Scene asset not found: {path}");
                return;
            }

            if (!TryParseOpenSceneMode(mode, out var openMode))
            {
                RestResponse.SendError(response, "Invalid mode. Expected single or additive.", 400);
                return;
            }

            if (openMode == OpenSceneMode.Single && !discardUnsaved && SceneResolver.HasDirtyLoadedScenes())
            {
                RestResponse.SendError(response,
                    "Cannot open a scene in single mode while loaded scenes have unsaved changes. Pass discardUnsaved: true to override.",
                    409);
                return;
            }

            var scene = EditorSceneManager.OpenScene(path, openMode);
            SendSceneResponse(response, "opened", scene, 200);
        }

        private static void HandleUnload(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var identifier = RequestBodyReader.GetString(body, "path") ?? RequestBodyReader.GetString(body, "name");
            var discardUnsaved = RequestBodyReader.GetBool(body, "discardUnsaved") == true;

            if (string.IsNullOrEmpty(identifier))
            {
                RestResponse.SendError(response, "Missing required field: path or name", 400);
                return;
            }

            if (!SceneResolver.TryResolveRequired(identifier, response, out var scene))
                return;

            if (scene.isDirty && !discardUnsaved)
            {
                RestResponse.SendError(response,
                    "Cannot unload a scene with unsaved changes. Pass discardUnsaved: true to override.",
                    409);
                return;
            }

            var id = SceneResolver.GetIdentifier(scene);
            var closed = EditorSceneManager.CloseScene(scene, true);
            if (!closed)
            {
                RestResponse.SendError(response, $"Failed to unload scene: {id}", 500);
                return;
            }

            RestResponse.Send(response, $"{{\"unloaded\":\"{RestResponse.EscapeJson(id)}\"}}");
        }

        private static void HandleActive(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var identifier = RequestBodyReader.GetString(body, "path") ?? RequestBodyReader.GetString(body, "name");

            if (string.IsNullOrEmpty(identifier))
            {
                RestResponse.SendError(response, "Missing required field: path or name", 400);
                return;
            }

            if (!SceneResolver.TryResolveRequired(identifier, response, out var scene))
                return;

            if (!EditorSceneManager.SetActiveScene(scene))
            {
                RestResponse.SendError(response, $"Failed to set active scene: {identifier}", 500);
                return;
            }

            SendSceneResponse(response, "active", scene, 200);
        }

        private static void SendSceneResponse(HttpListenerResponse response, string key, Scene scene, int status)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"{key}\":");
            SceneResolver.AppendScene(sb, scene, scene == EditorSceneManager.GetActiveScene());
            sb.Append("}");
            RestResponse.Send(response, sb.ToString(), status);
        }

        private static bool TryParseNewSceneMode(string mode, out NewSceneMode parsed)
        {
            if (string.Equals(mode, "single", StringComparison.OrdinalIgnoreCase))
            {
                parsed = NewSceneMode.Single;
                return true;
            }

            if (string.Equals(mode, "additive", StringComparison.OrdinalIgnoreCase))
            {
                parsed = NewSceneMode.Additive;
                return true;
            }

            parsed = NewSceneMode.Single;
            return false;
        }

        private static bool TryParseOpenSceneMode(string mode, out OpenSceneMode parsed)
        {
            if (string.Equals(mode, "single", StringComparison.OrdinalIgnoreCase))
            {
                parsed = OpenSceneMode.Single;
                return true;
            }

            if (string.Equals(mode, "additive", StringComparison.OrdinalIgnoreCase))
            {
                parsed = OpenSceneMode.Additive;
                return true;
            }

            parsed = OpenSceneMode.Single;
            return false;
        }

        private static bool TryParseNewSceneSetup(string setup, out NewSceneSetup parsed)
        {
            if (string.Equals(setup, "default", StringComparison.OrdinalIgnoreCase))
            {
                parsed = NewSceneSetup.DefaultGameObjects;
                return true;
            }

            if (string.Equals(setup, "empty", StringComparison.OrdinalIgnoreCase))
            {
                parsed = NewSceneSetup.EmptyScene;
                return true;
            }

            parsed = NewSceneSetup.DefaultGameObjects;
            return false;
        }
    }
}
