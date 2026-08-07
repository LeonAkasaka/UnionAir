using System;
using System.Globalization;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Shell a generated command is quoted for.
    /// </summary>
    /// <remarks>
    /// There is no single form that works everywhere, which is why this is a choice rather than a
    /// detail. Verified against a running server on Windows:
    /// <list type="bullet">
    /// <item>Single quotes reach curl intact in bash-family shells and in PowerShell 7, but
    /// Windows PowerShell 5.1 strips the inner double quotes while re-quoting arguments for a
    /// native executable, so even a plain JSON body arrives unparseable.</item>
    /// <item>Escaping the inner double quotes as <c>\"</c> is what survives that re-quoting, but
    /// PowerShell 7 and bash pass the backslashes through literally and curl then sees invalid
    /// JSON.</item>
    /// <item>A literal single quote is written <c>'\''</c> in bash and <c>''</c> in PowerShell.</item>
    /// </list>
    /// </remarks>
    internal enum CurlShell
    {
        /// <summary>
        /// bash, zsh, Git Bash, WSL, macOS, and Linux. PowerShell 7 accepts this too, except
        /// when the body contains a single quote, which it escapes differently.
        /// </summary>
        Posix,

        /// <summary>Windows PowerShell 5.1, the <c>powershell.exe</c> shipped with Windows.</summary>
        WindowsPowerShell,
    }

    /// <summary>
    /// Turns a captured exchange into the text the request log and its detail window display.
    /// </summary>
    /// <remarks>
    /// Kept apart from the windows that draw it so the layout can be rearranged without touching
    /// any of this, and so the parts worth getting right - the curl command in particular - can
    /// be tested without an EditorWindow.
    /// </remarks>
    internal static class RequestLogFormatter
    {
        /// <summary>
        /// Characters of a body rendered at once.
        /// </summary>
        /// <remarks>
        /// Both IMGUI and UI Toolkit have practical limits on how much text one element will
        /// render, and a very large body is not readable in a window anyway. What is displayed is
        /// capped; the copy and save actions still hand over the whole thing.
        /// </remarks>
        internal const int MaxDisplayChars = 20000;

        /// <summary>Row text for the request log list.</summary>
        internal static string SummaryLine(RequestLogEntry entry)
        {
            if (entry == null) return "";

            var sb = new StringBuilder(96);
            sb.Append(entry.StartedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            sb.Append("  ");
            sb.Append(entry.Completed
                ? entry.StatusCode.ToString(CultureInfo.InvariantCulture)
                : "...");
            sb.Append("  ");
            sb.Append(entry.Method);
            sb.Append(' ');
            sb.Append(entry.Path);
            if (!string.IsNullOrEmpty(entry.Query)) sb.Append(entry.Query);
            if (entry.Completed)
            {
                sb.Append("  ");
                sb.Append(FormatDuration(entry.DurationMs));
            }
            return sb.ToString();
        }

        /// <summary>Request line and headers, without the body.</summary>
        internal static string RequestSummary(RequestLogEntry entry)
        {
            if (entry == null) return "";

            var sb = new StringBuilder(256);
            sb.Append(entry.Method).Append(' ').Append(entry.Path);
            if (!string.IsNullOrEmpty(entry.Query)) sb.Append(entry.Query);
            sb.Append('\n');
            if (!string.IsNullOrEmpty(entry.RequestHeaders))
                sb.Append(entry.RequestHeaders).Append('\n');
            return sb.ToString();
        }

        /// <summary>Status, content type, and duration for the response section header.</summary>
        internal static string ResponseSummary(RequestLogEntry entry)
        {
            if (entry == null) return "";
            if (!entry.Completed) return "In progress";

            var sb = new StringBuilder(128);
            sb.Append(entry.StatusCode.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(entry.ResponseContentType))
                sb.Append("  ").Append(entry.ResponseContentType);
            sb.Append("  ").Append(FormatDuration(entry.DurationMs));
            sb.Append("  ").Append(FormatBytes(entry.ResponseBodyLength));
            return sb.ToString();
        }

        /// <summary>
        /// Body text for the request section, or an explanation of why there is none.
        /// </summary>
        internal static string RequestBodyText(RequestLogEntry entry, out bool clipped)
        {
            clipped = false;
            if (entry == null) return "";

            if (entry.RequestBodyTruncated)
                return "(request body not captured: " + FormatBytes(entry.RequestBodyLength) +
                       " exceeds the " + FormatBytes(RequestLogStore.MaxRequestBodyBytes) + " cap)";

            if (entry.RequestBodyUnreadable)
                return "(request body could not be read)";

            if (string.IsNullOrEmpty(entry.RequestBody))
                return "(no request body)";

            return Clip(entry.RequestBody, out clipped);
        }

        /// <summary>
        /// Body text for the response section, or an explanation of why there is none.
        /// </summary>
        internal static string ResponseBodyText(RequestLogEntry entry, out bool clipped)
        {
            clipped = false;
            if (entry == null) return "";
            if (!entry.Completed) return "(response has not completed)";

            if (entry.ResponseBodyLength == 0)
                return "(no response body)";

            if (!entry.ResponseBodyCaptured)
                return "(" + DescribeBinaryBody(entry) + ")";

            if (entry.ResponseBody == null)
                return "(no response body)";

            var text = Encoding.UTF8.GetString(entry.ResponseBody);
            var body = Clip(text, out clipped);
            if (entry.ResponseBodyTruncated)
                body += "\n\n(truncated at the " +
                        FormatBytes(RequestLogStore.MaxResponseBodyBytes) + " capture cap of " +
                        FormatBytes(entry.ResponseBodyLength) + ")";
            return body;
        }

        /// <summary>Describes a payload that was measured rather than kept.</summary>
        internal static string DescribeBinaryBody(RequestLogEntry entry)
        {
            var type = string.IsNullOrEmpty(entry.ResponseContentType)
                ? "binary"
                : entry.ResponseContentType;
            return type + ", " + FormatBytes(entry.ResponseBodyLength) + ", not captured";
        }

        /// <summary>
        /// Whether an executable command can be produced for this entry.
        /// </summary>
        /// <remarks>
        /// A request whose body was not captured would produce a command that runs but is not the
        /// request that was recorded, which is worse than offering nothing.
        /// </remarks>
        internal static bool CanBuildCurl(RequestLogEntry entry)
            => entry != null && !entry.RequestBodyTruncated;

        /// <summary>
        /// Builds a curl command that reproduces the captured request.
        /// </summary>
        /// <param name="entry">Captured exchange.</param>
        /// <param name="baseUrl">Origin of the running server, such as <c>http://localhost:8765</c>.</param>
        /// <param name="shell">Shell the command will be pasted into. See <see cref="CurlShell"/>.</param>
        /// <remarks>
        /// <c>curl.exe</c> rather than <c>curl</c>: in Windows PowerShell 5.1 the bare name is an
        /// alias for <c>Invoke-WebRequest</c>, so a pasted command would silently run a different
        /// program with different flags. The explicit name resolves correctly in every shell.
        /// <para>
        /// Only <c>Content-Type</c> is emitted. It is required - UnionAir answers <c>415</c>
        /// without it - while the headers a client happened to send are noise, and an
        /// <c>Origin</c> header would make the request fail with <c>403</c>.
        /// </para>
        /// </remarks>
        internal static string BuildCurl(
            RequestLogEntry entry, string baseUrl, CurlShell shell = CurlShell.Posix)
        {
            if (!CanBuildCurl(entry)) return "";

            var hasBody = !string.IsNullOrEmpty(entry.RequestBody);

            var sb = new StringBuilder(256);
            sb.Append("curl.exe");
            if (entry.Method != "GET")
                sb.Append(" -X ").Append(entry.Method);

            sb.Append(' ').Append(Quote(
                (baseUrl == null ? "" : baseUrl.TrimEnd('/')) + entry.Path + entry.Query, shell));

            if (hasBody)
            {
                sb.Append(" -H ").Append(Quote("Content-Type: application/json", shell));
                sb.Append(" -d ").Append(Quote(entry.RequestBody, shell));
            }
            else if (entry.Method == "POST" || entry.Method == "PATCH")
            {
                // Windows HttpListener answers 411 for a POST with neither Content-Length nor
                // Transfer-Encoding, so an empty one has to be framed explicitly.
                sb.Append(" -H ").Append(Quote("Content-Length: 0", shell));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Wraps a value in single quotes, escaped for the target shell.
        /// </summary>
        /// <remarks>
        /// Neither form is universal; see <see cref="CurlShell"/> for what was measured.
        /// </remarks>
        internal static string Quote(string value, CurlShell shell = CurlShell.Posix)
        {
            if (value == null) return "''";

            if (shell == CurlShell.WindowsPowerShell)
                // Windows PowerShell rebuilds the command line for a native executable and drops
                // bare double quotes on the way; escaping them is what survives that pass. A
                // literal single quote is doubled, the way PowerShell escapes one.
                return "'" + value.Replace("'", "''").Replace("\"", "\\\"") + "'";

            // A single quote cannot appear inside a single-quoted string, so the string is closed,
            // an escaped quote is emitted, and the string is reopened.
            return "'" + value.Replace("'", "'\\''") + "'";
        }

        /// <summary>Formats a byte count for display.</summary>
        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>Formats a duration for display.</summary>
        internal static string FormatDuration(double milliseconds)
        {
            if (milliseconds < 1000)
                return milliseconds.ToString("0.#", CultureInfo.InvariantCulture) + " ms";
            return (milliseconds / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + " s";
        }

        /// <summary>Suggests a file name for a saved body.</summary>
        internal static string SuggestFileName(RequestLogEntry entry, bool response)
        {
            if (entry == null) return "body.txt";

            var name = (entry.Path ?? "").Trim('/').Replace('/', '-');
            if (name.Length == 0) name = "request";
            return name + "-" + (response ? "response" : "request") + Extension(entry, response);
        }

        private static string Extension(RequestLogEntry entry, bool response)
        {
            if (!response) return ".json";
            var type = entry.ResponseContentType ?? "";
            if (type.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0) return ".json";
            if (type.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0) return ".xml";
            return ".txt";
        }

        private static string Clip(string text, out bool clipped)
        {
            clipped = text.Length > MaxDisplayChars;
            return clipped ? text.Substring(0, MaxDisplayChars) : text;
        }
    }
}
