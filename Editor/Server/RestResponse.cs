using System.Globalization;
using System.Net;
using System.Text;

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
        /// Escapes a string for safe inclusion in a JSON string literal.
        /// </summary>
        /// <param name="s">Input string to escape.</param>
        /// <returns>Escaped JSON string content without surrounding quotes.</returns>
        public static string EscapeJson(string s)
        {
            if (s == null) return "";

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
