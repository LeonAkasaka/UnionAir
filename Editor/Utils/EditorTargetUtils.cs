using System.Text;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class EditorTargetUtils
    {
        public static bool TryResolveTarget(
            string rawTarget,
            string defaultScenePath,
            string label,
            out UnityEngine.Object target,
            out string error,
            out int statusCode)
        {
            target = null;
            error = null;
            statusCode = 400;

            if (string.IsNullOrEmpty(rawTarget))
            {
                error = "Missing required field: " + label;
                return false;
            }

            var assetGuid = RequestBodyReader.GetString(rawTarget, "assetGuid");
            var assetPath = RequestBodyReader.GetString(rawTarget, "assetPath");
            var hasAssetReference = !string.IsNullOrEmpty(assetGuid) || !string.IsNullOrEmpty(assetPath);
            var typeName = RequestBodyReader.GetString(rawTarget, "type");
            var hasSceneReference = !string.IsNullOrEmpty(RequestBodyReader.GetString(rawTarget, "value")) ||
                                    IsSceneReferenceType(typeName);

            if (hasAssetReference && hasSceneReference)
            {
                error = label + " must not mix scene object reference fields with asset reference fields.";
                return false;
            }

            if (hasAssetReference)
            {
                return UnionAirReferenceResolver.TryResolveAssetReference(
                    rawTarget,
                    typeof(UnityEngine.Object),
                    label,
                    out target,
                    out error,
                    out statusCode);
            }

            if (!ObjectRefUtils.TryParse(rawTarget, label, out var objectRef, out error, out statusCode))
                return false;

            var scenePath = RequestBodyReader.GetString(rawTarget, "scenePath");
            if (string.IsNullOrEmpty(scenePath))
                scenePath = defaultScenePath;

            if (!UnionAirReferenceResolver.TryResolveOptionalScene(scenePath, out var scene, out error, out statusCode))
                return false;

            return ObjectRefUtils.TryResolveObject(scene, objectRef, label, out target, out error, out statusCode);
        }

        public static bool TryResolveAssetPath(
            string guid,
            string assetPath,
            string label,
            out string resolvedGuid,
            out string resolvedPath,
            out string error,
            out int statusCode)
        {
            resolvedGuid = "";
            resolvedPath = "";
            error = null;
            statusCode = 400;

            if (!string.IsNullOrEmpty(guid))
            {
                resolvedPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(resolvedPath))
                {
                    error = "Asset not found for " + label + " with GUID: " + guid;
                    statusCode = 404;
                    return false;
                }

                resolvedGuid = guid;
                return true;
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                error = label + " requires guid or assetPath.";
                return false;
            }

            if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) == null)
            {
                error = "Asset not found for " + label + ": " + assetPath;
                statusCode = 404;
                return false;
            }

            resolvedPath = assetPath;
            resolvedGuid = AssetDatabase.AssetPathToGUID(assetPath);
            return true;
        }

        public static string SelectionJson()
        {
            return SelectionJson(Selection.objects, Selection.activeObject);
        }

        public static string SelectionJson(UnityEngine.Object[] objects, UnityEngine.Object active)
        {
            if (objects == null)
                objects = new UnityEngine.Object[0];

            var activeIndex = FindIndex(objects, active);
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"count\":");
            sb.Append(objects.Length);
            sb.Append(",\"activeIndex\":");
            sb.Append(activeIndex);
            sb.Append(",\"active\":");
            if (activeIndex >= 0)
                AppendObjectJson(sb, active);
            else
                sb.Append("null");
            sb.Append(",\"objects\":[");
            for (var i = 0; i < objects.Length; i++)
            {
                if (i > 0) sb.Append(",");
                AppendObjectJson(sb, objects[i]);
            }
            sb.Append("],\"assetGuids\":[");
            var assetGuids = Selection.assetGUIDs;
            for (var i = 0; i < assetGuids.Length; i++)
            {
                if (i > 0) sb.Append(",");
                AppendStringValue(sb, assetGuids[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static void AppendObjectJson(StringBuilder sb, UnityEngine.Object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(value);
            var isAsset = !string.IsNullOrEmpty(assetPath) && EditorUtility.IsPersistent(value);
            var isSceneObject = value is GameObject || value is Component;

            sb.Append("{");
            AppendStringField(sb, "kind", isAsset ? "asset" : isSceneObject ? "sceneObject" : "unknown");
            sb.Append(",");
            AppendStringField(sb, "name", value.name);
            sb.Append(",");
            AppendStringField(sb, "type", value.GetType().FullName);

            if (isAsset)
            {
                sb.Append(",");
                AppendStringField(sb, "assetGuid", AssetDatabase.AssetPathToGUID(assetPath));
                sb.Append(",");
                AppendStringField(sb, "assetPath", assetPath);
            }
            else if (value is GameObject gameObject)
            {
                AppendSceneObjectFields(sb, gameObject, value);
            }
            else if (value is Component component)
            {
                AppendSceneObjectFields(sb, component.gameObject, value);
            }
            else
            {
                sb.Append(",");
                sb.Append("\"entityId\":");
                sb.Append(GetEditorObjectId(value));
            }

            sb.Append("}");
        }

        private static void AppendSceneObjectFields(StringBuilder sb, GameObject gameObject, UnityEngine.Object value)
        {
            sb.Append(",");
            AppendStringField(sb, "globalObjectId", ObjectIdUtils.GetGlobalObjectId(value));
            sb.Append(",");
            AppendStringField(sb, "scenePath", ScenePath(gameObject.scene));
        }

        private static string ScenePath(Scene scene)
        {
            return scene.IsValid() ? scene.path : "";
        }

        private static bool IsSceneReferenceType(string typeName)
        {
            return string.Equals(typeName, "hierarchyPath", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(typeName, "componentPath", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(typeName, "globalObjectId", System.StringComparison.OrdinalIgnoreCase);
        }

        private static long GetEditorObjectId(UnityEngine.Object value)
        {
            var method = typeof(UnityEngine.Object).GetMethod(
                "GetEntityId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                return 0;

            var result = method.Invoke(value, null);
            if (result is long longValue)
                return longValue;
            if (result is int intValue)
                return intValue;
            return 0;
        }

        private static int FindIndex(UnityEngine.Object[] objects, UnityEngine.Object value)
        {
            if (value == null) return -1;
            for (var i = 0; i < objects.Length; i++)
            {
                if (objects[i] == value)
                    return i;
            }
            return -1;
        }

        private static void AppendStringField(StringBuilder sb, string key, string value)
        {
            sb.Append("\"");
            sb.Append(key);
            sb.Append("\":");
            AppendStringValue(sb, value);
        }

        private static void AppendStringValue(StringBuilder sb, string value)
        {
            sb.Append("\"");
            sb.Append(RestResponse.EscapeJson(value));
            sb.Append("\"");
        }
    }
}
