using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles the shader asset read:
    ///   GET /api/assets/shaders/{guid} — by asset GUID
    ///   GET /api/assets/shaders?name= — by the shader name a material carries
    /// </summary>
    /// <remarks>
    /// A client can already write a <c>.shader</c> file itself, and this endpoint does not take
    /// that over. What the file cannot answer is whether Unity accepted the import: shader
    /// compilation happens at import time, its diagnostics carry a platform, a file and a line that
    /// the Console flattens into prose, and a shader that failed still exists on disk looking
    /// exactly as it did. <c>hasError</c> and <c>messages</c> are that answer, which makes an
    /// edit-import-diagnose cycle terminate the way the C# one already does through
    /// <c>POST /api/compile</c>.
    ///
    /// The property set is the second half. <c>GET /api/assets/materials/{guid}</c> reports the
    /// properties of a shader a material already uses; a client choosing a shader for a material it
    /// has not created yet had nowhere to ask what that shader declares, what a property defaults
    /// to, or which keywords are valid on it. A Shader Graph asset does not answer any of that from
    /// its file at all — the properties and passes are generated during import.
    ///
    /// Diagnostics are the ones Unity cached when the asset was last imported, not a fresh compile.
    /// After editing the file, reimport it — <c>POST /api/assets/reimport</c> or
    /// <c>POST /api/editor/refresh</c> — and read again.
    /// </remarks>
    internal class ShaderReadHandler
    {
        public void HandleByGuid(UnionAirResponse response, string guid)
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

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            if (shader == null)
            {
                RestResponse.SendError(response, $"Asset is not a Shader: {assetPath}", 400);
                return;
            }

            Send(response, shader, guid, assetPath);
        }

        public void HandleByName(UnionAirRequest request, UnionAirResponse response)
        {
            var name = request.QueryString["name"];
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required query parameter: name", 400);
                return;
            }

            // The same lookup POST /api/assets/materials performs, so a name this endpoint reports
            // nothing for is a name that endpoint would also fail on.
            var shader = Shader.Find(name);
            if (shader == null)
            {
                RestResponse.SendNotFound(response, $"No shader found with name: {name}");
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(shader);
            Send(response, shader, AssetDatabase.AssetPathToGUID(assetPath), assetPath);
        }

        private static void Send(UnionAirResponse response, Shader shader, string guid, string assetPath)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            // A shader Unity built into the editor reports the shared built-in resource container
            // rather than an identity of its own: measured on 6000.0.80f1, `Standard` reports
            // `Resources/unity_builtin_extra` and the GUID every built-in asset shares, and reading
            // that GUID back answers 400 because the container's main asset is not a Shader. That
            // is reported as it is rather than corrected — the shader is real, every other field
            // describes it, and the lookup by name is how it is reached a second time.
            sb.Append($"\"guid\":{RestResponse.FormatNullableString(NullIfEmpty(guid))},");
            sb.Append($"\"assetPath\":{RestResponse.FormatNullableString(NullIfEmpty(assetPath))},");
            sb.Append($"\"name\":{RestResponse.FormatNullableString(NullIfEmpty(shader.name))},");

            // Whether the object Unity holds is the client's shader at all. False when the import
            // failed and Unity substituted its error shader, and false when the shader imported but
            // no subshader survives for this platform and pipeline.
            sb.Append($"\"isSupported\":{RestResponse.FormatBool(shader.isSupported)},");

            // Before the structure, because when the structure is absent this is the answer.
            AppendDiagnostics(sb, shader);
            AppendStructure(sb, shader);

            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>
        /// Everything that describes the shader itself rather than its import, or nulls when the
        /// object Unity holds is not the client's shader.
        /// </summary>
        /// <remarks>
        /// Measured on 6000.0.80f1 against a shader with one property and one pass that Unity
        /// rejected with a ShaderLab parse error: <c>name</c> read <c>""</c>, <c>properties</c> read
        /// empty, <c>keywords</c> listed four stereo keywords the shader never declared, and
        /// <c>passCount</c> read 3. Every one of those describes Unity's error shader. Reporting
        /// them would be handing a client a shader it did not write and no way to tell — a client
        /// building a material from <c>properties</c> would build one with no properties and never
        /// learn why. They are reported as <c>null</c> together, under one rule, so that a client
        /// checks <c>isSupported</c> once rather than learning which fields happen to lie.
        ///
        /// The rule is <c>isSupported</c> and deliberately not <c>hasError</c>. A shader can carry
        /// errors and still be the shader Unity uses: measured against a shader whose first
        /// subshader fails to compile and whose second does not, <c>hasError</c> is true while
        /// <c>isSupported</c> is true and Unity selects the working subshader. Keying off
        /// <c>hasError</c> would hide the structure of a shader that is working, which is the case a
        /// client most needs described.
        /// </remarks>
        private static void AppendStructure(StringBuilder sb, Shader shader)
        {
            if (!shader.isSupported)
            {
                sb.Append("\"renderQueue\":null,\"maximumLOD\":null,\"subshaderCount\":null,");
                sb.Append("\"passCount\":null,\"keywords\":null,\"properties\":null,");
                sb.Append("\"activeSubshaderIndex\":null,\"subshaders\":null");
                return;
            }

            sb.Append($"\"renderQueue\":{Int(shader.renderQueue)},");
            sb.Append($"\"maximumLOD\":{Int(shader.maximumLOD)},");
            sb.Append($"\"subshaderCount\":{Int(shader.subshaderCount)},");
            sb.Append($"\"passCount\":{Int(shader.passCount)},");

            AppendKeywords(sb, shader);
            AppendProperties(sb, shader);
            AppendSubshaders(sb, shader);
        }

        /// <summary>
        /// The compiler messages Unity recorded for the asset, which is what no file read reaches.
        /// </summary>
        private static void AppendDiagnostics(StringBuilder sb, Shader shader)
        {
            sb.Append($"\"hasError\":{RestResponse.FormatBool(ShaderUtil.ShaderHasError(shader))},");
            sb.Append($"\"hasWarnings\":{RestResponse.FormatBool(ShaderUtil.ShaderHasWarnings(shader))},");

            sb.Append("\"messages\":[");
            var messages = ShaderUtil.GetShaderMessages(shader);
            for (var i = 0; i < messages.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var m = messages[i];

                // Kept as separate fields rather than one string: severity decides whether the
                // import failed, file and line locate it in source the client wrote, and platform
                // is why the same edit can be an error on one graphics API and silent on another.
                sb.Append("{");
                sb.Append($"\"severity\":\"{m.severity}\",");
                sb.Append($"\"message\":\"{RestResponse.EscapeJson(m.message)}\",");
                sb.Append($"\"messageDetails\":{RestResponse.FormatNullableString(NullIfEmpty(m.messageDetails))},");
                sb.Append($"\"file\":{RestResponse.FormatNullableString(NullIfEmpty(m.file))},");
                sb.Append($"\"line\":{Int(m.line)},");
                sb.Append($"\"platform\":{RestResponse.FormatNullableString(PlatformName(m.platform))}");
                sb.Append("}");
            }
            sb.Append("],");
        }

        /// <summary>
        /// The shader's local keyword space — every keyword it declares, enabled or not.
        /// </summary>
        /// <remarks>
        /// <c>GET /api/assets/materials/{guid}</c> reports the keywords a material has enabled and
        /// cannot report the ones it could enable, because a material only stores the set that is
        /// on. The valid set belongs to the shader.
        /// </remarks>
        private static void AppendKeywords(StringBuilder sb, Shader shader)
        {
            sb.Append("\"keywords\":[");
            var keywords = shader.keywordSpace.keywords;
            for (var i = 0; i < keywords.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var keyword = keywords[i];
                sb.Append("{");
                sb.Append($"\"name\":\"{RestResponse.EscapeJson(keyword.name)}\",");
                sb.Append($"\"isOverridable\":{RestResponse.FormatBool(keyword.isOverridable)},");
                sb.Append($"\"isDynamic\":{RestResponse.FormatBool(keyword.isDynamic)}");
                sb.Append("}");
            }
            sb.Append("],");
        }

        private static void AppendProperties(StringBuilder sb, Shader shader)
        {
            sb.Append("\"properties\":[");
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendProperty(sb, shader, i);
            }
            sb.Append("]");
        }

        private static void AppendProperty(StringBuilder sb, Shader shader, int index)
        {
            var type = shader.GetPropertyType(index);

            sb.Append("{");
            sb.Append($"\"name\":\"{RestResponse.EscapeJson(shader.GetPropertyName(index))}\",");
            sb.Append($"\"type\":\"{type}\",");

            // The Inspector label. It is the only human-readable name a property has, and a client
            // matching a request like "make it rougher" to a property has nothing else to match on.
            sb.Append($"\"description\":{RestResponse.FormatNullableString(NullIfEmpty(shader.GetPropertyDescription(index)))},");

            sb.Append("\"defaultValue\":");
            AppendDefaultValue(sb, shader, index, type);

            if (type == ShaderPropertyType.Range)
            {
                var limits = shader.GetPropertyRangeLimits(index);
                sb.Append($",\"range\":{{\"min\":{RestResponse.FormatFloat(limits.x)},\"max\":{RestResponse.FormatFloat(limits.y)}}}");
            }

            // What kind of texture the property expects, which the write does not check and a
            // client assigning one otherwise finds out by looking at the result.
            if (type == ShaderPropertyType.Texture)
                sb.Append($",\"textureDimension\":\"{shader.GetPropertyTextureDimension(index)}\"");

            sb.Append(",\"flags\":");
            ShaderPropertyFlagsJson.AppendArray(sb, shader.GetPropertyFlags(index));

            // The attributes Unity did not turn into a flag, verbatim and with their arguments.
            // [HideInInspector] and [MainTexture] arrive in `flags` instead; [Toggle(_X)], which
            // is how a keyword becomes reachable from a property, has no flag and would otherwise
            // be unreportable — leaving a client unable to say which property drives which keyword.
            sb.Append(",\"attributes\":[");
            var attributes = shader.GetPropertyAttributes(index);
            for (var i = 0; i < attributes.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{RestResponse.EscapeJson(attributes[i])}\"");
            }
            sb.Append("]}");
        }

        /// <summary>
        /// The value a new material gets, spelled the way <c>PATCH /api/assets/materials</c> reads
        /// it, so a client can tell an untouched property from a deliberate one.
        /// </summary>
        private static void AppendDefaultValue(StringBuilder sb, Shader shader, int index, ShaderPropertyType type)
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
                    sb.Append(Int(shader.GetPropertyDefaultIntValue(index)));
                    break;
                case ShaderPropertyType.Texture:
                    // A built-in texture name — "white", "bump", "gray" — rather than an object
                    // reference, because that is what the declaration carries and no asset exists
                    // to point at. null means the declaration named none.
                    sb.Append(RestResponse.FormatNullableString(
                        NullIfEmpty(shader.GetPropertyTextureDefaultName(index))));
                    break;
                default:
                    sb.Append("null");
                    break;
            }
        }

        /// <summary>
        /// The subshaders Unity compiled, and which of them it selected.
        /// </summary>
        /// <remarks>
        /// <c>activeSubshaderIndex</c> is the field that earns this section: a <c>.shader</c> file
        /// lists its subshaders, but which one survives the current render pipeline and platform is
        /// decided during import and appears nowhere on disk. It is reported for a shader carrying
        /// errors as readily as for a clean one, because a shader with a failing subshader and a
        /// working one is still a shader Unity draws with — see <see cref="AppendStructure"/> for
        /// the case where none of this describes the client's shader at all.
        /// </remarks>
        private static void AppendSubshaders(StringBuilder sb, Shader shader)
        {
            var data = ShaderUtil.GetShaderData(shader);
            sb.Append($",\"activeSubshaderIndex\":{Int(data.ActiveSubshaderIndex)},");

            sb.Append("\"subshaders\":[");
            for (var i = 0; i < data.SubshaderCount; i++)
            {
                if (i > 0) sb.Append(",");
                var subshader = data.GetSubshader(i);

                sb.Append($"{{\"levelOfDetail\":{Int(subshader.LevelOfDetail)},\"passes\":[");
                for (var p = 0; p < subshader.PassCount; p++)
                {
                    if (p > 0) sb.Append(",");
                    var pass = subshader.GetPass(p);

                    // A pass the shader did not name reports null rather than "", the same way
                    // every other absent string in this response does.
                    sb.Append($"{{\"name\":{RestResponse.FormatNullableString(NullIfEmpty(pass.Name))},");

                    // The tag that decides when a scriptable render pipeline draws the pass, and
                    // the one a client adding a pass to a URP or HDRP shader has to get right.
                    var lightMode = pass.FindTagValue(new ShaderTagId("LightMode"));
                    sb.Append($"\"lightMode\":{RestResponse.FormatNullableString(NullIfEmpty(lightMode.name))},");
                    sb.Append($"\"isGrabPass\":{RestResponse.FormatBool(pass.IsGrabPass)}}}");
                }
                sb.Append("]}");
            }
            sb.Append("]");
        }

        /// <summary>
        /// Reports "absent" as JSON null rather than as an empty string. Unity spells absence both
        /// ways across this surface — an unnamed pass, a message with no file, a shader Unity
        /// rejected — and a client should not have to test for both.
        /// </summary>
        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>
        /// The graphics API a compiler message came from, or null when the message has none.
        /// </summary>
        /// <remarks>
        /// A ShaderLab parse error happens before any graphics API is involved, and Unity reports
        /// its platform as an undefined enum value: measured on 6000.0.80f1, <c>ToString</c> on it
        /// yields <c>"-1"</c>. Emitting that would put a number in a field documented as an API
        /// name, which a client switching on the string cannot make sense of.
        /// </remarks>
        private static string PlatformName(UnityEditor.Rendering.ShaderCompilerPlatform platform)
            => System.Enum.IsDefined(typeof(UnityEditor.Rendering.ShaderCompilerPlatform), platform)
                ? platform.ToString()
                : null;

        /// <summary>
        /// Formats an integer for JSON. Invariant because the current culture decides the negative
        /// sign, and this response carries negative integers as a matter of course — a shader with
        /// no LOD cap reports <c>maximumLOD</c> as -1.
        /// </summary>
        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
