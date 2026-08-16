using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles the material asset read:
    ///   GET /api/assets/materials/{guid} — shader, render queue, keywords, and property values
    /// </summary>
    /// <remarks>
    /// The write had no matching read: a client could set <c>_BaseColor</c> and had no way to ask
    /// what <c>_BaseColor</c> was, or what it is on the material it is trying to match. Reading the
    /// <c>.mat</c> file does not answer it either, because that file serializes the overrides Unity
    /// recorded rather than the effective values, and the property set itself comes from the
    /// shader.
    ///
    /// Every value is spelled the way <c>PATCH /api/assets/materials</c> reads it, so a read,
    /// a change to one value, and a write back is a round trip that holds.
    /// </remarks>
    internal class MaterialReadHandler
    {
        // Built once rather than per property. Enum.GetValues allocates an array and iterating it
        // boxes every element, and this runs for each of the couple of hundred properties a shader
        // like a toon shader declares.
        private static readonly KeyValuePair<ShaderPropertyFlags, string>[] FlagNames = BuildFlagNames();

        private static KeyValuePair<ShaderPropertyFlags, string>[] BuildFlagNames()
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

        public void Handle(UnionAirResponse response, string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                RestResponse.SendError(response, "Missing required path parameter: guid", 400);
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                RestResponse.SendError(response, $"Asset is not a Material: {assetPath}", 400);
                return;
            }

            var shader = mat.shader;
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");
            sb.Append($"\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",");
            sb.Append(shader == null
                ? "\"shader\":null,"
                : $"\"shader\":\"{RestResponse.EscapeJson(shader.name)}\",");

            // Reported and not writable, and the reference says so. They are here because they are
            // usually why a material built from another one's property values still does not look
            // the same, which is the question this endpoint exists to stop being unanswerable.
            sb.Append($"\"renderQueue\":{mat.renderQueue},");
            AppendKeywords(sb, mat);

            sb.Append("\"properties\":[");
            if (shader != null)
            {
                var count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) sb.Append(",");
                    AppendProperty(sb, mat, shader, i);
                }
            }
            sb.Append("]}");

            RestResponse.Send(response, sb.ToString());
        }

        private static void AppendKeywords(StringBuilder sb, Material mat)
        {
            sb.Append("\"keywords\":[");
            var keywords = mat.shaderKeywords;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(keywords[i])}\"");
            }
            sb.Append("],");
        }

        private static void AppendProperty(StringBuilder sb, Material mat, Shader shader, int index)
        {
            var name = shader.GetPropertyName(index);
            var type = shader.GetPropertyType(index);

            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(name)}\",");
            sb.Append($"\"type\":\"{type}\",");
            sb.Append("\"value\":");
            AppendValue(sb, mat, name, type);

            // The Inspector slider's bound, which the write does not enforce, so a client choosing
            // a value has something to choose within.
            if (type == ShaderPropertyType.Range)
            {
                var limits = shader.GetPropertyRangeLimits(index);
                sb.Append($",\"range\":{{\"min\":{RestResponse.FormatFloat(limits.x)},\"max\":{RestResponse.FormatFloat(limits.y)}}}");
            }

            // Unity's own flag names, unfiltered. A shader can declare a couple of hundred
            // properties and hide most of them, and without this a client cannot tell the
            // material's real surface from its plumbing. Names rather than the enum value, so a
            // flag a later Unity adds still arrives readable.
            sb.Append(",\"flags\":[");
            var flags = shader.GetPropertyFlags(index);
            var first = true;
            foreach (var known in FlagNames)
            {
                if ((flags & known.Key) != known.Key) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{known.Value}\"");
            }
            sb.Append("]}");
        }

        // The spellings PATCH /api/assets/materials accepts, so a value read here can be sent back
        // without translation.
        private static void AppendValue(StringBuilder sb, Material mat, string name, ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Color:
                {
                    var c = mat.GetColor(name);
                    sb.Append($"{{\"r\":{RestResponse.FormatFloat(c.r)},\"g\":{RestResponse.FormatFloat(c.g)},\"b\":{RestResponse.FormatFloat(c.b)},\"a\":{RestResponse.FormatFloat(c.a)}}}");
                    break;
                }
                case ShaderPropertyType.Vector:
                {
                    var v = mat.GetVector(name);
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)},\"z\":{RestResponse.FormatFloat(v.z)},\"w\":{RestResponse.FormatFloat(v.w)}}}");
                    break;
                }
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    sb.Append(RestResponse.FormatFloat(mat.GetFloat(name)));
                    break;
                case ShaderPropertyType.Int:
                    sb.Append(mat.GetInteger(name));
                    break;
                case ShaderPropertyType.Texture:
                    // null is what the write takes to clear a texture, so the empty case round
                    // trips as well as the assigned one.
                    SerializedPropertySerializer.AppendObjectReferenceJson(
                        sb, mat.GetTexture(name), false);
                    break;
                default:
                    sb.Append("null");
                    break;
            }
        }
    }
}
