using System;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class ObjectReferenceResolverUtils
    {
        public static bool TryResolveAssetReference(
            string assetGuid,
            string assetPath,
            Type expectedType,
            Type requestedType,
            string label,
            string missingMessage,
            string assetNotFoundWithGuidMessage,
            string assetNotFoundOrIncompatibleMessage,
            string expectedTypeMismatchMessage,
            out UnityEngine.Object value,
            out string error,
            out int statusCode)
        {
            value = null;
            error = null;
            statusCode = 400;

            if (requestedType != null && !typeof(UnityEngine.Object).IsAssignableFrom(requestedType))
            {
                error = $"Type is not a UnityEngine.Object for {label}: {requestedType.FullName}";
                return false;
            }

            if (expectedType != null && !typeof(UnityEngine.Object).IsAssignableFrom(expectedType))
            {
                error = $"Expected type is not a UnityEngine.Object for {label}: {expectedType.FullName}";
                return false;
            }

            if (!string.IsNullOrEmpty(assetGuid))
            {
                assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrEmpty(assetPath))
                {
                    error = string.Format(assetNotFoundWithGuidMessage, label, assetGuid);
                    statusCode = 404;
                    return false;
                }
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                error = string.Format(missingMessage, label);
                return false;
            }

            var loadType = requestedType ?? expectedType ?? typeof(UnityEngine.Object);
            value = AssetDatabase.LoadAssetAtPath(assetPath, loadType);
            if (value == null)
            {
                error = string.Format(assetNotFoundOrIncompatibleMessage, label, assetPath);
                statusCode = 404;
                return false;
            }

            return ValidateObjectReferenceType(
                label,
                value,
                expectedType,
                requestedType,
                expectedTypeMismatchMessage,
                out error,
                out statusCode);
        }

        public static Type ResolveOptionalReferenceType(
            string typeName,
            string label,
            string unknownTypeMessage,
            out string error,
            out int statusCode)
        {
            error = null;
            statusCode = 400;
            if (string.IsNullOrEmpty(typeName)) return null;

            var type = ObjectRefUtils.ResolveType(typeName, typeof(UnityEngine.Object));
            if (type != null) return type;

            error = string.Format(unknownTypeMessage, label, typeName);
            return null;
        }

        public static bool ValidateObjectReferenceType(
            string label,
            UnityEngine.Object value,
            Type expectedType,
            Type requestedType,
            string expectedTypeMismatchMessage,
            out string error,
            out int statusCode)
        {
            error = null;
            statusCode = 400;

            if (value == null) return true;

            if (requestedType != null && !requestedType.IsInstanceOfType(value))
            {
                error = $"Resolved object for {label} is not assignable to requested type {requestedType.FullName}.";
                statusCode = 422;
                return false;
            }

            if (expectedType != null && !expectedType.IsInstanceOfType(value))
            {
                error = string.Format(expectedTypeMismatchMessage, label, expectedType.FullName);
                statusCode = 422;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads one field of an object reference, refusing a value that is not a JSON string.
        /// </summary>
        /// <remarks>
        /// Every field of a reference names something -- a GUID, a path, a type, a hierarchy
        /// address -- and none of them is a number. <see cref="RequestBodyReader.GetString"/>
        /// hands back the raw token when the value is not quoted, so <c>{"assetGuid": 5}</c>
        /// arrived here as the GUID <c>"5"</c> and was answered <c>404 Asset not found</c>: a
        /// status describing a missing asset, for a request whose fault is a value of the wrong
        /// type. Read through here instead, and the answer names the field.
        ///
        /// An explicit <c>null</c> reads as absent rather than as an error, because that is what
        /// it already meant: a reference carrying <c>"assetGuid": null</c> is one that did not
        /// give a GUID, and the caller's own "requires assetGuid or assetPath" is the answer it
        /// should get.
        /// </remarks>
        public static bool TryReadReferenceField(
            string referenceJson,
            string field,
            string label,
            out string value,
            out string error,
            out int statusCode)
        {
            value = null;
            error = null;
            statusCode = 400;

            // GetRawValue answers null for a field that is absent and for one whose value is not
            // well-formed JSON, and the second never arrives here: measured on 6000.0.80f1, a
            // reference carrying an unescaped Windows path is refused by the surrounding
            // 'properties' parse first, naming the key. Both read as absent, and the caller's
            // "requires assetGuid or assetPath" is the answer either one should get.
            var raw = RequestBodyReader.GetRawValue(referenceJson, field);
            if (raw == null) return true;

            raw = raw.Trim();
            if (raw == "null") return true;

            if (raw.Length == 0 || raw[0] != '"')
            {
                error = $"Field '{field}' of {label} must be a JSON string.";
                return false;
            }

            // Defensive: the token came from GetRawValue, which parses before it returns, so a
            // token opening with a quote is already known to be a complete string.
            if (!RequestBodyReader.TryParseJsonString(raw, out value))
            {
                error = $"Field '{field}' of {label} is not a well-formed JSON string.";
                return false;
            }

            return true;
        }

        public static Type GetManagedObjectType(SerializedProperty prop)
        {
            var typeName = prop.type;
            if (string.IsNullOrEmpty(typeName)) return null;

            const string prefix = "PPtr<$";
            if (typeName.StartsWith(prefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
                typeName = typeName.Substring(prefix.Length, typeName.Length - prefix.Length - 1);

            return ObjectRefUtils.ResolveType(typeName, typeof(UnityEngine.Object));
        }
    }
}
