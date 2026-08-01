using System;
using System.Text.RegularExpressions;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Pure text helpers for turning Unity compiler output into structured fields.
    /// </summary>
    /// <remarks>
    /// <c>CompilerMessage</c> carries only <c>message</c>, <c>file</c>, <c>line</c>,
    /// <c>column</c>, and <c>type</c>; the diagnostic code exists solely inside the message text.
    /// Severity is always taken from <c>type</c> and never from the text, because Roslyn
    /// localizes the words "error" and "warning" while the code token stays ASCII.
    /// </remarks>
    internal static class CompileMessageParser
    {
        private const int MaxMessageLength = 4000;

        // Matches a diagnostic code immediately followed by a colon: CS0103, UNT0001, IDE0051.
        private static readonly Regex CodePattern = new Regex(
            @"(?<![A-Za-z0-9])(?<code>[A-Za-z]{2,10}[0-9]{3,5})(?=\s*:)",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Extracts the diagnostic code from a compiler message.
        /// </summary>
        /// <param name="message">Raw compiler message text.</param>
        /// <returns>The code, or <c>null</c> when the message carries none.</returns>
        internal static string ExtractCode(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            var match = CodePattern.Match(message);
            return match.Success ? match.Groups["code"].Value : null;
        }

        /// <summary>
        /// Removes the <c>path(line,column): severity CODE:</c> prefix Unity places on messages.
        /// </summary>
        /// <param name="message">Raw compiler message text.</param>
        /// <returns>The human-readable remainder, or the original text when no prefix is present.</returns>
        internal static string StripPrefix(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";

            var match = CodePattern.Match(message);
            if (!match.Success) return message.Trim();

            var colon = message.IndexOf(':', match.Index + match.Length - 1);
            if (colon < 0 || colon + 1 >= message.Length) return message.Trim();

            return message.Substring(colon + 1).Trim();
        }

        /// <summary>
        /// Normalizes a compiler-reported path to a forward-slash project-relative path.
        /// </summary>
        /// <param name="file">Path as reported by the compiler; may be empty or absolute.</param>
        /// <param name="projectRoot">Absolute project root used to re-relativize absolute paths.</param>
        /// <returns>The normalized path, or <c>null</c> when the compiler reported none.</returns>
        /// <remarks>
        /// Build-system errors surfaced through the same channel carry no file, and Windows
        /// compilers may emit backslashes. An agent that cannot map this back to the file it
        /// wrote cannot act on the diagnostic.
        /// </remarks>
        internal static string NormalizePath(string file, string projectRoot)
        {
            if (string.IsNullOrEmpty(file)) return null;

            var normalized = file.Replace('\\', '/').Trim();
            if (normalized.Length == 0) return null;

            if (!string.IsNullOrEmpty(projectRoot))
            {
                var root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
                if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(root.Length);
            }

            return normalized.Length == 0 ? null : normalized;
        }

        /// <summary>
        /// Caps a message so a single pathological diagnostic cannot bloat the persisted record.
        /// </summary>
        /// <param name="value">Message text.</param>
        /// <returns>The text, truncated with an ellipsis when it exceeds the cap.</returns>
        internal static string Cap(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= MaxMessageLength
                ? value
                : value.Substring(0, MaxMessageLength) + "...";
        }

        /// <summary>
        /// Classifies an assembly output directory as an Editor, player, or other build target.
        /// </summary>
        /// <param name="outputDirectory">Directory the compiled assembly was written to.</param>
        /// <returns><c>editor</c>, <c>player</c>, or <c>other</c>.</returns>
        internal static string ClassifyTarget(string outputDirectory)
        {
            if (string.IsNullOrEmpty(outputDirectory)) return "other";

            var normalized = outputDirectory.Replace('\\', '/').TrimEnd('/');
            if (EndsWithPath(normalized, "Library/PlayerScriptAssemblies") ||
                EndsWithPath(normalized, "Library/Bee/PlayerScriptAssemblies"))
                return "player";
            if (EndsWithPath(normalized, "Library/ScriptAssemblies"))
                return "editor";
            return "other";
        }

        private static bool EndsWithPath(string path, string suffix)
        {
            if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;

            var prefixLength = path.Length - suffix.Length;
            return prefixLength == 0 || path[prefixLength - 1] == '/';
        }

        /// <summary>
        /// Whether a compile id is safe to use as a file name and route value.
        /// </summary>
        /// <param name="id">Candidate identifier.</param>
        /// <returns><c>true</c> when the id contains only unreserved characters.</returns>
        internal static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;

            for (var i = 0; i < id.Length; i++)
            {
                var c = id[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                         (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return false;
            }

            return !IsWindowsDeviceName(id);
        }

        private static bool IsWindowsDeviceName(string id)
        {
            var upper = id.ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL")
                return true;
            if (upper.Length != 4) return false;

            var prefix = upper.Substring(0, 3);
            var suffix = upper[3];
            return (prefix == "COM" || prefix == "LPT") && suffix >= '1' && suffix <= '9';
        }
    }
}
