using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class EditorStatusHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/editor/status";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"isPlaying\":{Bool(EditorApplication.isPlaying)},");
            sb.Append($"\"isPaused\":{Bool(EditorApplication.isPaused)},");
            sb.Append($"\"isCompiling\":{Bool(EditorApplication.isCompiling)},");
            sb.Append($"\"isUpdating\":{Bool(EditorApplication.isUpdating)},");
            sb.Append($"\"unityVersion\":\"{RestResponse.EscapeJson(Application.unityVersion)}\"");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        private static string Bool(bool v) => v ? "true" : "false";
    }
}
