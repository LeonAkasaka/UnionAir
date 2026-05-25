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
            string nonObjectTypeMessage,
            out string error,
            out int statusCode)
        {
            error = null;
            statusCode = 400;
            if (string.IsNullOrEmpty(typeName)) return null;

            var type = ObjectRefUtils.ResolveType(typeName);
            if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type)) return type;

            error = type == null
                ? string.Format(unknownTypeMessage, label, typeName)
                : string.Format(nonObjectTypeMessage, label, typeName);
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

        public static Type GetManagedObjectType(SerializedProperty prop)
        {
            var typeName = prop.type;
            if (string.IsNullOrEmpty(typeName)) return null;

            const string prefix = "PPtr<$";
            if (typeName.StartsWith(prefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
                typeName = typeName.Substring(prefix.Length, typeName.Length - prefix.Length - 1);

            var type = ObjectRefUtils.ResolveType(typeName);
            return type != null && typeof(UnityEngine.Object).IsAssignableFrom(type) ? type : null;
        }
    }
}
