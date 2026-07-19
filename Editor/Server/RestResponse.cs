using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Helper for writing JSON HTTP responses with CORS headers.
    /// </summary>
    public static class RestResponse
    {
        /// <summary>
        /// Writes a JSON response with UTF-8 encoding and CORS headers.
        /// </summary>
        /// <param name="response">HTTP response to write to.</param>
        /// <param name="json">Complete JSON payload to send.</param>
        /// <param name="statusCode">HTTP status code to set before writing the body.</param>
        public static void Send(HttpListenerResponse response, string json, int statusCode = 200)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            AddCorsHeaders(response);

            var bytes = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Writes a binary response with the supplied MIME type and CORS headers.
        /// </summary>
        /// <param name="response">HTTP response to write to.</param>
        /// <param name="data">Binary response body.</param>
        /// <param name="mimeType">Response MIME type, such as <c>image/png</c>.</param>
        public static void SendBinary(HttpListenerResponse response, byte[] data, string mimeType)
        {
            response.StatusCode = 200;
            response.ContentType = mimeType;
            AddCorsHeaders(response);

            response.ContentLength64 = data.Length;
            response.OutputStream.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Streams a completed UnionAir artifact on a background thread without loading it into memory.
        /// </summary>
        /// <param name="context">Request context whose response lifetime will be deferred.</param>
        /// <param name="path">Artifact path below <c>Library/UnionAir</c>.</param>
        /// <param name="mimeType">Response MIME type.</param>
        /// <param name="downloadName">Safe file name advertised through Content-Disposition.</param>
        public static void SendArtifactFile(
            UnionAirRequestContext context,
            string path,
            string mimeType,
            string downloadName)
        {
            var response = context.Response;
            var fullPath = Path.GetFullPath(path);
            var artifactRoot = Path.GetFullPath(Path.Combine("Library", "UnionAir"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(artifactRoot, System.StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                SendNotFound(response, "Artifact is not available.");
                return;
            }

            FileStream stream;
            try
            {
                stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException)
            {
                SendNotFound(response, "Artifact is not available.");
                return;
            }
            catch (System.UnauthorizedAccessException)
            {
                SendNotFound(response, "Artifact is not available.");
                return;
            }

            var contentLength = stream.Length;
            string queueError = null;

            try
            {
                if (ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        response.StatusCode = 200;
                        response.ContentType = mimeType;
                        AddCorsHeaders(response);
                        response.AddHeader("Content-Disposition",
                            $"attachment; filename=\"{EscapeHeaderFileName(downloadName)}\"");
                        response.ContentLength64 = contentLength;
                        using (stream)
                        {
                            var buffer = new byte[64 * 1024];
                            var remaining = contentLength;
                            while (remaining > 0)
                            {
                                var read = stream.Read(buffer, 0, (int)System.Math.Min(buffer.Length, remaining));
                                if (read <= 0) break;
                                response.OutputStream.Write(buffer, 0, read);
                                remaining -= read;
                            }
                        }
                    }
                    catch (System.Exception) { }
                    finally
                    {
                        try { response.Close(); } catch { }
                    }
                }))
                {
                    context.Defer();
                    return;
                }

                queueError = "The .NET thread pool rejected the artifact transfer.";
            }
            catch (System.Exception ex) { queueError = ex.Message; }

            stream.Dispose();
            UnityEngine.Debug.LogWarning("[UnionAir] Could not queue artifact transfer: " + queueError);
            SendError(response, "Artifact transfer could not be queued. Try the request again.", 503);
        }

        private static string EscapeHeaderFileName(string value)
            => string.IsNullOrEmpty(value)
                ? "artifact.bin"
                : value.Replace("\"", "_").Replace("\r", "_").Replace("\n", "_");

        /// <summary>
        /// Writes a JSON error response using the standard <c>{"error":"..."}</c> shape.
        /// </summary>
        /// <param name="response">HTTP response to write to.</param>
        /// <param name="message">Error message to include in the response body.</param>
        /// <param name="statusCode">HTTP status code for the error.</param>
        public static void SendError(HttpListenerResponse response, string message, int statusCode = 500)
        {
            Send(response, $"{{\"error\":\"{EscapeJson(message)}\"}}", statusCode);
        }

        /// <summary>
        /// Writes a 404 JSON error response.
        /// </summary>
        /// <param name="response">HTTP response to write to.</param>
        /// <param name="message">Optional not-found message.</param>
        public static void SendNotFound(HttpListenerResponse response, string message = "Not found")
        {
            SendError(response, message, 404);
        }

        /// <summary>
        /// Adds the CORS headers used by all UnionAir responses.
        /// </summary>
        /// <param name="response">HTTP response to modify.</param>
        public static void AddCorsHeaders(HttpListenerResponse response)
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, DELETE, PATCH, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
        }

        /// <summary>
        /// Formats a float for safe inclusion in JSON, mapping NaN and Infinity to <c>null</c>.
        /// </summary>
        /// <param name="v">Value to format.</param>
        /// <returns>Culture-invariant decimal string, or <c>"null"</c> for non-finite values.</returns>
        public static string FormatFloat(float v)
            => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("G", CultureInfo.InvariantCulture);

        /// <summary>
        /// Returns the JSON literal <c>true</c> or <c>false</c> for a boolean value.
        /// </summary>
        public static string FormatBool(bool b) => b ? "true" : "false";

        /// <summary>
        /// Formats a nullable string as a complete JSON string literal or <c>null</c>.
        /// Empty strings remain empty JSON strings; callers must explicitly map them to null when required.
        /// </summary>
        public static string FormatNullableString(string value)
            => value == null ? "null" : $"\"{EscapeJson(value)}\"";

        /// <summary>
        /// Escapes a string for safe inclusion in a JSON string literal.
        /// </summary>
        /// <param name="s">Input string to escape.</param>
        /// <returns>Escaped JSON string content without surrounding quotes.</returns>
        public static string EscapeJson(string s)
        {
            if (s == null) return "";

            // fast path: return the original string when no escaping is needed
            bool needsEscape = false;
            for (int i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                if (ch < 0x20 || ch == '"' || ch == '\\' || (ch >= (char)0x2028 && ch <= (char)0x2029))
                {
                    needsEscape = true;
                    break;
                }
            }
            if (!needsEscape) return s;

            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20 || c == '\u2028' || c == '\u2029')
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
