using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles the material-to-shader compatibility read:
    ///   GET /api/assets/materials/{guid}/shader-compatibility
    /// </summary>
    /// <remarks>
    /// A material and its shader can disagree, and neither existing read says so.
    /// <c>GET /api/assets/materials/{guid}</c> reports the properties the shader currently declares
    /// with the material's values, and <c>GET /api/assets/shaders/{guid}</c> reports what the
    /// shader declares. Both walk the shader's current declarations, so a value the material is
    /// holding that those declarations cannot reach appears in neither — Unity keeps it in the
    /// <c>.mat</c> file and hides it, and a renamed or retyped property therefore looks like a
    /// lost value with no explanation.
    ///
    /// That is the question after editing a shader, and it is about a pair, which is why it is its
    /// own endpoint rather than a section on either read: neither read owns the pair, and a client
    /// that never asks should not pay for the comparison.
    ///
    /// It reports and does not act. Dropping a stale property is a write to the material with a
    /// real consequence — the value it discards is the one a client may be trying to recover — so
    /// it does not belong behind a read.
    /// </remarks>
    internal class MaterialShaderCompatibilityHandler
    {
        /// <summary>
        /// The maps Unity serializes a material's values into, and the only place a value survives
        /// for a property the shader no longer declares.
        /// </summary>
        /// <remarks>
        /// Measured on 6000.0.80f1 by creating a material and looking up every declared property:
        /// <c>Texture</c> lands in <c>m_TexEnvs</c>, <c>Int</c> in <c>m_Ints</c>, <c>Float</c> and
        /// <c>Range</c> both in <c>m_Floats</c>, and <c>Color</c> and <c>Vector</c> both in
        /// <c>m_Colors</c>. So the storage does not preserve the declared type, and for a stale
        /// property the declared type is unrecoverable anyway — the declaration is gone. The
        /// response says which map held the value and does not guess beyond it.
        ///
        /// <c>m_Ints</c> is looked up like the others and skipped when absent, because it is a
        /// serialization detail rather than an API and a project on the 2022.3 floor is not
        /// promised to have it.
        /// </remarks>
        private static readonly string[] StorageMaps = { "m_TexEnvs", "m_Ints", "m_Floats", "m_Colors" };

        /// <summary>The storage name reported for each map, in the same order.</summary>
        private static readonly string[] StorageNames = { "Texture", "Int", "Float", "Color" };

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
            AppendShader(sb, shader);

            var reason = Refusal(shader);
            if (reason != null)
            {
                sb.Append("\"comparable\":false,");
                sb.Append($"\"reason\":\"{reason}\",");
                sb.Append("\"staleProperties\":null,\"unsetProperties\":null,\"invalidKeywords\":null");
                sb.Append("}");
                RestResponse.Send(response, sb.ToString());
                return;
            }

            sb.Append("\"comparable\":true,\"reason\":null,");

            var declared = DeclaredTypes(shader);
            var stored = new HashSet<string>();
            AppendStaleProperties(sb, mat, declared, stored);
            AppendUnsetProperties(sb, shader, stored);
            AppendInvalidKeywords(sb, mat, shader);

            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>
        /// The shader the comparison is against, identified the way the rest of the package
        /// identifies an asset, so a client can go and read it.
        /// </summary>
        /// <remarks>
        /// Reported even when the comparison is refused, because which shader the material ended up
        /// on is most of the answer in that case. A shader Unity built into the editor reports the
        /// shared built-in resource container as its path, exactly as
        /// <c>GET /api/assets/shaders/{guid}</c> does and for the same reason.
        /// </remarks>
        private static void AppendShader(StringBuilder sb, Shader shader)
        {
            if (shader == null)
            {
                sb.Append("\"shader\":null,");
                return;
            }

            var shaderPath = AssetDatabase.GetAssetPath(shader);
            sb.Append("\"shader\":{");
            sb.Append($"\"name\":{RestResponse.FormatNullableString(NullIfEmpty(shader.name))},");
            sb.Append($"\"guid\":{RestResponse.FormatNullableString(NullIfEmpty(AssetDatabase.AssetPathToGUID(shaderPath)))},");
            sb.Append($"\"assetPath\":{RestResponse.FormatNullableString(NullIfEmpty(shaderPath))}");
            sb.Append("},");
        }

        /// <summary>
        /// Why the pair cannot be compared, or null when it can.
        /// </summary>
        /// <remarks>
        /// Both refusals exist because answering would be worse than refusing. A shader Unity
        /// substituted declares no properties, so every value the material carries would be
        /// reported stale — the exact opposite of the truth, and indistinguishable from a real
        /// answer. The signal for each state is measured and lives in
        /// <see cref="ShaderProvenance"/>, which the shader read uses for the same purpose.
        /// </remarks>
        private static string Refusal(Shader shader)
        {
            if (ShaderProvenance.IsMissing(shader)) return "shaderMissing";
            if (ShaderProvenance.WasNotRead(shader)) return "shaderNotRead";
            return null;
        }

        private static Dictionary<string, ShaderPropertyType> DeclaredTypes(Shader shader)
        {
            var declared = new Dictionary<string, ShaderPropertyType>();
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
                declared[shader.GetPropertyName(i)] = shader.GetPropertyType(i);
            return declared;
        }

        /// <summary>
        /// Whether an entry found in <paramref name="mapIndex"/> is the one the shader's current
        /// declaration of <paramref name="name"/> reads, so the value in it is still reachable.
        /// </summary>
        /// <remarks>
        /// The name being declared is not enough, because a declaration can change type while
        /// keeping its name and the old entry stays behind unreachable. Measured on 6000.0.80f1
        /// against a material holding <c>_X</c> at 7 whose shader was then reimported with
        /// <c>_X</c> redeclared from <c>Float</c> to <c>Color</c>: <c>m_Floats</c> still held
        /// <c>_X</c> at 7 while <c>GetColor("_X")</c> answered the new declaration's default. The
        /// 7 is as lost as a dropped property's value, and testing only the name reports the pair
        /// as agreeing.
        ///
        /// <c>Int</c> accepts <c>m_Floats</c> as well as <c>m_Ints</c>. <c>m_Ints</c> is a
        /// serialization detail rather than an API, a project on the 2022.3 floor is not promised
        /// to have it, and where it is absent an <c>Int</c>'s value has nowhere else to be — so the
        /// tolerance costs a mismatch this check would never have been sure of, and buys not
        /// reporting every <c>Int</c> in such a project as unreachable.
        /// </remarks>
        private static bool StorageReadsDeclaration(
            Dictionary<string, ShaderPropertyType> declared, string name, int mapIndex)
        {
            ShaderPropertyType type;
            if (!declared.TryGetValue(name, out type)) return false;

            switch (type)
            {
                case ShaderPropertyType.Texture: return StorageMaps[mapIndex] == "m_TexEnvs";
                case ShaderPropertyType.Int: return StorageMaps[mapIndex] == "m_Ints"
                                                    || StorageMaps[mapIndex] == "m_Floats";
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return StorageMaps[mapIndex] == "m_Floats";
                default: return StorageMaps[mapIndex] == "m_Colors";
            }
        }

        /// <summary>
        /// The values the material is holding that its shader has no way to read.
        /// </summary>
        /// <remarks>
        /// Two things put a value here, and both leave it as unreachable as the other. The shader
        /// dropped the declaration: measured on 6000.0.80f1 against a material created from a
        /// shader declaring <c>_Extra</c> and then reimported after the declaration was removed,
        /// <c>m_SavedProperties.m_Floats</c> still held <c>_Extra</c> with its value while
        /// <c>HasProperty("_Extra")</c> answered false. Or the declaration changed type and kept
        /// its name, which <see cref="StorageReadsDeclaration"/> records the measurement for.
        ///
        /// Walked through <c>SerializedObject</c> because there is no other way to see either:
        /// every typed getter needs a name the shader declares, and for the type-change case the
        /// getter answers with the new declaration's default rather than admitting the old value
        /// is there.
        ///
        /// <paramref name="stored"/> is filled in on the way past, because the same walk answers
        /// the opposite question and walking twice would be walking the same maps twice.
        /// </remarks>
        private static void AppendStaleProperties(
            StringBuilder sb, Material mat,
            Dictionary<string, ShaderPropertyType> declared, HashSet<string> stored)
        {
            var so = new SerializedObject(mat);
            var first = true;

            sb.Append("\"staleProperties\":[");
            for (var m = 0; m < StorageMaps.Length; m++)
            {
                var arr = so.FindProperty("m_SavedProperties." + StorageMaps[m]);
                if (arr == null || !arr.isArray) continue;

                for (var i = 0; i < arr.arraySize; i++)
                {
                    var entry = arr.GetArrayElementAtIndex(i);
                    var key = entry.FindPropertyRelative("first");
                    if (key == null) continue;

                    var name = key.stringValue;
                    if (string.IsNullOrEmpty(name)) continue;

                    // Only an entry the current declaration reads counts as this property having
                    // a value. An entry left behind by a type change does not, so a property whose
                    // every entry is unreachable is reported as unset as well as stale, which
                    // together is the whole truth about it.
                    if (StorageReadsDeclaration(declared, name, m))
                    {
                        stored.Add(name);
                        continue;
                    }

                    if (!first) sb.Append(",");
                    first = false;

                    sb.Append("{");
                    sb.Append($"\"name\":\"{RestResponse.EscapeJson(name)}\",");
                    sb.Append($"\"storage\":\"{StorageNames[m]}\",");
                    sb.Append("\"value\":");
                    AppendStoredValue(sb, entry.FindPropertyRelative("second"), StorageNames[m]);
                    sb.Append("}");
                }
            }
            sb.Append("],");
        }

        /// <summary>
        /// The value the material is holding on to, so the report says what would be lost rather
        /// than only that something would be.
        /// </summary>
        private static void AppendStoredValue(StringBuilder sb, SerializedProperty value, string storage)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (storage)
            {
                case "Texture":
                {
                    // The whole entry, because a texture property stores a scale and an offset
                    // beside the reference and all three are lost together.
                    sb.Append("{\"texture\":");
                    SerializedPropertySerializer.AppendObjectReferenceJson(
                        sb, Texture(value), false);
                    AppendVector2(sb, ",\"scale\":", value.FindPropertyRelative("m_Scale"));
                    AppendVector2(sb, ",\"offset\":", value.FindPropertyRelative("m_Offset"));
                    sb.Append("}");
                    break;
                }
                case "Int":
                    sb.Append(value.intValue.ToString(CultureInfo.InvariantCulture));
                    break;
                case "Float":
                    sb.Append(RestResponse.FormatFloat(value.floatValue));
                    break;
                default:
                {
                    // Spelled as a colour because that is the map it was found in. A stale Vector
                    // is stored here too and is not distinguishable from a colour once the
                    // declaration naming it a Vector is gone.
                    var c = value.colorValue;
                    sb.Append($"{{\"r\":{RestResponse.FormatFloat(c.r)},\"g\":{RestResponse.FormatFloat(c.g)},\"b\":{RestResponse.FormatFloat(c.b)},\"a\":{RestResponse.FormatFloat(c.a)}}}");
                    break;
                }
            }
        }

        private static UnityEngine.Object Texture(SerializedProperty value)
        {
            var texture = value.FindPropertyRelative("m_Texture");
            return texture == null ? null : texture.objectReferenceValue;
        }

        private static void AppendVector2(StringBuilder sb, string key, SerializedProperty value)
        {
            sb.Append(key);
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            var v = value.vector2Value;
            sb.Append($"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)}}}");
        }

        /// <summary>
        /// The properties the shader declares that the material has no serialized value for.
        /// </summary>
        /// <remarks>
        /// This describes the material as it is serialized at the moment of the call, and Unity
        /// writes the entry for a newly reachable property lazily. Measured on 6000.0.80f1 across
        /// two calls against the same unmodified material after its shader redeclared <c>_X</c>
        /// from <c>Float</c> to <c>Color</c>: the first reported <c>_X</c> here as well as in
        /// <c>staleProperties</c>, because no <c>Color</c> entry existed yet, and the second
        /// reported it only as stale. Both are accurate about when they were asked, and the
        /// reference tells a client to read this list that way.
        ///
        /// Unity writes an entry for every property the shader declares when the material is
        /// created, so this is empty for a material and shader that have not drifted. It fills when
        /// the shader gains a property afterwards, which is the case worth knowing about: measured
        /// on 6000.0.80f1, a property added to the shader after the material existed appeared in no
        /// storage map while the older ones were all present.
        ///
        /// Such a property is not broken — the material uses the declared default, which is
        /// reported here so the client can see what that is without a second call — but it did not
        /// come from anyone's decision, and telling it apart from a value someone chose is not
        /// possible from the material read alone.
        /// </remarks>
        private static void AppendUnsetProperties(StringBuilder sb, Shader shader, HashSet<string> stored)
        {
            sb.Append("\"unsetProperties\":[");
            var count = shader.GetPropertyCount();
            var first = true;
            for (var i = 0; i < count; i++)
            {
                var name = shader.GetPropertyName(i);
                if (stored.Contains(name)) continue;

                if (!first) sb.Append(",");
                first = false;

                var type = shader.GetPropertyType(i);
                sb.Append("{");
                sb.Append($"\"name\":\"{RestResponse.EscapeJson(name)}\",");
                sb.Append($"\"type\":\"{type}\",");
                sb.Append("\"defaultValue\":");
                ShaderPropertyDefaultJson.Append(sb, shader, i, type);
                sb.Append("}");
            }
            sb.Append("],");
        }

        /// <summary>
        /// The keywords the material has enabled that the shader has no room for.
        /// </summary>
        /// <remarks>
        /// Checked against the shader's <b>effective local keyword space</b>, which is what Unity
        /// exposes and what <c>GET /api/assets/shaders/{guid}</c> reports under <c>keywords</c>.
        /// That space is wider than the source: it also carries keywords reached through
        /// <c>Fallback</c> and <c>UsePass</c> dependencies and keywords Unity adds by itself, so a
        /// keyword absent from it is genuinely unusable, while a keyword present in it is not
        /// evidence that the shader's author declared it. "Did the author declare this" is not
        /// answerable through public API at all, and this endpoint does not pretend to answer it.
        ///
        /// Measured on 6000.0.80f1: a material with <c>UNIONAIR_PROBE_ON</c> enabled, whose shader
        /// was then reimported with that keyword renamed, still reported the old keyword in
        /// <c>Material.shaderKeywords</c> while the shader's space carried only the new one. Unity
        /// does not prune the material, which is why nothing else reports this.
        /// </remarks>
        private static void AppendInvalidKeywords(StringBuilder sb, Material mat, Shader shader)
        {
            var space = new HashSet<string>();
            var keywords = shader.keywordSpace.keywords;
            for (var i = 0; i < keywords.Length; i++)
                space.Add(keywords[i].name);

            sb.Append("\"invalidKeywords\":[");
            var enabled = mat.shaderKeywords;
            var first = true;
            for (var i = 0; i < enabled.Length; i++)
            {
                if (space.Contains(enabled[i])) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{RestResponse.EscapeJson(enabled[i])}\"");
            }
            sb.Append("]");
        }

        /// <summary>
        /// Reports "absent" as JSON null rather than as an empty string, the same way the shader
        /// read does.
        /// </summary>
        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
