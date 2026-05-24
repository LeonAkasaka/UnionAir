using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles prefab override operations on scene instances:
    ///   POST /api/assets/prefabs/apply  — apply all instance overrides to the prefab asset
    ///   POST /api/assets/prefabs/revert — revert instance to match the prefab asset
    /// Body: { "goPath": "..." }
    /// </summary>
    internal class PrefabOverrideHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               (request.Url.AbsolutePath == "/api/assets/prefabs/apply" ||
                request.Url.AbsolutePath == "/api/assets/prefabs/revert");

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body   = RequestBodyReader.ReadString(request);
            var goPath = RequestBodyReader.GetString(body, "goPath");
            var globalObjectId = RequestBodyReader.GetString(body, "globalObjectId");
            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            if (!GameObjectUtils.TryResolveTarget(scene, globalObjectId, goPath, "prefab instance", out var go, out var error, out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;
            goPath = GameObjectUtils.GetPath(go);

            if (!PrefabUtility.IsPartOfAnyPrefab(go))
            {
                RestResponse.SendError(response,
                    $"GameObject is not a prefab instance: {goPath}", 400);
                return;
            }

            var prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);

            if (request.Url.AbsolutePath == "/api/assets/prefabs/apply")
                HandleApply(go, goPath, prefabAssetPath, scene, response);
            else
                HandleRevert(go, goPath, prefabAssetPath, scene, response);
        }

        private static void HandleApply(
            GameObject go, string goPath, string prefabAssetPath, UnityEngine.SceneManagement.Scene scene, HttpListenerResponse response)
        {
            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(scene);

            RestResponse.Send(response,
                $"{{\"applied\":\"{RestResponse.EscapeJson(goPath)}\"," +
                $"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\"," +
                $"\"prefabAssetPath\":\"{RestResponse.EscapeJson(prefabAssetPath)}\"}}");
        }

        private static void HandleRevert(
            GameObject go, string goPath, string prefabAssetPath, UnityEngine.SceneManagement.Scene scene, HttpListenerResponse response)
        {
            PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(scene);

            RestResponse.Send(response,
                $"{{\"reverted\":\"{RestResponse.EscapeJson(goPath)}\"," +
                $"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\"," +
                $"\"prefabAssetPath\":\"{RestResponse.EscapeJson(prefabAssetPath)}\"}}");
        }
    }
}
