using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Shared utilities for reading and writing SerializedProperty values as JSON.
    /// Extracted from ComponentWriteHandler so that ScriptableObject handlers can reuse the same logic.
    /// </summary>
    internal static class SerializedPropertySerializer
    {
        // ── Read direction ────────────────────────────────────────────────────

        /// <summary>
        /// Appends the JSON representation of <paramref name="prop"/> to <paramref name="sb"/>.
        /// ObjectReferences to scene objects (non-asset) are emitted as <c>null</c>.
        /// </summary>
        public static void SerializePropertyToJson(SerializedProperty prop, StringBuilder sb)
        {
            // Serialize arrays (Unity internally represents strings as char arrays, so exclude those)
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                sb.Append('[');
                for (int i = 0; i < prop.arraySize; i++)
                {
                    if (i > 0) sb.Append(',');
                    SerializePropertyToJson(prop.GetArrayElementAtIndex(i), sb);
                }
                sb.Append(']');
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    sb.Append(prop.boolValue ? "true" : "false");
                    break;

                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                    sb.Append(prop.intValue);
                    break;

                case SerializedPropertyType.Float:
                    sb.Append(RestResponse.FormatFloat(prop.floatValue));
                    break;

                case SerializedPropertyType.String:
                    sb.Append('"');
                    sb.Append(RestResponse.EscapeJson(prop.stringValue));
                    sb.Append('"');
                    break;

                case SerializedPropertyType.Color:
                {
                    var c = prop.colorValue;
                    sb.Append($"{{\"r\":{RestResponse.FormatFloat(c.r)},\"g\":{RestResponse.FormatFloat(c.g)},\"b\":{RestResponse.FormatFloat(c.b)},\"a\":{RestResponse.FormatFloat(c.a)}}}");
                    break;
                }
                case SerializedPropertyType.Vector2:
                {
                    var v = prop.vector2Value;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)}}}");
                    break;
                }
                case SerializedPropertyType.Vector3:
                {
                    var v = prop.vector3Value;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)},\"z\":{RestResponse.FormatFloat(v.z)}}}");
                    break;
                }
                case SerializedPropertyType.Vector4:
                {
                    var v = prop.vector4Value;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)},\"z\":{RestResponse.FormatFloat(v.z)},\"w\":{RestResponse.FormatFloat(v.w)}}}");
                    break;
                }
                case SerializedPropertyType.Quaternion:
                {
                    var q = prop.quaternionValue;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(q.x)},\"y\":{RestResponse.FormatFloat(q.y)},\"z\":{RestResponse.FormatFloat(q.z)},\"w\":{RestResponse.FormatFloat(q.w)}}}");
                    break;
                }
                case SerializedPropertyType.Rect:
                {
                    var r = prop.rectValue;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(r.x)},\"y\":{RestResponse.FormatFloat(r.y)},\"width\":{RestResponse.FormatFloat(r.width)},\"height\":{RestResponse.FormatFloat(r.height)}}}");
                    break;
                }
                case SerializedPropertyType.Bounds:
                {
                    var b = prop.boundsValue;
                    sb.Append($"{{\"center\":{{\"x\":{RestResponse.FormatFloat(b.center.x)},\"y\":{RestResponse.FormatFloat(b.center.y)},\"z\":{RestResponse.FormatFloat(b.center.z)}}},");
                    sb.Append($"\"extents\":{{\"x\":{RestResponse.FormatFloat(b.extents.x)},\"y\":{RestResponse.FormatFloat(b.extents.y)},\"z\":{RestResponse.FormatFloat(b.extents.z)}}}}}");
                    break;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    var obj = prop.objectReferenceValue;
                    if (obj == null)
                    {
                        sb.Append("null");
                        break;
                    }
                    var assetPath = AssetDatabase.GetAssetPath(obj);
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        // Scene object — not serialisable as an asset reference
                        sb.Append("null");
                        break;
                    }
                    var assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    var typeName  = obj.GetType().FullName;
                    sb.Append($"{{\"assetGuid\":\"{RestResponse.EscapeJson(assetGuid)}\",");
                    sb.Append($"\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",");
                    sb.Append($"\"assetType\":\"{RestResponse.EscapeJson(typeName)}\"}}");
                    break;
                }
                default:
                    // Generic (arrays, nested structs) and unsupported types → null
                    sb.Append("null");
                    break;
            }
        }

        // ── Write direction ───────────────────────────────────────────────────

        /// <summary>
        /// Tries to apply the value for <paramref name="jsonKey"/> from <paramref name="json"/>
        /// to <paramref name="prop"/>.  Returns <c>true</c> if the property was updated.
        /// <paramref name="error"/> is non-null only when the value was found but could not be applied.
        /// Array properties are silently skipped (returns <c>false</c> with no error).
        /// </summary>
        public static bool ApplyPropertyFromJson(
            SerializedProperty prop, string json, string jsonKey, out string error, out int statusCode)
        {
            error = null;
            statusCode = 400;

            // Skip array/list properties silently.
            // Note: Unity represents string as a char array internally, so prop.isArray is true
            // for string fields — we must not skip those, as they are handled by the String case below.
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String) return false;

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

        // ── ObjectReference resolution ────────────────────────────────────────

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
                // Selected by top-level presence, so an unreadable value is not an absent field.
                // See the matching note in ComponentWriteHandler.TryResolveObjectReference.
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

            error = $"Object reference property {jsonKey} requires assetGuid or assetPath.";
            return false;
        }
    }
}
