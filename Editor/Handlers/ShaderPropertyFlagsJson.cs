using System.Collections.Generic;
using System.Text;
using UnityEngine.Rendering;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Renders <see cref="ShaderPropertyFlags"/> as the JSON array both the material read and the
    /// shader read report.
    /// </summary>
    /// <remarks>
    /// Unity's own flag names, unfiltered. A shader can declare a couple of hundred properties and
    /// hide most of them, and without this a client cannot tell a material's real surface from its
    /// plumbing. Names rather than the enum value, so a flag a later Unity adds still arrives
    /// readable.
    /// </remarks>
    internal static class ShaderPropertyFlagsJson
    {
        // Built once rather than per property. Enum.GetValues allocates an array and iterating it
        // boxes every element, and this runs for each of the couple of hundred properties a shader
        // like a toon shader declares.
        private static readonly KeyValuePair<ShaderPropertyFlags, string>[] Names = BuildNames();

        private static KeyValuePair<ShaderPropertyFlags, string>[] BuildNames()
        {
            var values = System.Enum.GetValues(typeof(ShaderPropertyFlags));
            var names = new List<KeyValuePair<ShaderPropertyFlags, string>>(values.Length);
            foreach (ShaderPropertyFlags flag in values)
            {
                if (flag == ShaderPropertyFlags.None) continue;
                names.Add(new KeyValuePair<ShaderPropertyFlags, string>(flag, flag.ToString()));
            }
            return names.ToArray();
        }

        /// <summary>Appends the flags as a JSON array, without a key.</summary>
        public static void AppendArray(StringBuilder sb, ShaderPropertyFlags flags)
        {
            sb.Append("[");
            var first = true;
            foreach (var known in Names)
            {
                if ((flags & known.Key) != known.Key) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{known.Value}\"");
            }
            sb.Append("]");
        }
    }
}
