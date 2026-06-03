using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// POST /api/editor/refresh
    /// Triggers AssetDatabase.Refresh() so Unity picks up new or changed script files
    /// and starts recompilation. Returns current editor status so clients can immediately
    /// begin polling isCompiling.
    ///
    /// Typical workflow after writing a new .cs file:
    ///   1. POST /api/editor/refresh
    ///   2. Poll GET /api/editor/status until isCompiling == false
    ///      (note: domain reload will briefly restart the server — handle connection retries)
    ///   3. POST /api/gameobjects/components with the newly compiled type
    /// </summary>
    internal class EditorRefreshHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            AssetDatabase.Refresh();

            // Return editor state so the caller can start polling isCompiling immediately.
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"refreshed\":true,");
            sb.Append($"\"isCompiling\":{Bool(EditorApplication.isCompiling)},");
            sb.Append($"\"isUpdating\":{Bool(EditorApplication.isUpdating)},");
            sb.Append($"\"isPlaying\":{Bool(EditorApplication.isPlaying)}");
            sb.Append("}");

            RestResponse.Send(response, sb.ToString());
        }

        private static string Bool(bool v) => v ? "true" : "false";
    }
}
