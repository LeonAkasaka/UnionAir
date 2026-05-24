using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/reparent
    /// Moves a GameObject under a new parent (or to the root when parent is omitted).
    /// Body: { "target": {"value": "..."}, "parent": {"value": "..."} }
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

            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            if (!ObjectRefUtils.TryReadBody(body, "target", out var target, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObject(scene, target, "target", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;
            var originalScene = go.scene;

            Transform newParent = null;

            var parentJson = RequestBodyReader.GetObject(body, "parent");
            if (!string.IsNullOrEmpty(parentJson))
            {
                if (!ObjectRefUtils.TryParse(parentJson, "parent", out var parentRef, out error, out statusCode) ||
                    !ObjectRefUtils.TryResolveGameObject(scene, parentRef, "parent", out var parentGo, out error, out statusCode))
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
