using System;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles write operations on components attached to a GameObject:
    ///   POST   /api/gameobjects/components              — add component
    ///   DELETE /api/gameobjects/components?path=&amp;type= — remove component
    ///   PATCH  /api/gameobjects/components?path=&amp;type= — update serialized properties
    /// </summary>
    internal class ComponentWriteHandler : IRequestHandler
    {
        public bool CanHandle(HttpListenerRequest request)
            => request.Url.AbsolutePath == "/api/gameobjects/components" &&
               (request.HttpMethod == "POST" ||
                request.HttpMethod == "DELETE" ||
                request.HttpMethod == "PATCH");

        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            switch (request.HttpMethod)
            {
                case "POST":   HandleAdd(request, response);    break;
                case "DELETE": HandleRemove(request, response); break;
                case "PATCH":  HandleUpdate(request, response); break;
                default:
                    RestResponse.SendError(response, "Method not allowed", 405);
                    break;
            }
        }

        // ── POST /api/gameobjects/components ─────────────────────────────────

        private static void HandleAdd(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var path = RequestBodyReader.GetString(body, "path");
            var typeName = RequestBodyReader.GetString(body, "type");

            if (string.IsNullOrEmpty(path))
            {
                RestResponse.SendError(response, "Missing required field: path", 400);
                return;
            }
            if (string.IsNullOrEmpty(typeName))
            {
                RestResponse.SendError(response, "Missing required field: type", 400);
                return;
            }

            var go = GameObjectUtils.FindByPath(path);
            if (go == null)
            {
                RestResponse.SendNotFound(response, $"GameObject not found at path: {path}");
                return;
            }

            var type = ResolveType(typeName);
            if (type == null)
            {
                RestResponse.SendError(response, $"Unknown component type: {typeName}", 400);
                return;
            }
            if (!type.IsSubclassOf(typeof(Component)))
            {
                RestResponse.SendError(response, $"Type is not a Component: {typeName}", 400);
                return;
            }

            Undo.SetCurrentGroupName("UnionAir: Add Component");
            var group = Undo.GetCurrentGroup();
            var added = Undo.AddComponent(go, type);
            Undo.CollapseUndoOperations(group);

            if (added == null)
            {
                RestResponse.SendError(response, $"Failed to add component: {typeName}", 500);
                return;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            RestResponse.Send(response,
                $"{{\"path\":\"{RestResponse.EscapeJson(path)}\",\"type\":\"{RestResponse.EscapeJson(typeName)}\"}}", 201);
        }

        // ── DELETE /api/gameobjects/components?path=&type= ───────────────────

        private static void HandleRemove(HttpListenerRequest request, HttpListenerResponse response)
        {
            var path     = request.QueryString["path"];
            var typeName = request.QueryString["type"];

            if (string.IsNullOrEmpty(path))
            {
                RestResponse.SendError(response, "Missing required query parameter: path", 400);
                return;
            }
            if (string.IsNullOrEmpty(typeName))
            {
                RestResponse.SendError(response, "Missing required query parameter: type", 400);
                return;
            }

            var go = GameObjectUtils.FindByPath(path);
            if (go == null)
            {
                RestResponse.SendNotFound(response, $"GameObject not found at path: {path}");
                return;
            }

            var type = ResolveType(typeName);
            if (type == null)
            {
                RestResponse.SendError(response, $"Unknown component type: {typeName}", 400);
                return;
            }

            var comp = go.GetComponent(type);
            if (comp == null)
            {
                RestResponse.SendNotFound(response,
                    $"Component {typeName} not found on {path}");
                return;
            }

            Undo.SetCurrentGroupName("UnionAir: Remove Component");
            Undo.DestroyObjectImmediate(comp);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            RestResponse.Send(response,
                $"{{\"deleted\":\"{RestResponse.EscapeJson(typeName)}\",\"from\":\"{RestResponse.EscapeJson(path)}\"}}");
        }

        // ── PATCH /api/gameobjects/components?path=&type= ────────────────────

        private static void HandleUpdate(HttpListenerRequest request, HttpListenerResponse response)
        {
            var path     = request.QueryString["path"];
            var typeName = request.QueryString["type"];

            if (string.IsNullOrEmpty(path))
            {
                RestResponse.SendError(response, "Missing required query parameter: path", 400);
                return;
            }
            if (string.IsNullOrEmpty(typeName))
            {
                RestResponse.SendError(response, "Missing required query parameter: type", 400);
                return;
            }

            var go = GameObjectUtils.FindByPath(path);
            if (go == null)
            {
                RestResponse.SendNotFound(response, $"GameObject not found at path: {path}");
                return;
            }

            var type = ResolveType(typeName);
            if (type == null)
            {
                RestResponse.SendError(response, $"Unknown component type: {typeName}", 400);
                return;
            }

            var comp = go.GetComponent(type);
            if (comp == null)
            {
                RestResponse.SendNotFound(response,
                    $"Component {typeName} not found on {path}");
                return;
            }

            var body = RequestBodyReader.ReadString(request);
            var propertiesJson = RequestBodyReader.GetObject(body, "properties");
            if (string.IsNullOrEmpty(propertiesJson))
            {
                RestResponse.SendError(response, "Missing required field: properties", 400);
                return;
            }

            Undo.SetCurrentGroupName("UnionAir: Update Component");
            var group = Undo.GetCurrentGroup();

            var so = new SerializedObject(comp);
            var updated = new System.Collections.Generic.List<string>();

            // Iterate over all top-level properties and attempt to set matching values
            var iter = so.GetIterator();
            bool enterChildren = true;
            while (iter.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iter.name == "m_Script") continue;
                if (!PropertyExistsInJson(propertiesJson, iter.name)) continue;

                if (ApplyPropertyFromJson(iter, propertiesJson))
                    updated.Add(iter.name);
            }

            so.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var sb = new StringBuilder();
            sb.Append("{\"updated\":[");
            for (int i = 0; i < updated.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(updated[i])}\"");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static bool PropertyExistsInJson(string json, string propName)
            => json.IndexOf($"\"{propName}\"", StringComparison.Ordinal) >= 0;

        private static bool ApplyPropertyFromJson(SerializedProperty prop, string json)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                    {
                        var v = RequestBodyReader.GetBool(json, prop.name);
                        if (v.HasValue) { prop.boolValue = v.Value; return true; }
                        break;
                    }
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Enum:
                    {
                        var v = RequestBodyReader.GetInt(json, prop.name);
                        if (v.HasValue) { prop.intValue = v.Value; return true; }
                        break;
                    }
                    case SerializedPropertyType.Float:
                    {
                        var v = RequestBodyReader.GetFloat(json, prop.name);
                        if (v.HasValue) { prop.floatValue = v.Value; return true; }
                        break;
                    }
                    case SerializedPropertyType.String:
                    {
                        var v = RequestBodyReader.GetString(json, prop.name);
                        if (v != null) { prop.stringValue = v; return true; }
                        break;
                    }
                    case SerializedPropertyType.Color:
                    {
                        var obj = RequestBodyReader.GetObject(json, prop.name);
                        if (obj != null)
                        {
                            var r = RequestBodyReader.GetFloat(obj, "r") ?? prop.colorValue.r;
                            var g = RequestBodyReader.GetFloat(obj, "g") ?? prop.colorValue.g;
                            var b = RequestBodyReader.GetFloat(obj, "b") ?? prop.colorValue.b;
                            var a = RequestBodyReader.GetFloat(obj, "a") ?? prop.colorValue.a;
                            prop.colorValue = new Color(r, g, b, a);
                            return true;
                        }
                        break;
                    }
                    case SerializedPropertyType.Vector2:
                    {
                        var obj = RequestBodyReader.GetObject(json, prop.name);
                        if (obj != null)
                        {
                            var x = RequestBodyReader.GetFloat(obj, "x") ?? prop.vector2Value.x;
                            var y = RequestBodyReader.GetFloat(obj, "y") ?? prop.vector2Value.y;
                            prop.vector2Value = new Vector2(x, y);
                            return true;
                        }
                        break;
                    }
                    case SerializedPropertyType.Vector3:
                    {
                        var obj = RequestBodyReader.GetObject(json, prop.name);
                        if (obj != null)
                        {
                            var x = RequestBodyReader.GetFloat(obj, "x") ?? prop.vector3Value.x;
                            var y = RequestBodyReader.GetFloat(obj, "y") ?? prop.vector3Value.y;
                            var z = RequestBodyReader.GetFloat(obj, "z") ?? prop.vector3Value.z;
                            prop.vector3Value = new Vector3(x, y, z);
                            return true;
                        }
                        break;
                    }
                    case SerializedPropertyType.Vector4:
                    {
                        var obj = RequestBodyReader.GetObject(json, prop.name);
                        if (obj != null)
                        {
                            var x = RequestBodyReader.GetFloat(obj, "x") ?? prop.vector4Value.x;
                            var y = RequestBodyReader.GetFloat(obj, "y") ?? prop.vector4Value.y;
                            var z = RequestBodyReader.GetFloat(obj, "z") ?? prop.vector4Value.z;
                            var w = RequestBodyReader.GetFloat(obj, "w") ?? prop.vector4Value.w;
                            prop.vector4Value = new Vector4(x, y, z, w);
                            return true;
                        }
                        break;
                    }
                }
            }
            catch { /* ignore serialization errors for exotic properties */ }
            return false;
        }

        private static Type ResolveType(string typeName)
        {
            // Try direct lookup first
            var t = Type.GetType(typeName);
            if (t != null) return t;

            // Search all loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(typeName);
                if (t != null) return t;

                // Also try short name matching
                foreach (var candidate in asm.GetTypes())
                {
                    if (candidate.Name == typeName || candidate.FullName == typeName)
                        return candidate;
                }
            }
            return null;
        }
    }
}
