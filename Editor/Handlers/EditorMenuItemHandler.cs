using System.Net;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles POST /api/editor/menu-item by executing a Unity Editor menu item path.
    /// </summary>
    internal class EditorMenuItemHandler
    {
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
