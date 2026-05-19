using System;
using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" &&
               request.Url.AbsolutePath == "/api/gameobjects/primitive";

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body     = RequestBodyReader.ReadString(request);
            var typeName = RequestBodyReader.GetString(body, "type");
            var name     = RequestBodyReader.GetString(body, "name");
            var parentPath = RequestBodyReader.GetString(body, "parentPath");

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

            if (!string.IsNullOrEmpty(name))
                go.name = name;

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = GameObjectUtils.FindByPath(parentPath);
                if (parent == null)
                {
                    Undo.DestroyObjectImmediate(go);
                    RestResponse.SendError(response, $"Parent not found: {parentPath}", 404);
                    return;
                }
                go.transform.SetParent(parent.transform, false);
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var goPath = GameObjectUtils.GetPath(go);
            var components = go.GetComponents<Component>();
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(go.name)}\",");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(goPath)}\",");
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
