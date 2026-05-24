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
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.Url.AbsolutePath == "/api/gameobjects/components" &&
               (request.HttpMethod == "POST" ||
                request.HttpMethod == "DELETE" ||
                request.HttpMethod == "PATCH");

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
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
            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

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

            var go = GameObjectUtils.FindByPath(scene, path);
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

            EditorSceneManager.MarkSceneDirty(scene);
            RestResponse.Send(response,
                $"{{\"path\":\"{RestResponse.EscapeJson(path)}\",\"type\":\"{RestResponse.EscapeJson(typeName)}\"}}", 201);
        }

        // ── DELETE /api/gameobjects/components?path=&type= ───────────────────

        private static void HandleRemove(HttpListenerRequest request, HttpListenerResponse response)
        {
            var path     = request.QueryString["path"];
            var typeName = request.QueryString["type"];
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

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

            var go = GameObjectUtils.FindByPath(scene, path);
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
            EditorSceneManager.MarkSceneDirty(scene);

            RestResponse.Send(response,
                $"{{\"deleted\":\"{RestResponse.EscapeJson(typeName)}\",\"from\":\"{RestResponse.EscapeJson(path)}\"}}");
        }

        // ── PATCH /api/gameobjects/components?path=&type= ────────────────────

        private static void HandleUpdate(HttpListenerRequest request, HttpListenerResponse response)
        {
            var path     = request.QueryString["path"];
            var typeName = request.QueryString["type"];
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

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

            var go = GameObjectUtils.FindByPath(scene, path);
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

            // Iterate over serialized properties and attempt to set matching values.
            var iter = so.GetIterator();
            bool enterChildren = true;
            while (iter.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (iter.name == "m_Script") continue;

                var jsonKey = FindPropertyKey(propertiesJson, iter);
                if (jsonKey == null) continue;

                string error;
                int statusCode;
                if (ApplyPropertyFromJson(iter, propertiesJson, jsonKey, out error, out statusCode))
                {
                    updated.Add(jsonKey);
                    continue;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }
            }

            so.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(path)}\",");
            sb.Append($"\"component\":\"{RestResponse.EscapeJson(typeName)}\",");
            sb.Append("\"updated\":[");
            for (int i = 0; i < updated.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(updated[i])}\"");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string FindPropertyKey(string json, SerializedProperty prop)
        {
            if (PropertyExistsInJson(json, prop.propertyPath)) return prop.propertyPath;
            if (prop.propertyPath == prop.name && PropertyExistsInJson(json, prop.name)) return prop.name;
            return null;
        }

        private static bool PropertyExistsInJson(string json, string propName)
            => FindJsonValue(json, propName) != null;

        private static bool ApplyPropertyFromJson(
            SerializedProperty prop, string json, string jsonKey, out string error, out int statusCode)
        {
            error = null;
            statusCode = 400;

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                    {
                        var v = RequestBodyReader.GetBool(json, jsonKey);
                        if (v.HasValue) { prop.boolValue = v.Value; return true; }
                        break;
                    }
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Enum:
                    {
                        var v = RequestBodyReader.GetInt(json, jsonKey);
                        if (v.HasValue) { prop.intValue = v.Value; return true; }
                        break;
                    }
                    case SerializedPropertyType.Float:
                    {
                        var v = RequestBodyReader.GetFloat(json, jsonKey);
                        if (v.HasValue) { prop.floatValue = v.Value; return true; }
                        break;
                    }
                    case SerializedPropertyType.String:
                    {
                        var v = RequestBodyReader.GetString(json, jsonKey);
                        if (v != null) { prop.stringValue = v; return true; }
                        break;
                    }
                    case SerializedPropertyType.Color:
                    {
                        var obj = RequestBodyReader.GetObject(json, jsonKey);
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
                        var obj = RequestBodyReader.GetObject(json, jsonKey);
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
                        var obj = RequestBodyReader.GetObject(json, jsonKey);
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
                        var obj = RequestBodyReader.GetObject(json, jsonKey);
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
                    case SerializedPropertyType.ObjectReference:
                    {
                        UnityEngine.Object value;
                        if (TryResolveObjectReference(json, jsonKey, prop, out value, out error, out statusCode))
                        {
                            prop.objectReferenceValue = value;
                            return true;
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                error = $"Failed to update property {jsonKey}: {ex.Message}";
                statusCode = 400;
            }
            return false;
        }

        private static bool TryResolveObjectReference(
            string json, string jsonKey, SerializedProperty prop,
            out UnityEngine.Object value, out string error, out int statusCode)
        {
            value = null;
            error = null;
            statusCode = 400;

            var rawValue = FindJsonValue(json, jsonKey);
            if (rawValue == null) return false;

            rawValue = rawValue.Trim();
            if (rawValue == "null")
                return true;

            if (rawValue.Length == 0 || rawValue[0] != '{')
            {
                error = $"Object reference property {jsonKey} must be null or an object.";
                return false;
            }

            var expectedType = GetManagedObjectType(prop);
            var requestedTypeName = RequestBodyReader.GetString(rawValue, "type");
            var requestedType = ResolveOptionalReferenceType(requestedTypeName, jsonKey, out error, out statusCode);
            if (error != null) return false;

            var scenePath = RequestBodyReader.GetString(rawValue, "scenePath");
            var sceneAssetPath = RequestBodyReader.GetString(rawValue, "sceneAssetPath");
            var assetGuid = RequestBodyReader.GetString(rawValue, "assetGuid");
            var assetPath = RequestBodyReader.GetString(rawValue, "assetPath");

            if (!string.IsNullOrEmpty(scenePath))
                return TryResolveSceneReference(rawValue, jsonKey, sceneAssetPath, scenePath, expectedType, requestedType,
                    out value, out error, out statusCode);

            if (!string.IsNullOrEmpty(assetGuid) || !string.IsNullOrEmpty(assetPath))
                return TryResolveAssetReference(jsonKey, assetGuid, assetPath, expectedType, requestedType,
                    out value, out error, out statusCode);

            error = $"Object reference property {jsonKey} requires scenePath, assetGuid, assetPath, or null.";
            return false;
        }

        private static bool TryResolveSceneReference(
            string rawValue, string jsonKey, string sceneAssetPath, string scenePath, Type expectedType, Type requestedType,
            out UnityEngine.Object value, out string error, out int statusCode)
        {
            value = null;
            error = null;
            statusCode = 400;

            if (!TryResolveReferenceScene(sceneAssetPath, out var scene, out error, out statusCode))
                return false;

            var go = GameObjectUtils.FindByPath(scene, scenePath);
            if (go == null)
            {
                error = $"GameObject not found for property {jsonKey}: {scenePath}";
                statusCode = 404;
                return false;
            }

            var componentTypeName = RequestBodyReader.GetString(rawValue, "component");
            if (!string.IsNullOrEmpty(componentTypeName))
            {
                var componentType = ResolveType(componentTypeName);
                if (componentType == null)
                {
                    error = $"Unknown component type for property {jsonKey}: {componentTypeName}";
                    return false;
                }

                if (!typeof(Component).IsAssignableFrom(componentType))
                {
                    error = $"Type is not a Component for property {jsonKey}: {componentTypeName}";
                    return false;
                }

                var component = go.GetComponent(componentType);
                if (component == null)
                {
                    error = $"Component {componentTypeName} not found on {scenePath} for property {jsonKey}";
                    statusCode = 404;
                    return false;
                }

                value = component;
                return ValidateObjectReferenceType(jsonKey, value, expectedType, requestedType, out error, out statusCode);
            }

            value = go;
            return ValidateObjectReferenceType(jsonKey, value, expectedType, requestedType, out error, out statusCode);
        }

        private static bool TryResolveReferenceScene(
            string sceneAssetPath, out UnityEngine.SceneManagement.Scene scene, out string error, out int statusCode)
        {
            error = null;
            statusCode = 400;

            if (string.IsNullOrEmpty(sceneAssetPath))
            {
                scene = EditorSceneManager.GetActiveScene();
                return true;
            }

            var status = SceneResolver.ResolveLoaded(sceneAssetPath, out scene, out error);
            if (status == ResolveStatus.Found) return true;

            statusCode = status == ResolveStatus.Ambiguous ? 409 : 404;
            return false;
        }

        private static bool TryResolveAssetReference(
            string jsonKey, string assetGuid, string assetPath, Type expectedType, Type requestedType,
            out UnityEngine.Object value, out string error, out int statusCode)
        {
            value = null;
            error = null;
            statusCode = 400;

            if (!string.IsNullOrEmpty(assetGuid))
            {
                assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrEmpty(assetPath))
                {
                    error = $"Asset not found for property {jsonKey} with GUID: {assetGuid}";
                    statusCode = 404;
                    return false;
                }
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                error = $"Object reference property {jsonKey} requires assetGuid or assetPath.";
                return false;
            }

            var loadType = requestedType ?? expectedType ?? typeof(UnityEngine.Object);
            value = AssetDatabase.LoadAssetAtPath(assetPath, loadType);
            if (value == null)
            {
                error = $"Asset not found or incompatible for property {jsonKey}: {assetPath}";
                statusCode = 404;
                return false;
            }

            return ValidateObjectReferenceType(jsonKey, value, expectedType, requestedType, out error, out statusCode);
        }

        private static Type ResolveOptionalReferenceType(
            string typeName, string jsonKey, out string error, out int statusCode)
        {
            error = null;
            statusCode = 400;
            if (string.IsNullOrEmpty(typeName)) return null;

            var type = ResolveType(typeName);
            if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type)) return type;

            error = type == null
                ? $"Unknown object reference type for property {jsonKey}: {typeName}"
                : $"Type is not a UnityEngine.Object for property {jsonKey}: {typeName}";
            return null;
        }

        private static bool ValidateObjectReferenceType(
            string jsonKey, UnityEngine.Object value, Type expectedType, Type requestedType,
            out string error, out int statusCode)
        {
            error = null;
            statusCode = 400;

            if (value == null) return true;

            if (requestedType != null && !requestedType.IsInstanceOfType(value))
            {
                error = $"Resolved object for property {jsonKey} is not assignable to requested type {requestedType.FullName}.";
                statusCode = 422;
                return false;
            }

            if (expectedType != null && !expectedType.IsInstanceOfType(value))
            {
                error = $"Resolved object for property {jsonKey} is not assignable to field type {expectedType.FullName}.";
                statusCode = 422;
                return false;
            }

            return true;
        }

        private static Type GetManagedObjectType(SerializedProperty prop)
        {
            var typeName = prop.type;
            if (string.IsNullOrEmpty(typeName)) return null;

            const string prefix = "PPtr<$";
            if (typeName.StartsWith(prefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
                typeName = typeName.Substring(prefix.Length, typeName.Length - prefix.Length - 1);

            var type = ResolveType(typeName);
            return type != null && typeof(UnityEngine.Object).IsAssignableFrom(type) ? type : null;
        }

        private static string FindJsonValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;

            var searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + searchKey.Length);
            if (colonIdx < 0) return null;

            int start = colonIdx + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length) return null;

            int end = FindJsonValueEnd(json, start);
            if (end <= start) return null;

            return json.Substring(start, end - start);
        }

        private static int FindJsonValueEnd(string json, int start)
        {
            if (json[start] == '"')
            {
                int end = start + 1;
                while (end < json.Length)
                {
                    if (json[end] == '\\') { end += 2; continue; }
                    if (json[end] == '"') return end + 1;
                    end++;
                }
                return json.Length;
            }

            if (json[start] == '{' || json[start] == '[')
            {
                char open = json[start];
                char close = open == '{' ? '}' : ']';
                int depth = 0;
                bool inString = false;
                int end = start;
                while (end < json.Length)
                {
                    var c = json[end];
                    if (inString)
                    {
                        if (c == '\\') end++;
                        else if (c == '"') inString = false;
                    }
                    else
                    {
                        if (c == '"') inString = true;
                        else if (c == open) depth++;
                        else if (c == close)
                        {
                            depth--;
                            if (depth == 0) return end + 1;
                        }
                    }
                    end++;
                }
                return json.Length;
            }

            int scalarEnd = start;
            while (scalarEnd < json.Length &&
                   json[scalarEnd] != ',' &&
                   json[scalarEnd] != '}' &&
                   json[scalarEnd] != '\n' &&
                   json[scalarEnd] != '\r')
            {
                scalarEnd++;
            }

            return scalarEnd;
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
