using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles write operations for ScriptableObject assets:
    ///   POST   /api/assets/scriptableobjects           — create a new ScriptableObject asset
    ///   PATCH  /api/assets/scriptableobjects?guid=     — update serialized properties
    ///   DELETE /api/assets/scriptableobjects/{guid}    — delete the asset
    /// </summary>
    internal class ScriptableObjectWriteHandler
    {
        public void Handle(UnionAirRequest request, UnionAirResponse response)
        {
            switch (request.HttpMethod)
            {
                case "POST":   HandleCreate(request, response); break;
                case "PATCH":  HandleUpdate(request, response); break;
                case "DELETE": HandleDelete(request, response); break;
                default:
                    RestResponse.SendError(response, "Method not allowed", 405);
                    break;
            }
        }

        // ── POST /api/assets/scriptableobjects ───────────────────────────────

        private static void HandleCreate(UnionAirRequest request, UnionAirResponse response)
        {
            var body      = RequestBodyReader.ReadString(request);
            var typeName  = RequestBodyReader.GetString(body, "typeName");
            var assetPath = RequestBodyReader.GetString(body, "assetPath");

            if (string.IsNullOrEmpty(typeName))
            {
                RestResponse.SendError(response, "Missing required field: typeName", 400);
                return;
            }
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendError(response, "Missing required field: assetPath", 400);
                return;
            }
            if (!assetPath.EndsWith(".asset"))
            {
                RestResponse.SendError(response, "assetPath must end with .asset", 400);
                return;
            }
            if (!assetPath.StartsWith("Assets/"))
            {
                RestResponse.SendError(response, "assetPath must start with 'Assets/'", 400);
                return;
            }
            // Use LoadAssetAtPath rather than AssetPathToGUID to detect actual file existence.
            // AssetPathToGUID can return stale GUIDs for files deleted outside Unity without a Refresh,
            // whereas LoadAssetAtPath reads from disk and returns null when no file is present.
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
            {
                RestResponse.SendError(response, $"Asset already exists at path: {assetPath}", 409);
                return;
            }

            var type = ObjectRefUtils.ResolveType(typeName, typeof(ScriptableObject));
            if (type == null)
            {
                RestResponse.SendError(response, $"Unknown type: {typeName}", 400);
                return;
            }
            if (!type.IsSubclassOf(typeof(ScriptableObject)))
            {
                RestResponse.SendError(response, $"Type is not a ScriptableObject: {typeName}", 400);
                return;
            }
            if (type.IsAbstract)
            {
                RestResponse.SendError(response, $"Type is abstract and cannot be instantiated: {typeName}", 400);
                return;
            }

            // Create instance in memory first, apply properties before saving to disk.
            // This ensures a failed property validation leaves no orphaned asset on disk.
            var instance = ScriptableObject.CreateInstance(type);
            try
            {
                var updated = new List<string>();
                var propertiesJson = RequestBodyReader.GetObject(body, "properties");
                if (!string.IsNullOrEmpty(propertiesJson))
                {
                    ApplyProperties(instance, propertiesJson, updated, response, out var earlyExit);
                    if (earlyExit) return;
                }

                // All validation passed — now persist to disk.
                var dir = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
                AssetUtils.EnsureDirectory(dir);
                AssetDatabase.CreateAsset(instance, assetPath);
                if (updated.Count > 0) EditorUtility.SetDirty(instance);
                AssetDatabase.SaveAssets();

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");
                sb.Append($"\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",");
                sb.Append($"\"type\":\"{RestResponse.EscapeJson(typeName)}\",");
                sb.Append("\"updated\":[");
                for (int i = 0; i < updated.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"\"{RestResponse.EscapeJson(updated[i])}\"");
                }
                sb.Append("]}");
                RestResponse.Send(response, sb.ToString(), 201);
            }
            finally
            {
                // Once CreateAsset succeeds, the AssetDatabase owns the instance. Before that,
                // including every validation rejection, this method owns its native lifetime.
                if (instance != null && !EditorUtility.IsPersistent(instance))
                    Object.DestroyImmediate(instance);
            }
        }

        // ── PATCH /api/assets/scriptableobjects?guid= ────────────────────────

        private static void HandleUpdate(UnionAirRequest request, UnionAirResponse response)
        {
            var guid = request.QueryString["guid"];
            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Missing required query parameter: guid", 400);
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (obj == null)
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }
            var instance = obj as ScriptableObject;
            if (instance == null)
            {
                RestResponse.SendError(response, $"Asset is not a ScriptableObject: {assetPath}", 400);
                return;
            }

            var body = RequestBodyReader.ReadString(request);
            var propertiesJson = RequestBodyReader.GetObject(body, "properties");
            if (string.IsNullOrEmpty(propertiesJson))
            {
                RestResponse.SendError(response, "Missing required field: properties", 400);
                return;
            }

            var updated = new List<string>();
            ApplyProperties(instance, propertiesJson, updated, response, out var earlyExit);
            if (earlyExit) return;

            EditorUtility.SetDirty(instance);
            AssetDatabase.SaveAssets();

            var typeName = instance.GetType().FullName;
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");
            sb.Append($"\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",");
            sb.Append($"\"type\":\"{RestResponse.EscapeJson(typeName)}\",");
            sb.Append("\"updated\":[");
            for (int i = 0; i < updated.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(updated[i])}\"");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── DELETE /api/assets/scriptableobjects/{guid} ──────────────────────

        private static void HandleDelete(UnionAirRequest request, UnionAirResponse response)
        {
            var absPath = request.Url.AbsolutePath;
            var guid = absPath.Substring("/api/assets/scriptableobjects/".Length);

            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Missing GUID", 400);
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            // Confirm the asset exists and is actually a ScriptableObject.
            // GetMainAssetTypeAtPath returns null when the file no longer exists (stale GUID cache).
            var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (type == null)
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }
            if (!type.IsSubclassOf(typeof(ScriptableObject)))
            {
                RestResponse.SendError(response, $"Asset is not a ScriptableObject: {assetPath}", 400);
                return;
            }

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"deleted\":\"{RestResponse.EscapeJson(assetPath)}\",\"guid\":\"{RestResponse.EscapeJson(guid)}\"}}");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Iterates all visible serialized properties on <paramref name="instance"/> and
        /// applies matching values from <paramref name="propertiesJson"/>.
        /// Every requested property must be writable and carry a compatible JSON value.
        /// Sets <paramref name="earlyExit"/> to true if a response error was already sent.
        /// </summary>
        private static void ApplyProperties(
            ScriptableObject instance,
            string propertiesJson,
            List<string> updated,
            UnionAirResponse response,
            out bool earlyExit)
        {
            earlyExit = true;
            var so = new SerializedObject(instance);

            // Every key the request sent has to be accounted for. A key naming no property is
            // found here rather than in the loop below, which can only report keys it reached.
            if (!RequestBodyReader.TryGetTopLevelFieldNames(
                    propertiesJson, out var requestedKeys, out var keyError))
            {
                RestResponse.SendError(response, $"Invalid 'properties': {keyError}", 400);
                return;
            }

            var typeName = instance.GetType().FullName;
            var unwritable = SerializedPropertySerializer.FindUnwritableKey(
                so, propertiesJson, false, requestedKeys, out var reason);
            if (unwritable != null)
            {
                RestResponse.SendError(
                    response,
                    reason ??
                    $"No serialized property named '{unwritable}' on {typeName}. " +
                    "Send the names GET /api/assets/scriptableobjects/{guid} reports for this asset.",
                    400);
                return;
            }

            var iter = so.GetIterator();
            bool enterChildren = true;

            while (iter.NextVisible(enterChildren))
            {
                enterChildren = false; // visit top-level properties only; do not descend into children

                // Arrays and their elements are written by the pass below, which addresses an
                // element through its array rather than through this walk -- which never reaches
                // one, since it does not descend.
                if (SerializedPropertySerializer.IsWritableAsArray(iter)) continue;

                // Try both the simple name and full propertyPath as JSON keys, top-level only:
                // a key nested inside another property's value is not a key this request sent.
                string jsonKey = null;
                if (RequestBodyReader.HasTopLevelField(propertiesJson, iter.name))
                    jsonKey = iter.name;
                else if (iter.propertyPath != iter.name &&
                         RequestBodyReader.HasTopLevelField(propertiesJson, iter.propertyPath))
                    jsonKey = iter.propertyPath;

                if (jsonKey == null) continue;

                if (iter.name == "m_Script")
                {
                    RestResponse.SendError(
                        response,
                        $"Property {jsonKey} cannot be written. The script a ScriptableObject " +
                        "asset instantiates is fixed when the asset is created.",
                        400);
                    return;
                }

                if (SerializedPropertySerializer.ApplyPropertyFromJson(
                        iter, propertiesJson, jsonKey, out var error, out var statusCode))
                {
                    updated.Add(jsonKey);
                    continue;
                }

                RestResponse.SendError(response, error, statusCode);
                return;
            }

            if (!SerializedPropertySerializer.TryApplyArrayKeys(
                    so, propertiesJson, requestedKeys,
                    SerializedPropertySerializer.ApplyPropertyFromJson, updated,
                    out var arrayError, out var arrayStatusCode))
            {
                RestResponse.SendError(response, arrayError, arrayStatusCode);
                return;
            }

            so.ApplyModifiedProperties();
            earlyExit = false;
        }
    }
}
