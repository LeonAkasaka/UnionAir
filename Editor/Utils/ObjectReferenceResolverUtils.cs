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
            => TryResolveAssetReference(
                assetGuid, assetPath, null, expectedType, requestedType, label,
                missingMessage, assetNotFoundWithGuidMessage, assetNotFoundOrIncompatibleMessage,
                expectedTypeMismatchMessage, out value, out error, out statusCode);

        public static bool TryResolveAssetReference(
            string assetGuid,
            string assetPath,
            long? localIdentifier,
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

            if (localIdentifier.HasValue)
            {
                value = FindByLocalIdentifier(assetPath, localIdentifier.Value);
                if (value == null)
                {
                    error = $"No object with localIdentifier {localIdentifier.Value} for {label} at: {assetPath}";
                    statusCode = 404;
                    return false;
                }
            }
            else if (!TryResolveTheOnlyCandidate(assetPath, loadType, out value, out var candidates))
            {
                // A path holding more than one object of the required type cannot be addressed by
                // path alone, and answering 200 having bound whichever one Unity returned is the
                // silence this vocabulary was extended to remove: the client cannot tell which
                // object it got, and the read afterwards is identical for every one of them.
                error = $"{candidates} objects assignable to {loadType.Name} exist at {assetPath} for {label}. " +
                        "Send localIdentifier to name one; GET /api/assets/{guid} lists them.";
                return false;
            }

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

        private static UnityEngine.Object FindByLocalIdentifier(string assetPath, long localIdentifier)
        {
            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (candidate == null) continue;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out _, out long id)) continue;
                if (id == localIdentifier) return candidate;
            }
            return null;
        }

        /// <summary>
        /// Resolves a path that holds exactly one object of the required type, and reports how many
        /// it holds when that is not the case.
        /// </summary>
        /// <remarks>
        /// Sub-asset representations are asked for before the whole file is loaded, because most
        /// references name a single-object asset -- a material, a texture, a prefab -- and that
        /// answer is empty for all of them.
        /// </remarks>
        private static bool TryResolveTheOnlyCandidate(
            string assetPath, Type loadType, out UnityEngine.Object value, out int candidates)
        {
            value = AssetDatabase.LoadAssetAtPath(assetPath, loadType);
            candidates = value == null ? 0 : 1;

            var representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
            if (representations == null || representations.Length == 0) return true;

            var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
            candidates = main != null && loadType.IsInstanceOfType(main) ? 1 : 0;

            foreach (var representation in representations)
            {
                if (representation == null || !loadType.IsInstanceOfType(representation)) continue;
                candidates++;
                if (candidates == 1) value = representation;
            }

            if (candidates <= 1) return true;

            value = null;
            return false;
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

        /// <summary>
        /// Reads the three asset fields of an object reference and resolves the requested type.
        /// </summary>
        /// <remarks>
        /// Every write that takes a reference needs exactly this sequence -- read
        /// <c>assetType</c>, resolve it, then read <c>assetGuid</c> and <c>assetPath</c> -- and
        /// four of them carried their own copy of it, including four copies of the same "unknown
        /// type" message. One copy means one answer: a client cannot tell which endpoint it is
        /// talking to from how a malformed reference is refused.
        /// </remarks>
        public static bool TryReadAssetReferenceFields(
            string referenceJson,
            string label,
            out string assetGuid,
            out string assetPath,
            out Type requestedType,
            out string error,
            out int statusCode)
            => TryReadAssetReferenceFields(
                referenceJson, label, out assetGuid, out assetPath, out requestedType,
                out _, out error, out statusCode);

        /// <summary>
        /// Reads the asset fields of an object reference, including the optional
        /// <c>localIdentifier</c> that names one object inside a file.
        /// </summary>
        public static bool TryReadAssetReferenceFields(
            string referenceJson,
            string label,
            out string assetGuid,
            out string assetPath,
            out Type requestedType,
            out long? localIdentifier,
            out string error,
            out int statusCode)
        {
            assetGuid = null;
            assetPath = null;
            requestedType = null;
            localIdentifier = null;

            if (!TryReadReferenceField(
                    referenceJson, "assetType", label, out var requestedTypeName, out error, out statusCode))
                return false;

            requestedType = ResolveOptionalReferenceType(
                requestedTypeName,
                label,
                "Unknown object reference type for {0}: {1}",
                out error,
                out statusCode);
            if (error != null) return false;

            if (!TryReadReferenceField(referenceJson, "assetGuid", label, out assetGuid, out error, out statusCode)
                || !TryReadReferenceField(referenceJson, "assetPath", label, out assetPath, out error, out statusCode)
                || !TryReadReferenceField(referenceJson, "localIdentifier", label, out var localIdentifierText, out error, out statusCode))
                return false;

            if (string.IsNullOrEmpty(localIdentifierText)) return true;

            // A decimal string, because 64 bits do not survive a JSON number intact. Read strictly:
            // a value that is not one is the client's mistake and naming it beats resolving to
            // something else.
            if (!long.TryParse(
                    localIdentifierText,
                    System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed))
            {
                error = $"Field 'localIdentifier' of {label} must be a decimal integer in a JSON string: {localIdentifierText}";
                return false;
            }

            localIdentifier = parsed;
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
