using System;
using System.Collections.Specialized;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Typed object reference kinds accepted by UnionAir request payloads.
    /// </summary>
    public enum UnionAirObjectReferenceType
    {
        /// <summary>A GameObject hierarchy path, such as <c>Canvas/Button</c>.</summary>
        HierarchyPath,

        /// <summary>A component path in <c>GameObjectPath:ComponentType</c> form.</summary>
        ComponentPath,

        /// <summary>A Unity <see cref="GlobalObjectId"/> string.</summary>
        GlobalObjectId
    }

    /// <summary>
    /// Parsed representation of a UnionAir typed object reference.
    /// </summary>
    public struct UnionAirObjectReference
    {
        /// <summary>
        /// Creates a typed object reference.
        /// </summary>
        /// <param name="type">Reference type.</param>
        /// <param name="value">Reference value.</param>
        public UnionAirObjectReference(UnionAirObjectReferenceType type, string value)
        {
            Type = type;
            Value = value;
        }

        /// <summary>
        /// Reference type.
        /// </summary>
        public UnionAirObjectReferenceType Type { get; }

        /// <summary>
        /// Reference value.
        /// </summary>
        public string Value { get; }
    }

    /// <summary>
    /// Public helpers for custom UnionAir controllers that need to parse and resolve UnionAir object references.
    /// </summary>
    public static class UnionAirReferenceResolver
    {
        /// <summary>
        /// Reads and parses a typed object reference from a query string field.
        /// </summary>
        public static bool TryReadQuery(
            NameValueCollection query,
            string fieldName,
            out UnionAirObjectReference objectReference,
            out string error,
            out int statusCode)
        {
            objectReference = default(UnionAirObjectReference);
            if (!ObjectRefUtils.TryReadQuery(query, fieldName, out var parsed, out error, out statusCode))
                return false;

            objectReference = ToPublic(parsed);
            return true;
        }

        /// <summary>
        /// Reads and parses a typed object reference from a JSON body field.
        /// </summary>
        public static bool TryReadBody(
            string body,
            string fieldName,
            out UnionAirObjectReference objectReference,
            out string error,
            out int statusCode)
        {
            objectReference = default(UnionAirObjectReference);
            if (!ObjectRefUtils.TryReadBody(body, fieldName, out var parsed, out error, out statusCode))
                return false;

            objectReference = ToPublic(parsed);
            return true;
        }

        /// <summary>
        /// Parses a raw typed object reference JSON value.
        /// </summary>
        public static bool TryParse(
            string rawValue,
            string fieldName,
            out UnionAirObjectReference objectReference,
            out string error,
            out int statusCode)
        {
            objectReference = default(UnionAirObjectReference);
            if (!ObjectRefUtils.TryParse(rawValue, fieldName, out var parsed, out error, out statusCode))
                return false;

            objectReference = ToPublic(parsed);
            return true;
        }

        /// <summary>
        /// Resolves the optional <c>scenePath</c> selector from a request query string or JSON body.
        /// </summary>
        public static bool TryResolveSceneFromRequest(
            UnionAirRequest request,
            string body,
            out Scene scene,
            out string error,
            out int statusCode)
        {
            var scenePath = request.QueryString["scenePath"];
            if (string.IsNullOrEmpty(scenePath) && !string.IsNullOrEmpty(body))
                scenePath = RequestBodyReader.GetString(body, "scenePath");

            return TryResolveOptionalScene(scenePath, out scene, out error, out statusCode);
        }

        /// <summary>
        /// Resolves a loaded scene by path or unambiguous scene name, or returns the active scene when omitted.
        /// </summary>
        public static bool TryResolveOptionalScene(
            string scenePathOrName,
            out Scene scene,
            out string error,
            out int statusCode)
        {
            error = null;
            statusCode = 400;

            if (string.IsNullOrEmpty(scenePathOrName))
            {
                scene = EditorSceneManager.GetActiveScene();
                return true;
            }

            return TryResolveRequiredScene(scenePathOrName, out scene, out error, out statusCode);
        }

        /// <summary>
        /// Resolves a required loaded scene by path or unambiguous scene name.
        /// </summary>
        public static bool TryResolveRequiredScene(
            string scenePathOrName,
            out Scene scene,
            out string error,
            out int statusCode)
        {
            var status = SceneResolver.ResolveLoaded(scenePathOrName, out scene, out error);
            if (status == ResolveStatus.Found)
            {
                statusCode = 200;
                return true;
            }

            statusCode = status == ResolveStatus.Ambiguous ? 409 : 404;
            return false;
        }

        /// <summary>
        /// Resolves a typed object reference to a GameObject.
        /// </summary>
        public static bool TryResolveGameObject(
            Scene scene,
            UnionAirObjectReference objectReference,
            string label,
            out GameObject gameObject,
            out string error,
            out int statusCode)
        {
            return ObjectRefUtils.TryResolveGameObject(
                scene,
                ToInternal(objectReference),
                label,
                out gameObject,
                out error,
                out statusCode);
        }

        /// <summary>
        /// Resolves a typed object reference to a Component.
        /// </summary>
        public static bool TryResolveComponent(
            Scene scene,
            UnionAirObjectReference objectReference,
            string label,
            out GameObject gameObject,
            out Component component,
            out string error,
            out int statusCode)
        {
            return ObjectRefUtils.TryResolveComponent(
                scene,
                ToInternal(objectReference),
                label,
                out gameObject,
                out component,
                out error,
                out statusCode);
        }

        /// <summary>
        /// Resolves a typed object reference to a GameObject or Component.
        /// </summary>
        public static bool TryResolveGameObjectOrComponent(
            Scene scene,
            UnionAirObjectReference objectReference,
            string label,
            out GameObject gameObject,
            out Component component,
            out string error,
            out int statusCode)
        {
            return ObjectRefUtils.TryResolveGameObjectOrComponent(
                scene,
                ToInternal(objectReference),
                label,
                out gameObject,
                out component,
                out error,
                out statusCode);
        }

        /// <summary>
        /// Resolves a typed object reference to a Unity object.
        /// </summary>
        public static bool TryResolveObject(
            Scene scene,
            UnionAirObjectReference objectReference,
            string label,
            out UnityEngine.Object value,
            out string error,
            out int statusCode)
        {
            return ObjectRefUtils.TryResolveObject(
                scene,
                ToInternal(objectReference),
                label,
                out value,
                out error,
                out statusCode);
        }

        /// <summary>
        /// Resolves a typed object reference to a Camera component.
        /// </summary>
        public static bool TryResolveCamera(
            Scene scene,
            UnionAirObjectReference objectReference,
            out Camera camera,
            out string error,
            out int statusCode)
        {
            return ObjectRefUtils.TryResolveCamera(
                scene,
                ToInternal(objectReference),
                out camera,
                out error,
                out statusCode);
        }

        /// <summary>
        /// Returns the Unity GlobalObjectId string for an object.
        /// </summary>
        public static string GetGlobalObjectId(UnityEngine.Object value)
        {
            return ObjectIdUtils.GetGlobalObjectId(value);
        }

        /// <summary>
        /// Resolves a Unity GlobalObjectId string to a Unity object.
        /// </summary>
        public static bool TryResolveGlobalObjectId(
            string globalObjectId,
            out UnityEngine.Object value,
            out string error,
            out int statusCode)
        {
            return ObjectIdUtils.TryResolveObject(globalObjectId, out value, out error, out statusCode);
        }

        /// <summary>
        /// Resolves a Unity GlobalObjectId string to a GameObject.
        /// </summary>
        public static bool TryResolveGlobalObjectIdAsGameObject(
            string globalObjectId,
            out GameObject gameObject,
            out string error,
            out int statusCode)
        {
            return ObjectIdUtils.TryResolveGameObject(globalObjectId, out gameObject, out error, out statusCode);
        }

        /// <summary>
        /// Resolves a Unity GlobalObjectId string to a Component.
        /// </summary>
        public static bool TryResolveGlobalObjectIdAsComponent(
            string globalObjectId,
            out Component component,
            out string error,
            out int statusCode)
        {
            return ObjectIdUtils.TryResolveComponent(globalObjectId, out component, out error, out statusCode);
        }

        /// <summary>
        /// Resolves a Unity GlobalObjectId string to a GameObject or Component.
        /// </summary>
        public static bool TryResolveGlobalObjectIdAsGameObjectOrComponent(
            string globalObjectId,
            out GameObject gameObject,
            out Component component,
            out string error,
            out int statusCode)
        {
            return ObjectIdUtils.TryResolveGameObjectOrComponent(
                globalObjectId,
                out gameObject,
                out component,
                out error,
                out statusCode);
        }

        /// <summary>
        /// Resolves an asset object reference from an asset GUID or asset path.
        /// </summary>
        public static bool TryResolveAssetReference(
            string assetGuid,
            string assetPath,
            Type expectedType,
            Type requestedType,
            string label,
            out UnityEngine.Object value,
            out string error,
            out int statusCode)
        {
            return ObjectReferenceResolverUtils.TryResolveAssetReference(
                assetGuid,
                assetPath,
                expectedType,
                requestedType,
                label,
                "{0} requires assetGuid or assetPath.",
                "Asset not found for {0} with GUID: {1}",
                "Asset not found or incompatible for {0}: {1}",
                "Resolved object for {0} is not assignable to expected type {1}.",
                out value,
                out error,
                out statusCode);
        }

        /// <summary>
        /// Resolves an asset object reference from raw JSON containing <c>assetGuid</c> or <c>assetPath</c>.
        /// </summary>
        public static bool TryResolveAssetReference(
            string rawValue,
            Type expectedType,
            string label,
            out UnityEngine.Object value,
            out string error,
            out int statusCode)
        {
            value = null;
            error = null;
            statusCode = 400;

            if (!ObjectReferenceResolverUtils.TryReadReferenceField(
                    rawValue, "assetType", label, out var requestedTypeName, out error, out statusCode))
                return false;
            var requestedType = ObjectReferenceResolverUtils.ResolveOptionalReferenceType(
                requestedTypeName,
                label,
                "Unknown object reference type for {0}: {1}",
                out error,
                out statusCode);
            if (error != null) return false;

            if (!ObjectReferenceResolverUtils.TryReadReferenceField(
                    rawValue, "assetGuid", label, out var assetGuid, out error, out statusCode) ||
                !ObjectReferenceResolverUtils.TryReadReferenceField(
                    rawValue, "assetPath", label, out var assetPath, out error, out statusCode))
                return false;

            return TryResolveAssetReference(
                assetGuid,
                assetPath,
                expectedType,
                requestedType,
                label,
                out value,
                out error,
                out statusCode);
        }

        /// <summary>
        /// Resolves a type name using the same lookup behavior as UnionAir's built-in handlers.
        /// </summary>
        public static Type ResolveType(string typeName)
        {
            return ObjectRefUtils.ResolveType(typeName);
        }

        private static UnionAirObjectReference ToPublic(ObjectRef objectReference)
        {
            return new UnionAirObjectReference(ToPublic(objectReference.Type), objectReference.Value);
        }

        private static ObjectRef ToInternal(UnionAirObjectReference objectReference)
        {
            return new ObjectRef(ToInternal(objectReference.Type), objectReference.Value);
        }

        private static UnionAirObjectReferenceType ToPublic(ObjectRefType type)
        {
            switch (type)
            {
                case ObjectRefType.HierarchyPath:
                    return UnionAirObjectReferenceType.HierarchyPath;
                case ObjectRefType.ComponentPath:
                    return UnionAirObjectReferenceType.ComponentPath;
                case ObjectRefType.GlobalObjectId:
                    return UnionAirObjectReferenceType.GlobalObjectId;
                default:
                    return UnionAirObjectReferenceType.HierarchyPath;
            }
        }

        private static ObjectRefType ToInternal(UnionAirObjectReferenceType type)
        {
            switch (type)
            {
                case UnionAirObjectReferenceType.HierarchyPath:
                    return ObjectRefType.HierarchyPath;
                case UnionAirObjectReferenceType.ComponentPath:
                    return ObjectRefType.ComponentPath;
                case UnionAirObjectReferenceType.GlobalObjectId:
                    return ObjectRefType.GlobalObjectId;
                default:
                    return ObjectRefType.HierarchyPath;
            }
        }

    }
}
