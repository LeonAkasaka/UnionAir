using System.Net;
using System.Text;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Helper for writing JSON HTTP responses with CORS headers.
    /// </summary>
    internal static class RestResponse
    {
        public static void Send(HttpListenerResponse response, string json, int statusCode = 200)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            AddCorsHeaders(response);

            var bytes = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        public static void SendBinary(HttpListenerResponse response, byte[] data, string mimeType)
        {
            response.StatusCode = 200;
            response.ContentType = mimeType;
            AddCorsHeaders(response);

            response.ContentLength64 = data.Length;
            response.OutputStream.Write(data, 0, data.Length);
        }

        public static void SendError(HttpListenerResponse response, string message, int statusCode = 500)
        {
            Send(response, $"{{\"error\":\"{EscapeJson(message)}\"}}", statusCode);
        }

        public static void SendNotFound(HttpListenerResponse response, string message = "Not found")
        {
            SendError(response, message, 404);
        }

        public static void AddCorsHeaders(HttpListenerResponse response)
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, DELETE, PATCH, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
        }

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
