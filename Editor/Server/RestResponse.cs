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
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
