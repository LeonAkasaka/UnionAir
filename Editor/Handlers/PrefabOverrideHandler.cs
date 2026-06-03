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
    /// Body: { "source": {"value": "..."} }
    /// </summary>
    internal class PrefabOverrideHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body   = RequestBodyReader.ReadString(request);
            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            if (!ObjectRefUtils.TryReadBody(body, "source", out var source, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObject(scene, source, "source", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;
            var goPath = GameObjectUtils.GetPath(go);

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
