using System;
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
    internal class ComponentWriteHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
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

        private static void HandleAdd(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var typeName = RequestBodyReader.GetString(body, "type");
            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return;

            if (string.IsNullOrEmpty(typeName))
            {
                RestResponse.SendError(response, "Missing required field: type", 400);
                return;
            }

            if (!ObjectRefUtils.TryReadBody(body, "target", out var target, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObject(scene, target, "target", out var go, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }
            scene = go.scene;

            var type = ObjectRefUtils.ResolveType(typeName, typeof(Component));
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

            Component added;
            if (EditorApplication.isPlaying)
            {
                added = go.AddComponent(type);
            }
            else
            {
                var group = UndoGroups.Begin("UnionAir: Add Component");
                added = Undo.AddComponent(go, type);
                Undo.CollapseUndoOperations(group);
            }

            if (added == null)
            {
                RestResponse.SendError(response, $"Failed to add component: {typeName}", 500);
                return;
            }

            SceneUtils.MarkDirtyUnlessPlaying(scene);
            var goPath = GameObjectUtils.GetPath(go);
            RestResponse.Send(response,
                $"{{\"path\":\"{RestResponse.EscapeJson(goPath)}\",\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",\"type\":\"{RestResponse.EscapeJson(typeName)}\",\"componentGlobalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(added))}\"}}", 201);
        }

        // ── DELETE /api/gameobjects/components?path=&type= ───────────────────

        private static void HandleRemove(UnionAirRequest request, UnionAirResponse response)
        {
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            if (!TryResolveComponentForWrite(request, scene, out var go, out var comp, out var targetError, out var targetStatusCode))
            {
                RestResponse.SendError(response, targetError, targetStatusCode);
                return;
            }
            scene = go.scene;
            var typeName = comp.GetType().FullName;
            var goPath = GameObjectUtils.GetPath(go);
            var componentId = ObjectIdUtils.GetGlobalObjectId(comp);

            if (EditorApplication.isPlaying)
                UnityEngine.Object.Destroy(comp);
            else
            {
                UndoGroups.Begin("UnionAir: Remove Component");
                Undo.DestroyObjectImmediate(comp);
            }
            SceneUtils.MarkDirtyUnlessPlaying(scene);

            RestResponse.Send(response,
                $"{{\"deleted\":\"{RestResponse.EscapeJson(typeName)}\",\"from\":\"{RestResponse.EscapeJson(goPath)}\",\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",\"componentGlobalObjectId\":\"{RestResponse.EscapeJson(componentId)}\"}}");
        }

        // ── PATCH /api/gameobjects/components?path=&type= ────────────────────

        private static void HandleUpdate(UnionAirRequest request, UnionAirResponse response)
        {
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene))
                return;

            if (!TryResolveComponentForWrite(request, scene, out var go, out var comp, out var targetError, out var targetStatusCode))
            {
                RestResponse.SendError(response, targetError, targetStatusCode);
                return;
            }
            scene = go.scene;
            var typeName = comp.GetType().FullName;
            var path = GameObjectUtils.GetPath(go);

            var body = RequestBodyReader.ReadString(request);
            var propertiesJson = RequestBodyReader.GetObject(body, "properties");
            if (string.IsNullOrEmpty(propertiesJson))
            {
                RestResponse.SendError(response, "Missing required field: properties", 400);
                return;
            }

            var useUndo = !EditorApplication.isPlaying;
            var group = -1;
            if (useUndo)
            {
                group = UndoGroups.Begin("UnionAir: Update Component");
            }

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
            if (useUndo)
                Undo.CollapseUndoOperations(group);
            SceneUtils.MarkDirtyUnlessPlaying(scene);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(path)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"component\":\"{RestResponse.EscapeJson(typeName)}\",");
            sb.Append($"\"componentGlobalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(comp))}\",");
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

        // Resolves the target component for DELETE/PATCH.
        // Accepts componentPath or a component's globalObjectId directly.
        // Also accepts hierarchyPath / GameObject globalObjectId when ?type=ComponentName is provided.
        private static bool TryResolveComponentForWrite(
            UnionAirRequest request,
            UnityEngine.SceneManagement.Scene scene,
            out GameObject go,
            out Component comp,
            out string error,
            out int statusCode)
        {
            go = null;
            comp = null;

            if (!ObjectRefUtils.TryReadQuery(request.QueryString, "target", out var target, out error, out statusCode))
                return false;

            if (!ObjectRefUtils.TryResolveGameObjectOrComponent(scene, target, "target", out go, out comp, out error, out statusCode))
                return false;

            if (comp != null)
                return true;

            // Target resolved to a GameObject — require ?type=ComponentName to identify the component
            var typeName = request.QueryString["type"];
            if (string.IsNullOrEmpty(typeName))
            {
                error = "target resolved to a GameObject. Specify the component using componentPath format or add ?type=ComponentName.";
                statusCode = 422;
                return false;
            }

            var type = ObjectRefUtils.ResolveType(typeName, typeof(Component));
            if (type == null)
            {
                error = $"Unknown component type: {typeName}";
                statusCode = 400;
                return false;
            }

            comp = go.GetComponent(type);
            if (comp == null)
            {
                error = $"No component of type '{typeName}' on '{GameObjectUtils.GetPath(go)}'.";
                statusCode = 404;
                return false;
            }

            return true;
        }

        // Top-level keys only. Every nested object a request sends carries key names that also name
        // serialized fields somewhere -- the "x" of a vector, the "assetPath" of an object reference --
        // so a search of the whole body reports a field as requested that the client never named.
        private static string FindPropertyKey(string json, SerializedProperty prop)
        {
            if (RequestBodyReader.HasTopLevelField(json, prop.propertyPath)) return prop.propertyPath;
            if (prop.propertyPath == prop.name && RequestBodyReader.HasTopLevelField(json, prop.name)) return prop.name;
            return null;
        }

        private static bool ApplyPropertyFromJson(
            SerializedProperty prop, string json, string jsonKey, out string error, out int statusCode)
        {
            // Delegate scalar-type handling to the shared serializer, but handle ObjectReference
            // ourselves so we can also resolve scene-object references (globalObjectId).
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return SerializedPropertySerializer.ApplyPropertyFromJson(prop, json, jsonKey, out error, out statusCode);

            error = null;
            statusCode = 400;
            try
            {
                UnityEngine.Object value;
                if (TryResolveObjectReference(json, jsonKey, prop, out value, out error, out statusCode))
                {
                    prop.objectReferenceValue = value;
                    return true;
                }
            }
            catch (System.Exception ex)
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

            var rawValue = RequestBodyReader.GetRawValue(json, jsonKey);
            if (rawValue == null)
            {
                // The key was selected by its presence at the top level, so no value here means the
                // value is present and unreadable -- an unescaped backslash in a Windows path is the
                // likely one -- rather than the field being absent. Returning false in silence would
                // answer 200 for a write that never happened.
                if (RequestBodyReader.HasTopLevelField(json, jsonKey))
                    error = $"Object reference property {jsonKey} is not a well-formed JSON value.";
                return false;
            }

            rawValue = rawValue.Trim();
            if (rawValue == "null")
                return true;

            if (rawValue.Length == 0 || rawValue[0] != '{')
            {
                error = $"Object reference property {jsonKey} must be null or an object.";
                return false;
            }

            var expectedType = ObjectReferenceResolverUtils.GetManagedObjectType(prop);
            var requestedTypeName = RequestBodyReader.GetString(rawValue, "assetType");
            var requestedType = ObjectReferenceResolverUtils.ResolveOptionalReferenceType(
                requestedTypeName,
                $"property {jsonKey}",
                "Unknown object reference type for {0}: {1}",
                out error,
                out statusCode);
            if (error != null) return false;

            var assetGuid = RequestBodyReader.GetString(rawValue, "assetGuid");
            var assetPath = RequestBodyReader.GetString(rawValue, "assetPath");

            if (!string.IsNullOrEmpty(assetGuid) || !string.IsNullOrEmpty(assetPath))
                return ObjectReferenceResolverUtils.TryResolveAssetReference(
                    assetGuid,
                    assetPath,
                    expectedType,
                    requestedType,
                    $"property {jsonKey}",
                    "Object reference {0} requires assetGuid or assetPath.",
                    "Asset not found for {0} with GUID: {1}",
                    "Asset not found or incompatible for {0}: {1}",
                    "Resolved object for {0} is not assignable to field type {1}.",
                    out value,
                    out error,
                    out statusCode);

            if (!TryResolveReferenceScene(RequestBodyReader.GetString(rawValue, "scenePath"), out var scene, out error, out statusCode))
                return false;

            if (!ObjectRefUtils.TryParse(rawValue, jsonKey, out var objectRef, out error, out statusCode))
                return false;

            if (!ObjectRefUtils.TryResolveObject(scene, objectRef, jsonKey, out value, out error, out statusCode))
                return false;
            return ObjectReferenceResolverUtils.ValidateObjectReferenceType(
                $"property {jsonKey}",
                value,
                expectedType,
                requestedType,
                "Resolved object for {0} is not assignable to field type {1}.",
                out error,
                out statusCode);
        }

        private static bool TryResolveReferenceScene(
            string scenePath, out UnityEngine.SceneManagement.Scene scene, out string error, out int statusCode)
        {
            error = null;
            statusCode = 400;

            if (string.IsNullOrEmpty(scenePath))
            {
                scene = EditorSceneManager.GetActiveScene();
                return true;
            }

            var status = SceneResolver.ResolveLoaded(scenePath, out scene, out error);
            if (status == ResolveStatus.Found) return true;

            statusCode = status == ResolveStatus.Ambiguous ? 409 : 404;
            return false;
        }

    }
}
