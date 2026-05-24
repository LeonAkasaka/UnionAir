using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/reparent
    /// Moves a GameObject under a new parent (or to the root when parentPath is omitted).
    /// Body: { "path": "...", "parentPath": "..." }
    /// </summary>
    internal class GameObjectReparentHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               request.Url.AbsolutePath == "/api/gameobjects/reparent";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var path = RequestBodyReader.GetString(body, "path");
            var globalObjectId = RequestBodyReader.GetString(body, "globalObjectId");

            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            if (!GameObjectUtils.TryResolveTarget(scene, globalObjectId, path, "target", out var go, out var error, out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;
            var originalScene = go.scene;

            var parentPath = RequestBodyReader.GetString(body, "parentPath");
            var parentGlobalObjectId = RequestBodyReader.GetString(body, "parentGlobalObjectId");
            Transform newParent = null;

            if (!string.IsNullOrEmpty(parentGlobalObjectId) || !string.IsNullOrEmpty(parentPath))
            {
                if (!GameObjectUtils.TryResolveTarget(scene, parentGlobalObjectId, parentPath, "parent", out var parentGo, out error, out statusCode))
                {
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }
                scene = parentGo.scene;
                newParent = parentGo.transform;
            }

            Undo.SetCurrentGroupName("UnionAir: Reparent GameObject");
            var group = Undo.GetCurrentGroup();
            Undo.SetTransformParent(go.transform, newParent, "UnionAir: Reparent GameObject");
            if (newParent == null)
                SceneManager.MoveGameObjectToScene(go, scene);
            Undo.CollapseUndoOperations(group);

            if (originalScene != scene)
                EditorSceneManager.MarkSceneDirty(originalScene);
            EditorSceneManager.MarkSceneDirty(scene);

            var newGoPath = GameObjectUtils.GetPath(go);
            RestResponse.Send(response, $"{{\"path\":\"{RestResponse.EscapeJson(newGoPath)}\",\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\"}}");
        }
    }
}
