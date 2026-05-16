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
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               (request.Url.AbsolutePath == "/api/assets/prefabs/apply" ||
                request.Url.AbsolutePath == "/api/assets/prefabs/revert");

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body   = RequestBodyReader.ReadString(request);
            var goPath = RequestBodyReader.GetString(body, "goPath");

            if (string.IsNullOrEmpty(goPath))
            {
                RestResponse.SendError(response, "Missing required field: goPath", 400);
                return;
            }

            var go = GameObjectUtils.FindByPath(goPath);
            if (go == null)
            {
                RestResponse.SendNotFound(response, $"GameObject not found at path: {goPath}");
                return;
            }

            if (!PrefabUtility.IsPartOfAnyPrefab(go))
            {
                RestResponse.SendError(response,
                    $"GameObject is not a prefab instance: {goPath}", 400);
                return;
            }

            var prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);

            if (request.Url.AbsolutePath == "/api/assets/prefabs/apply")
                HandleApply(go, goPath, prefabAssetPath, response);
            else
                HandleRevert(go, goPath, prefabAssetPath, response);
        }

        private static void HandleApply(
            GameObject go, string goPath, string prefabAssetPath, HttpListenerResponse response)
        {
            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            RestResponse.Send(response,
                $"{{\"applied\":\"{RestResponse.EscapeJson(goPath)}\"," +
                $"\"prefabAssetPath\":\"{RestResponse.EscapeJson(prefabAssetPath)}\"}}");
        }

        private static void HandleRevert(
            GameObject go, string goPath, string prefabAssetPath, HttpListenerResponse response)
        {
            PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            RestResponse.Send(response,
                $"{{\"reverted\":\"{RestResponse.EscapeJson(goPath)}\"," +
                $"\"prefabAssetPath\":\"{RestResponse.EscapeJson(prefabAssetPath)}\"}}");
        }
    }
}
