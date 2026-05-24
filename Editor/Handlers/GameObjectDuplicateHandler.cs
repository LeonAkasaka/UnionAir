using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/duplicate?path=&lt;path&gt;
    /// Duplicates the target GameObject and places it next to the original.
    /// </summary>
    internal class GameObjectDuplicateHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               request.Url.AbsolutePath == "/api/gameobjects/duplicate";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            if (!ObjectRefUtils.TryReadQuery(request.QueryString, "source", out var source, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObject(scene, source, "source", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;

            Undo.SetCurrentGroupName("UnionAir: Duplicate GameObject");
            var group = Undo.GetCurrentGroup();

            var copy = Object.Instantiate(go, go.transform.parent);
            copy.name = GameObjectUtility.GetUniqueNameForSibling(go.transform.parent, go.name);
            Undo.RegisterCreatedObjectUndo(copy, "UnionAir: Duplicate GameObject");

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);

            var newPath = GameObjectUtils.GetPath(copy);
            RestResponse.Send(response,
                $"{{\"name\":\"{RestResponse.EscapeJson(copy.name)}\",\"path\":\"{RestResponse.EscapeJson(newPath)}\",\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(copy))}\"}}", 201);
        }
    }
}
