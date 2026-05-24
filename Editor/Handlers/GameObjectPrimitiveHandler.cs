using System;
using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/gameobjects/primitive
    /// Creates a Unity primitive (Cube, Sphere, Capsule, Cylinder, Plane, Quad)
    /// using GameObject.CreatePrimitive so the mesh and default material are assigned.
    /// Body: { "type": "Cube", "name": "MyCube", "parentPath": "..." }
    /// </summary>
    internal class GameObjectPrimitiveHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               request.Url.AbsolutePath == "/api/gameobjects/primitive";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body     = RequestBodyReader.ReadString(request);
            var typeName = RequestBodyReader.GetString(body, "type");
            var name     = RequestBodyReader.GetString(body, "name");
            var parentPath = RequestBodyReader.GetString(body, "parentPath");
            var parentGlobalObjectId = RequestBodyReader.GetString(body, "parentGlobalObjectId");
            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            if (string.IsNullOrEmpty(typeName))
            {
                RestResponse.SendError(response,
                    "Missing required field: type (Cube|Sphere|Capsule|Cylinder|Plane|Quad)", 400);
                return;
            }

            if (!Enum.TryParse<PrimitiveType>(typeName, true, out var primitiveType))
            {
                RestResponse.SendError(response,
                    $"Unknown primitive type: {typeName}. Valid values: Cube, Sphere, Capsule, Cylinder, Plane, Quad", 400);
                return;
            }

            Undo.SetCurrentGroupName($"UnionAir: Create {primitiveType} Primitive");
            var group = Undo.GetCurrentGroup();

            var go = GameObject.CreatePrimitive(primitiveType);
            Undo.RegisterCreatedObjectUndo(go, $"UnionAir: Create {primitiveType}");
            SceneManager.MoveGameObjectToScene(go, scene);

            if (!string.IsNullOrEmpty(name))
                go.name = name;

            if (!string.IsNullOrEmpty(parentGlobalObjectId) || !string.IsNullOrEmpty(parentPath))
            {
                if (!GameObjectUtils.TryResolveTarget(scene, parentGlobalObjectId, parentPath, "parent", out var parent, out var error, out var statusCode))
                {
                    Undo.DestroyObjectImmediate(go);
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }
                scene = parent.scene;
                go.transform.SetParent(parent.transform, false);
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);

            var goPath = GameObjectUtils.GetPath(go);
            var components = go.GetComponents<Component>();
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(go.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(goPath)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"primitiveType\":\"{RestResponse.EscapeJson(primitiveType.ToString())}\",");
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
