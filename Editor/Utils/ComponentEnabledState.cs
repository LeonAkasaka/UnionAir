using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Reads and writes the checkbox in a component's Inspector header.
    /// </summary>
    /// <remarks>
    /// There is no single base class to test: <see cref="Behaviour"/>, <see cref="Renderer"/> and
    /// <see cref="Collider"/> each declare their own <c>enabled</c>, and a component that declares
    /// none — a <see cref="Transform"/>, a <see cref="MeshFilter"/> — has no checkbox at all. What
    /// the ones that have it share is the serialized field behind it, so that is what is asked for
    /// here rather than a list of types nobody can keep complete.
    ///
    /// It is the same field the property endpoints cannot reach: Unity draws it in the component
    /// header rather than in the body, and the walk those endpoints address properties through
    /// returns only what the body draws.
    /// </remarks>
    internal static class ComponentEnabledState
    {
        private const string PropertyName = "m_Enabled";

        /// <summary>
        /// Returns the serialized enabled state of <paramref name="so"/>'s component, or null when
        /// the component has none.
        /// </summary>
        internal static SerializedProperty Find(SerializedObject so)
        {
            if (so == null) return null;
            var prop = so.FindProperty(PropertyName);
            return prop != null && prop.propertyType == SerializedPropertyType.Boolean ? prop : null;
        }

        /// <summary>
        /// Reads the state through an existing <see cref="SerializedObject"/>, or null when the
        /// component has none.
        /// </summary>
        internal static bool? Read(SerializedObject so)
        {
            var prop = Find(so);
            return prop == null ? (bool?)null : prop.boolValue;
        }

        /// <summary>Reads the state of <paramref name="component"/>, or null when it has none.</summary>
        internal static bool? Read(Component component)
        {
            if (component == null) return null;
            try
            {
                return Read(new SerializedObject(component));
            }
            catch
            {
                // Matches the read path's treatment of components it cannot serialize.
                return null;
            }
        }
    }
}
