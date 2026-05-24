using System;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Shared helpers for Unity GlobalObjectId serialization and resolution.
    /// </summary>
    internal static class ObjectIdUtils
    {
        public static string GetGlobalObjectId(UnityEngine.Object obj)
        {
            if (obj == null) return string.Empty;
            return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        }

        public static bool TryResolveObject(
            string globalObjectId,
            out UnityEngine.Object obj,
            out string error,
            out int statusCode)
        {
            obj = null;
            error = null;
            statusCode = 400;

            if (string.IsNullOrEmpty(globalObjectId))
            {
                error = "Missing globalObjectId.";
                return false;
            }

            GlobalObjectId id;
            if (!GlobalObjectId.TryParse(globalObjectId, out id))
            {
                error = $"Malformed globalObjectId: {globalObjectId}";
                return false;
            }

            obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            if (obj == null)
            {
                error = $"Object not found for globalObjectId: {globalObjectId}";
                statusCode = 404;
                return false;
            }

            return true;
        }

        public static bool TryResolveGameObject(
            string globalObjectId,
            out GameObject go,
            out string error,
            out int statusCode)
        {
            go = null;

            if (!TryResolveObject(globalObjectId, out var obj, out error, out statusCode))
                return false;

            go = obj as GameObject;
            if (go == null)
            {
                error = $"globalObjectId does not resolve to a GameObject: {globalObjectId}";
                statusCode = 422;
                return false;
            }

            return true;
        }

        public static bool TryResolveComponent(
            string globalObjectId,
            out Component component,
            out string error,
            out int statusCode)
        {
            component = null;

            if (!TryResolveObject(globalObjectId, out var obj, out error, out statusCode))
                return false;

            component = obj as Component;
            if (component == null)
            {
                error = $"globalObjectId does not resolve to a Component: {globalObjectId}";
                statusCode = 422;
                return false;
            }

            return true;
        }

        public static bool TryResolveGameObjectOrComponent(
            string globalObjectId,
            out GameObject go,
            out Component component,
            out string error,
            out int statusCode)
        {
            go = null;
            component = null;

            if (!TryResolveObject(globalObjectId, out var obj, out error, out statusCode))
                return false;

            go = obj as GameObject;
            component = obj as Component;
            if (go == null && component == null)
            {
                error = $"globalObjectId does not resolve to a GameObject or Component: {globalObjectId}";
                statusCode = 422;
                return false;
            }

            return true;
        }
    }
}
