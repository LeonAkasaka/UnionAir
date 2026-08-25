using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The value a new material gets for a declared shader property, spelled the way
    /// <c>PATCH /api/assets/materials</c> reads it.
    /// </summary>
    /// <remarks>
    /// Shared by <c>GET /api/assets/shaders/{guid}</c> and by the <c>unsetProperties</c> of
    /// <c>GET /api/assets/materials/{guid}/shader-compatibility</c>, for the same reason
    /// <see cref="ShaderPropertyFlagsJson"/> is shared: two endpoints reporting the same value in
    /// two spellings is a difference a client would have to discover.
    /// </remarks>
    internal static class ShaderPropertyDefaultJson
    {
        /// <summary>
        /// Appends the declared default of <paramref name="index"/> on <paramref name="shader"/>,
        /// so a client can tell an untouched property from a deliberate one.
        /// </summary>
        internal static void Append(StringBuilder sb, Shader shader, int index, ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Color:
                {
                    var v = shader.GetPropertyDefaultVectorValue(index);
                    sb.Append($"{{\"r\":{RestResponse.FormatFloat(v.x)},\"g\":{RestResponse.FormatFloat(v.y)},\"b\":{RestResponse.FormatFloat(v.z)},\"a\":{RestResponse.FormatFloat(v.w)}}}");
                    break;
                }
                case ShaderPropertyType.Vector:
                {
                    var v = shader.GetPropertyDefaultVectorValue(index);
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)},\"z\":{RestResponse.FormatFloat(v.z)},\"w\":{RestResponse.FormatFloat(v.w)}}}");
                    break;
                }
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    sb.Append(RestResponse.FormatFloat(shader.GetPropertyDefaultFloatValue(index)));
                    break;
                case ShaderPropertyType.Int:
                    sb.Append(shader.GetPropertyDefaultIntValue(index).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case ShaderPropertyType.Texture:
                    // A built-in texture name — "white", "bump", "gray" — rather than an object
                    // reference, because that is what the declaration carries and no asset exists
                    // to point at. null means the declaration named none.
                    sb.Append(RestResponse.FormatNullableString(
                        string.IsNullOrEmpty(shader.GetPropertyTextureDefaultName(index))
                            ? null
                            : shader.GetPropertyTextureDefaultName(index)));
                    break;
                default:
                    sb.Append("null");
                    break;
            }
        }
    }
}
