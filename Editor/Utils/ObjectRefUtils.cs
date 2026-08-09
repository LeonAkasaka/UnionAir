using System;
using System.Collections.Specialized;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    internal enum ObjectRefType
    {
        HierarchyPath,
        ComponentPath,
        GlobalObjectId
    }

    internal struct ObjectRef
    {
        public ObjectRefType Type;
        public string Value;

        public ObjectRef(ObjectRefType type, string value)
        {
            Type = type;
            Value = value;
        }
    }

    internal static class ObjectRefUtils
    {
        public static bool TryReadQuery(
            NameValueCollection query,
            string fieldName,
            out ObjectRef objectRef,
            out string error,
            out int statusCode)
        {
            return TryParse(query[fieldName], fieldName, out objectRef, out error, out statusCode);
        }

        public static bool TryReadBody(
            string body,
            string fieldName,
            out ObjectRef objectRef,
            out string error,
            out int statusCode)
        {
            var rawObject = RequestBodyReader.GetObject(body, fieldName);
            if (rawObject == null && RequestBodyReader.GetString(body, fieldName) != null)
            {
                objectRef = default(ObjectRef);
                error = $"Field {fieldName} must be an ObjectRef object such as " +
                        "{\"type\":\"hierarchyPath\",\"value\":\"Canvas/Button\"}.";
                statusCode = 400;
                return false;
            }

            return TryParse(rawObject, fieldName, out objectRef, out error, out statusCode);
        }

        public static bool TryParse(
            string rawValue,
            string fieldName,
            out ObjectRef objectRef,
            out string error,
            out int statusCode)
        {
            objectRef = default(ObjectRef);
            error = null;
            statusCode = 400;

            if (string.IsNullOrEmpty(rawValue))
            {
                error = $"Missing required field: {fieldName}";
                return false;
            }

            rawValue = rawValue.Trim();
            if (rawValue.Length == 0 || rawValue[0] != '{')
            {
                error = $"Field {fieldName} must be an ObjectRef object with type and value.";
                return false;
            }

            var value = RequestBodyReader.GetString(rawValue, "value");
            if (string.IsNullOrEmpty(value))
            {
                error = $"Missing required field: {fieldName}.value";
                return false;
            }

            var typeName = RequestBodyReader.GetString(rawValue, "type") ?? "hierarchyPath";
            if (!TryParseType(typeName, out var type))
            {
                error = $"Unknown {fieldName}.type: {typeName}";
                return false;
            }

            objectRef = new ObjectRef(type, value);
            return true;
        }

        public static bool TryResolveGameObject(
            Scene scene,
            ObjectRef objectRef,
            string label,
            out GameObject go,
            out string error,
            out int statusCode)
        {
            go = null;
            error = null;
            statusCode = 400;

            switch (objectRef.Type)
            {
                case ObjectRefType.HierarchyPath:
                    go = GameObjectUtils.FindByPath(scene, objectRef.Value);
                    if (go == null)
                    {
                        error = $"GameObject not found for {label}: {objectRef.Value}";
                        statusCode = 404;
                        return false;
                    }
                    return true;

                case ObjectRefType.GlobalObjectId:
                    return ObjectIdUtils.TryResolveGameObject(objectRef.Value, out go, out error, out statusCode);

                case ObjectRefType.ComponentPath:
                    error = $"{label} must resolve to a GameObject; componentPath is not valid here.";
                    statusCode = 422;
                    return false;
            }

            error = $"Unsupported {label}.type.";
            return false;
        }

        public static bool TryResolveGameObjectOrComponent(
            Scene scene,
            ObjectRef objectRef,
            string label,
            out GameObject go,
            out Component component,
            out string error,
            out int statusCode)
        {
            go = null;
            component = null;
            error = null;
            statusCode = 400;

            switch (objectRef.Type)
            {
                case ObjectRefType.HierarchyPath:
                    if (!TryResolveGameObject(scene, objectRef, label, out go, out error, out statusCode))
                        return false;
                    return true;

                case ObjectRefType.ComponentPath:
                    return TryResolveComponentPath(scene, objectRef.Value, label, out go, out component, out error, out statusCode);

                case ObjectRefType.GlobalObjectId:
                    if (!ObjectIdUtils.TryResolveGameObjectOrComponent(objectRef.Value, out go, out component, out error, out statusCode))
                        return false;

                    if (component != null)
                        go = component.gameObject;
                    return true;
            }

            error = $"Unsupported {label}.type.";
            return false;
        }

        public static bool TryResolveComponent(
            Scene scene,
            ObjectRef objectRef,
            string label,
            out GameObject go,
            out Component component,
            out string error,
            out int statusCode)
        {
            if (!TryResolveGameObjectOrComponent(scene, objectRef, label, out go, out component, out error, out statusCode))
                return false;

            if (component == null)
            {
                error = $"{label} must resolve to a Component.";
                statusCode = 422;
                return false;
            }

            return true;
        }

        public static bool TryResolveObject(
            Scene scene,
            ObjectRef objectRef,
            string label,
            out UnityEngine.Object value,
            out string error,
            out int statusCode)
        {
            value = null;

            if (!TryResolveGameObjectOrComponent(scene, objectRef, label, out var go, out var component, out error, out statusCode))
                return false;

            value = component != null ? (UnityEngine.Object)component : go;
            return true;
        }

        public static bool TryResolveCamera(
            Scene scene,
            ObjectRef objectRef,
            out Camera camera,
            out string error,
            out int statusCode)
        {
            camera = null;

            if (!TryResolveGameObjectOrComponent(scene, objectRef, "target", out var go, out var component, out error, out statusCode))
                return false;

            camera = component as Camera;
            if (camera != null) return true;

            if (go != null)
                camera = go.GetComponent<Camera>();

            if (camera != null) return true;

            error = $"No Camera component found for target: {objectRef.Value}";
            statusCode = 404;
            return false;
        }

        private static bool TryResolveComponentPath(
            Scene scene,
            string value,
            string label,
            out GameObject go,
            out Component component,
            out string error,
            out int statusCode)
        {
            go = null;
            component = null;
            error = null;
            statusCode = 400;

            var split = value.LastIndexOf(':');
            if (split <= 0 || split == value.Length - 1)
            {
                error = $"{label}.value must use GameObjectPath:ComponentType for componentPath.";
                return false;
            }

            var path = value.Substring(0, split);
            var typeName = value.Substring(split + 1);

            go = GameObjectUtils.FindByPath(scene, path);
            if (go == null)
            {
                error = $"GameObject not found for {label}: {path}";
                statusCode = 404;
                return false;
            }

            var componentType = ResolveType(typeName, typeof(Component));
            if (componentType == null)
            {
                error = $"Unknown component type for {label}: {typeName}";
                return false;
            }

            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Type is not a Component for {label}: {typeName}";
                return false;
            }

            component = go.GetComponent(componentType);
            if (component == null)
            {
                error = $"Component {typeName} not found on {path}";
                statusCode = 404;
                return false;
            }

            return true;
        }

        public static Type ResolveType(string typeName, Type requiredBaseType = null)
        {
            // Assembly.GetType throws on an empty name rather than answering null, so an
            // empty type name reached the caller as a 500 carrying the exception text.
            // Every caller today rejects an empty name before getting here, which is why
            // it was never seen; a caller that defaults the field instead of requiring it
            // does reach this.
            if (string.IsNullOrEmpty(typeName)) return null;

            var t = Type.GetType(typeName);
            if (Matches(t, requiredBaseType)) return t;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(typeName);
                if (Matches(t, requiredBaseType)) return t;

                // Assembly.GetTypes throws rather than returning what loaded when any type
                // in the assembly does not, and one assembly in that state used to take the
                // whole lookup with it: a name that resolves to nothing answered 500 with
                // the reflection exception in the body instead of "Unknown type". Unity's
                // own expression evaluator puts such an assembly in the domain -- a dynamic
                // one holding an uncreated TypeBuilder -- so it appears and disappears
                // during a session with nothing in the project to explain it.
                //
                // ReflectionTypeLoadException.Types carries the types that did load with
                // null in the gaps, so the assembly still contributes its candidates. This
                // is the guard EditorMenuItemsHandler and UnionAirRouteRegistry already use
                // on the same call.
                Type[] candidates;
                try { candidates = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { candidates = ex.Types; }
                catch { continue; }

                foreach (var candidate in candidates)
                {
                    if (candidate == null) continue;
                    if ((candidate.Name == typeName || candidate.FullName == typeName) &&
                        Matches(candidate, requiredBaseType))
                        return candidate;
                }
            }
            return null;
        }

        private static bool Matches(Type type, Type requiredBaseType)
        {
            if (type == null) return false;
            return requiredBaseType == null || requiredBaseType.IsAssignableFrom(type);
        }

        private static bool TryParseType(string typeName, out ObjectRefType type)
        {
            if (string.Equals(typeName, "hierarchyPath", StringComparison.OrdinalIgnoreCase))
            {
                type = ObjectRefType.HierarchyPath;
                return true;
            }
            if (string.Equals(typeName, "componentPath", StringComparison.OrdinalIgnoreCase))
            {
                type = ObjectRefType.ComponentPath;
                return true;
            }
            if (string.Equals(typeName, "globalObjectId", StringComparison.OrdinalIgnoreCase))
            {
                type = ObjectRefType.GlobalObjectId;
                return true;
            }

            type = default(ObjectRefType);
            return false;
        }
    }
}
