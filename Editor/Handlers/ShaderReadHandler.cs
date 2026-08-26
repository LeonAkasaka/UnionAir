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
    /// exactly as it did. <c>hasError</c> and <c>messages</c> are the diagnostics Unity has cached,
    /// and once a reimport has settled they are that answer, which makes an
    /// edit-import-diagnose cycle terminate the way the C# one already does through
    /// <c>POST /api/compile</c>. They are not only that answer, and the remark below the property
    /// set says what else gets into them.
    ///
    /// The property set is the second half. <c>GET /api/assets/materials/{guid}</c> reports the
    /// properties of a shader a material already uses; a client choosing a shader for a material it
    /// has not created yet had nowhere to ask what that shader declares, what a property defaults
    /// to, or which keywords are valid on it.
    ///
    /// A <c>.shadergraph</c> is read by this endpoint like any other shader asset, and none of it
    /// is a special case: Unity's importer generates a shader and makes it the asset's main object,
    /// so <c>AssetDatabase.LoadAssetAtPath&lt;Shader&gt;</c> returns it. Measured on 6000.0.80f1
    /// with Shader Graph 17.0.4 against a graph carrying one blackboard property, the read reports
    /// that property with its default alongside the ones Shader Graph generates, the URP
    /// subshader's ten passes with their <c>lightMode</c>, and a 60-keyword space. The generated
    /// name is the part a client cannot get from the file: the graph stores its category and Unity
    /// joins the file name to it, and that name is what a material carries and what
    /// <c>POST /api/assets/materials</c> takes.
    ///
    /// Diagnostics are the set Unity currently has cached, not a fresh compile. A reimport clears
    /// it and refills it with what that import compiled, so after editing the file, reimport
    /// — <c>POST /api/assets/reimport</c> or <c>POST /api/editor/refresh</c> — and read again.
    /// Those two calls are the whole loop; there is no validation endpoint beside them, because one
    /// would report the state at the moment it ran and that is not a verdict on the shader.
    ///
    /// Unity compiles variants on demand, so the set grows without an import. Measured on
    /// 6000.0.80f1, an error inside a <c>multi_compile</c> keyword's branch is absent right after
    /// the reimport and present once the Scene View renders a material enabling that keyword, with
    /// no reimport in between; reimporting clears it again. A clean <c>hasError</c> therefore means
    /// nothing has compiled a broken variant yet, and an error appearing in a later read with no
    /// edit is a variant reaching the compiler for the first time rather than a fault. They come from two places, and both are
    /// reported: <c>hasError</c> and <c>messages</c> are the shader compiler's, and
    /// <c>hasImportError</c> and <c>importMessages</c> are the asset importer's. A generated shader
    /// is why the second set is not redundant — see <see cref="ShaderImportDiagnostics"/>.
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
                SendNoShader(response, assetPath);
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

        /// <summary>
        /// The answer when the asset produced no Shader: either it is not a shader asset, or it is
        /// one whose import failed outright.
        /// </summary>
        /// <remarks>
        /// The two are worth telling apart. Measured on 6000.0.80f1 with Shader Graph 17.0.4, a
        /// <c>.shadergraph</c> whose JSON does not parse produces no shader object at all and types
        /// as a <c>DefaultAsset</c>, so the read reached this path and said "Asset is not a Shader"
        /// — true of the object Unity holds, and misleading about the asset, which is a shader
        /// asset that failed to import. A client editing a graph then had no diagnostic and no next
        /// step, which is the loop this endpoint exists to close.
        ///
        /// The importer's log is what distinguishes them, so the error carries it. The status stays
        /// 400: there is still no shader to report, and every structural field would be a guess.
        ///
        /// An import error is not on its own enough to call the asset a shader, because every
        /// importer writes to the same log. The extension has to agree, and without that check a
        /// file named <c>NotAnImage.png</c> holding plain text — rejected by the texture importer,
        /// so typed as a <c>DefaultAsset</c> exactly like a broken graph — was answered
        /// "Shader asset failed to import", handing a client that passed the wrong GUID a texture
        /// importer's error to act on. The log is reported either way; only the sentence changes.
        /// </remarks>
        private static void SendNoShader(UnionAirResponse response, string assetPath)
        {
            // Written first because the answer decides the wording, and reading the log once keeps
            // the wording and the entries describing the same import.
            var diagnostics = new StringBuilder();
            var importFailed = ShaderImportDiagnostics.Append(diagnostics, assetPath);

            var message = importFailed && IsShaderSource(assetPath)
                ? $"Shader asset failed to import: {assetPath}"
                : $"Asset is not a Shader: {assetPath}";

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"error\":\"{RestResponse.EscapeJson(message)}\",");
            sb.Append(diagnostics.ToString());
            sb.Length -= 1; // the shared appender leaves a trailing comma for the fields that follow it
            sb.Append("}");

            RestResponse.Send(response, sb.ToString(), 400);
        }

        /// <summary>
        /// Whether the asset at <paramref name="assetPath"/> is one whose import produces a Shader.
        /// </summary>
        /// <remarks>
        /// The extension is the test because the asset itself cannot be: an import that failed
        /// outright leaves no object to ask, which is the whole reason this question is being asked
        /// here.
        ///
        /// <c>.shadersubgraph</c> is deliberately absent. A Sub Graph's main asset is a
        /// <c>SubGraphAsset</c> and never a <c>Shader</c>, so one that imports cleanly is already
        /// answered "Asset is not a Shader"; including it here would have a Sub Graph change its
        /// story depending on whether it failed. <c>.compute</c> and <c>.raytrace</c> are absent for
        /// the same reason.
        /// </remarks>
        private static bool IsShaderSource(string assetPath)
        {
            var extension = System.IO.Path.GetExtension(assetPath);
            return string.Equals(extension, ".shader", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".shadergraph", System.StringComparison.OrdinalIgnoreCase);
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

            // Unity's own capability signal: whether this shader can run on the current GPU, with
            // fallbacks taken into account. It says nothing about whether the import succeeded and
            // nothing about whose declaration the structure below describes — measured on
            // 6000.0.80f1, a shader with no errors at all reports false when its only subshader is
            // excluded for the current renderer, and the same shader with a Fallback reports true.
            sb.Append($"\"isSupported\":{RestResponse.FormatBool(shader.isSupported)},");

            // Before the structure, because when the structure is absent this is the answer.
            AppendDiagnostics(sb, shader);
            ShaderImportDiagnostics.Append(sb, assetPath);
            AppendStructure(sb, shader);

            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>
        /// Everything that describes the shader itself rather than its import, or nulls when Unity
        /// read nothing from the file.
        /// </summary>
        /// <remarks>
        /// The only case this suppresses is the one where there is nothing to report. Measured on
        /// 6000.0.80f1 against a shader with one property and one pass that Unity rejected with a
        /// ShaderLab parse error: <c>name</c> read <c>""</c>, <c>properties</c> read empty,
        /// <c>keywords</c> listed four stereo keywords the shader never declared, and
        /// <c>passCount</c> read 3 against the one pass in the file. The parse failed before the
        /// name was read, so none of that came from the file, and a client building a material from
        /// <c>properties</c> would build one with no properties and never learn why. An empty name
        /// on a shader carrying an error is what that state looks like, and it is the whole test.
        ///
        /// It is deliberately not <c>isSupported</c>, which is a capability signal and not a
        /// provenance one. Measured on 6000.0.80f1: a shader with no errors whose only subshader is
        /// excluded for the current renderer reports <c>isSupported</c> false while its properties
        /// and passes are perfectly readable, so suppressing on it discards a correct declaration;
        /// and the same shader given a <c>Fallback</c> reports <c>isSupported</c> true while its
        /// subshaders become the fallback's, so a true value does not establish provenance either.
        ///
        /// It is also not <c>hasError</c>. A shader can carry errors and still be the shader Unity
        /// draws with: measured against a shader whose first subshader fails to compile and whose
        /// second does not, <c>hasError</c> is true, <c>isSupported</c> is true, and Unity selects
        /// the working subshader.
        /// </remarks>
        private static void AppendStructure(StringBuilder sb, Shader shader)
        {
            if (ShaderProvenance.WasNotRead(shader))
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
        /// The shader's effective local keyword space: every keyword valid on it, enabled or not.
        /// </summary>
        /// <remarks>
        /// <c>GET /api/assets/materials/{guid}</c> reports the keywords a material has enabled and
        /// cannot report the ones it could enable, because a material only stores the set that is
        /// on. The valid set belongs to the shader.
        ///
        /// "Effective" rather than "declared", and the distinction is not pedantic: the space also
        /// carries keywords from dependencies reached through <c>Fallback</c> and <c>UsePass</c>,
        /// and keywords Unity adds by itself. Measured on 6000.0.80f1, a shader whose source
        /// declares exactly one keyword through <c>multi_compile</c> reports five — the four extra
        /// being <c>STEREO_INSTANCING_ON</c>, <c>UNITY_SINGLE_PASS_STEREO</c>,
        /// <c>STEREO_MULTIVIEW_ON</c> and <c>STEREO_CUBEMAP_RENDER_ON</c>, none of which appear in
        /// the file. A client must not read this as the shader's own declarations, and the
        /// reference says so.
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
            ShaderPropertyDefaultJson.Append(sb, shader, index, type);

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
        /// The subshaders Unity compiled, and which of them it selected.
        /// </summary>
        /// <remarks>
        /// <c>activeSubshaderIndex</c> is the field that earns this section: a <c>.shader</c> file
        /// lists its subshaders, but which one survives the current render pipeline and platform is
        /// decided during import and appears nowhere on disk. It is reported for a shader carrying
        /// errors as readily as for a clean one, because a shader with a failing subshader and a
        /// working one is still a shader Unity draws with.
        ///
        /// This is what Unity compiled, which is not always what the file declares. When a shader's
        /// own subshaders are unusable and it names a <c>Fallback</c>, the fallback's subshaders are
        /// what appear here: measured on 6000.0.80f1, a shader declaring one subshader with one pass
        /// named <c>ExcludedPass</c>, excluded for the current renderer and falling back to
        /// <c>Diffuse</c>, reports two subshaders and four passes — <c>FORWARD</c>, <c>FORWARD</c>,
        /// <c>DEFERRED</c>, <c>Meta</c> — which is exactly what reading <c>Legacy Shaders/Diffuse</c>
        /// reports. The reference says so rather than the endpoint pretending to detect it;
        /// <c>properties</c> in that same response stayed the client's.
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

                sb.Append($"{{\"levelOfDetail\":{Int(subshader.LevelOfDetail)},");

                // The tag that decides which render pipeline a subshader belongs to, and the first
                // thing a client has to know when picking a shader for a material: whether this is
                // a URP shader, an HDRP one, or a built-in one. A built-in-pipeline subshader
                // declares no such tag and reports null, the same way an untagged pass reports a
                // null lightMode.
                //
                // A named field per tag rather than a tags map, because Unity has no way to
                // enumerate the tags a subshader carries -- they are looked up by name -- so a map
                // could only ever hold the keys this handler thought to ask for, while looking
                // like the whole set.
                var renderPipeline = subshader.FindTagValue(new ShaderTagId("RenderPipeline"));
                sb.Append($"\"renderPipeline\":{RestResponse.FormatNullableString(NullIfEmpty(renderPipeline.name))},");

                sb.Append("\"passes\":[");
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
