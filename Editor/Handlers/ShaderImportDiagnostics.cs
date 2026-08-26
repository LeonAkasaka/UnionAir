using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// The diagnostics the asset importer recorded for a shader asset, which are a different set
    /// from the ones the shader compiler recorded.
    /// </summary>
    /// <remarks>
    /// <c>ShaderUtil.GetShaderMessages</c> reports what the shader compiler said about the shader
    /// Unity ended up with. It cannot report that Unity ended up with the wrong shader, and for a
    /// Shader Graph that is the failure that actually happens. Measured on 6000.0.80f1 with Shader
    /// Graph 17.0.4: a <c>.shadergraph</c> the importer could not build is replaced by
    /// <c>ShaderGraphImporter</c>'s own error shader, renamed to the name the graph would have had,
    /// and that substitute compiles cleanly. The read then answers <c>hasError</c> false,
    /// <c>messages</c> empty, one unnamed pass and no properties — a clean report of a shader that
    /// does not exist, and <see cref="ShaderProvenance.WasNotRead"/> does not catch it, because the
    /// name is not empty and there is no compiler error to pair it with.
    ///
    /// <c>AssetImporter.GetImportLog</c> is the only channel that can carry such a failure, and it
    /// is a supported public API on the declared floor: it and <c>ImportLog.ImportLogEntry</c>'s
    /// <c>message</c>, <c>flags</c>, <c>file</c> and <c>line</c> read identically out of
    /// 2022.3.62f2 and 6000.0.80f1. The alternative was parsing <c>GET /api/editor/logs</c>, where
    /// an import failure arrives as prose with an asset path glued to the front.
    ///
    /// "Can carry" rather than "does carry", and the difference is one the reference states: an
    /// import that fails outright writes its exception here, but the substitute case above does
    /// not — measured on 6000.0.80f1, a graph that parses and cannot be built writes to neither log
    /// and not even to the Console, and a target that does not resolve reaches only the Console,
    /// because Shader Graph writes that one with <c>Debug.LogError</c> instead of through the
    /// import context. A clean <c>hasImportError</c> is not proof of a clean import, and
    /// <c>Documentation~/api/assets.md</c> carries the boundary as a table.
    ///
    /// These are reported beside the compiler's messages rather than merged into them.
    /// <c>hasError</c> and <c>messages</c> keep meaning what the reference already says they mean,
    /// and an importer message has no <c>platform</c> to report, because an importer does not run
    /// per graphics API.
    /// </remarks>
    internal static class ShaderImportDiagnostics
    {
        /// <summary>
        /// Appends <c>hasImportError</c>, <c>hasImportWarnings</c> and <c>importMessages</c>,
        /// with a trailing comma.
        /// </summary>
        /// <returns>
        /// Whether the importer recorded an error, so that a caller needing the answer before this
        /// text — the error response writes <c>error</c> first — does not have to read the log a
        /// second time to get it. Two reads would also be two answers: an import landing between
        /// them would let the message and the entries below it disagree.
        /// </returns>
        /// <remarks>
        /// All three are <c>null</c> when there is no importer to ask rather than when the importer
        /// had nothing to say, and the two are not the same answer. A shader Unity built into the
        /// editor is reached through the shared built-in resource container, which no importer in
        /// this project owns; an empty log on an asset that does have an importer means the import
        /// was clean, and that is <c>[]</c>.
        ///
        /// <c>file</c> and <c>line</c> are reported as Unity gives them and are not necessarily a
        /// location in the project. Measured on 6000.0.80f1, an entry written by a native importer
        /// pointed at Unity's own C++ source under
        /// <c>Editor\Src\AssetPipeline</c>, a path on the machine that built the editor. The
        /// reference says so, because <c>messages[].file</c> beside it does name a file the client
        /// can open.
        /// </remarks>
        internal static bool Append(StringBuilder sb, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetImporter.GetAtPath(assetPath) == null)
            {
                sb.Append("\"hasImportError\":null,\"hasImportWarnings\":null,\"importMessages\":null,");
                return false;
            }

            var entries = Entries(assetPath);

            var hasError = false;
            var hasWarnings = false;
            for (var i = 0; i < entries.Length; i++)
            {
                if (IsError(entries[i].flags)) hasError = true;
                else if (IsWarning(entries[i].flags)) hasWarnings = true;
            }

            sb.Append($"\"hasImportError\":{RestResponse.FormatBool(hasError)},");
            sb.Append($"\"hasImportWarnings\":{RestResponse.FormatBool(hasWarnings)},");

            sb.Append("\"importMessages\":[");
            for (var i = 0; i < entries.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var entry = entries[i];

                sb.Append("{");
                sb.Append($"\"severity\":\"{Severity(entry.flags)}\",");
                sb.Append($"\"message\":\"{RestResponse.EscapeJson(entry.message)}\",");
                sb.Append($"\"file\":{RestResponse.FormatNullableString(NullIfEmpty(entry.file))},");
                sb.Append($"\"line\":{entry.line.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                sb.Append("}");
            }
            sb.Append("],");

            return hasError;
        }

        private static ImportLog.ImportLogEntry[] Entries(string assetPath)
        {
            var log = AssetImporter.GetImportLog(assetPath);
            return log == null || log.logEntries == null
                ? new ImportLog.ImportLogEntry[0]
                : log.logEntries;
        }

        // ImportLogFlags is a bit field, so a message is tested for the bit rather than compared to
        // the enum value. Error wins over Warning when both are set: an entry Unity marked as an
        // error is an error whatever else it also is.
        private static bool IsError(ImportLogFlags flags) => (flags & ImportLogFlags.Error) != 0;

        private static bool IsWarning(ImportLogFlags flags) => (flags & ImportLogFlags.Warning) != 0;

        // The vocabulary messages[] already uses, so a client reading both arrays reads one word
        // for one meaning. "None" is Unity's own name for an entry that is neither.
        private static string Severity(ImportLogFlags flags)
            => IsError(flags) ? "Error"
             : IsWarning(flags) ? "Warning"
             : "None";

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
