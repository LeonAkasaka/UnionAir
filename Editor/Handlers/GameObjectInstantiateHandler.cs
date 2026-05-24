using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/instantiate
    /// Instantiates a prefab asset into the active scene while maintaining the prefab connection.
    /// Body: { "guid": "...", "assetPath": "...", "name": "...", "parentPath": "..." }
    /// Either "guid" or "assetPath" is required; "guid" takes precedence when both are provided.
    /// </summary>
    internal class GameObjectInstantiateHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               request.Url.AbsolutePath == "/api/gameobjects/instantiate";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body       = RequestBodyReader.ReadString(request);
            var guid       = RequestBodyReader.GetString(body, "guid");
            var assetPath  = RequestBodyReader.GetString(body, "assetPath");
            var name       = RequestBodyReader.GetString(body, "name");
            var parentPath = RequestBodyReader.GetString(body, "parentPath");
            var parentGlobalObjectId = RequestBodyReader.GetString(body, "parentGlobalObjectId");
            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            // Resolve asset path from guid or assetPath
            if (!string.IsNullOrEmpty(guid))
            {
                var resolved = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(resolved))
                {
                    RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                    return;
                }
                assetPath = resolved;
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendError(response,
                    "Missing required field: guid or assetPath", 400);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                RestResponse.SendError(response,
                    $"Asset at path is not a GameObject/prefab: {assetPath}", 400);
                return;
            }

            // Resolve optional parent transform
            Transform parentTransform = null;
            if (!string.IsNullOrEmpty(parentGlobalObjectId) || !string.IsNullOrEmpty(parentPath))
            {
                if (!GameObjectUtils.TryResolveTarget(scene, parentGlobalObjectId, parentPath, "parent", out var parentGo, out var error, out var statusCode))
                {
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }
                scene = parentGo.scene;
                parentTransform = parentGo.transform;
            }

            Undo.SetCurrentGroupName($"UnionAir: Instantiate {prefab.name}");
            var group = Undo.GetCurrentGroup();

            // InstantiatePrefab maintains the prefab connection (unlike Instantiate)
            var instance = parentTransform != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parentTransform)
                : (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            if (instance == null)
            {
                RestResponse.SendError(response, "Failed to instantiate prefab.", 500);
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, $"UnionAir: Instantiate {prefab.name}");

            if (!string.IsNullOrEmpty(name))
                instance.name = name;

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);

            var instancePath = GameObjectUtils.GetPath(instance);
            var components   = instance.GetComponents<Component>();

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(instance.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(instancePath)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(instance))}\",");
            sb.Append($"\"prefabAssetPath\":\"{RestResponse.EscapeJson(assetPath)}\",");
            sb.Append("\"components\":[");
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(components[i].GetType().Name)}\"");
            }
            sb.Append("]}");

            RestResponse.Send(response, sb.ToString(), 201);
        }
    }
}
