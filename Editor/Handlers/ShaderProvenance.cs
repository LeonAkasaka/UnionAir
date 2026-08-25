using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The two states in which a Shader object describes something other than the shader a client
    /// asked about, and so cannot be reported or compared against.
    /// </summary>
    /// <remarks>
    /// Shared by <c>GET /api/assets/shaders/{guid}</c>, which suppresses its structural fields, and
    /// by <c>GET /api/assets/materials/{guid}/shader-compatibility</c>, which refuses to compare.
    /// They are the same question asked for two reasons, and one answer keeps them from drifting.
    /// </remarks>
    internal static class ShaderProvenance
    {
        /// <summary>
        /// The name Unity gives the shader it substitutes when a material's own shader cannot be
        /// resolved. Matching on it is matching on a string, and this is the only way the state is
        /// reachable: the substitution is not reported by a flag, and the property of a material
        /// that would name the missing shader is gone once Unity has replaced it.
        /// </summary>
        private const string InternalErrorShaderName = "Hidden/InternalErrorShader";

        /// <summary>
        /// Whether Unity got nothing out of the file: the ShaderLab parse failed before even the
        /// shader's name was read, so every structural field describes an internal substitute.
        /// </summary>
        /// <remarks>
        /// A shader that parses always has a name — ShaderLab requires one — so an empty name on a
        /// shader carrying an error identifies this state and nothing else. The error is required
        /// alongside it so that the check cannot be met by any shader Unity accepted.
        /// </remarks>
        internal static bool WasNotRead(Shader shader)
            => shader != null
               && ShaderUtil.ShaderHasError(shader)
               && string.IsNullOrEmpty(shader.name);

        /// <summary>
        /// Whether a material has no shader to speak of, because Unity replaced the one it named
        /// with the internal error shader.
        /// </summary>
        /// <remarks>
        /// Measured on 6000.0.80f1, both ways a material reaches this state read identically —
        /// a material whose <c>shader</c> was set to null, and a material asset whose shader asset
        /// was deleted out from under it: <c>shader.name</c> is
        /// <c>Hidden/InternalErrorShader</c>, <c>ShaderUtil.ShaderHasError</c> is <c>false</c>,
        /// <c>GetPropertyCount</c> is <c>0</c>, and <c>isSupported</c> is <c>false</c>.
        ///
        /// So <see cref="WasNotRead"/> does not cover it: there is no error, and the name is not
        /// empty. It has to be tested for separately, and the property count cannot be the test —
        /// a shader is allowed to declare no properties at all.
        /// </remarks>
        internal static bool IsMissing(Shader shader)
            => shader == null || shader.name == InternalErrorShaderName;
    }
}
