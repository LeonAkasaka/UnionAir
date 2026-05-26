using System.Net;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/editor/menu-item by executing a Unity Editor menu item path.
    /// </summary>
    internal class EditorMenuItemHandler : IRequestHandler
    {
        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "POST" && request.Url.AbsolutePath == "/api/editor/menu-item";

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var path = RequestBodyReader.GetString(body, "path");

            if (string.IsNullOrWhiteSpace(path))
            {
                RestResponse.SendError(response, "Body field 'path' is required.", 400);
                return;
            }

            path = path.Trim();
            if (!EditorApplication.ExecuteMenuItem(path))
            {
                RestResponse.SendError(response,
                    "Menu item was not found, is disabled, or could not be executed: " + path,
                    404);
                return;
            }

            RestResponse.Send(response,
                "{\"executed\":true,\"path\":\"" + RestResponse.EscapeJson(path) + "\"}");
        }
    }
}
