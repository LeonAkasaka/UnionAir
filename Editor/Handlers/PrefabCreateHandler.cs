using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/assets/prefabs
    /// Creates or overwrites a prefab asset from a scene GameObject.
    /// Body: { "goPath": "...", "assetPath": "Assets/Prefabs/Name.prefab", "mode": "new|replace" }
    /// </summary>
    internal class PrefabCreateHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               request.Url.AbsolutePath == "/api/assets/prefabs";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body      = RequestBodyReader.ReadString(request);
            var goPath    = RequestBodyReader.GetString(body, "goPath");
            var assetPath = RequestBodyReader.GetString(body, "assetPath");
            var mode      = RequestBodyReader.GetString(body, "mode") ?? "new";

            if (string.IsNullOrEmpty(goPath))
            {
                RestResponse.SendError(response, "Missing required field: goPath", 400);
                return;
            }
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendError(response, "Missing required field: assetPath", 400);
                return;
            }
            if (!assetPath.EndsWith(".prefab"))
            {
                RestResponse.SendError(response, "assetPath must end with .prefab", 400);
                return;
            }

            var go = GameObjectUtils.FindByPath(goPath);
            if (go == null)
            {
                RestResponse.SendNotFound(response, $"GameObject not found at path: {goPath}");
                return;
            }

            // Ensure parent directory exists
            var dir = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
                var current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }

            GameObject saved;
            if (mode == "replace")
            {
                saved = PrefabUtility.SaveAsPrefabAsset(go, assetPath);
            }
            else
            {
                // "new" mode: save and connect so the scene instance becomes a prefab instance
                saved = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    go, assetPath, InteractionMode.AutomatedAction);
            }

            if (saved == null)
            {
                RestResponse.SendError(response, $"Failed to save prefab to: {assetPath}", 500);
                return;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            RestResponse.Send(response,
                $"{{\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",\"guid\":\"{RestResponse.EscapeJson(guid)}\"}}", 201);
        }
    }
}
