using System.Net;
using UnityEditor;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles play mode control endpoints:
    ///   POST /api/editor/play   — enter play mode
    ///   POST /api/editor/stop   — exit play mode
    ///   POST /api/editor/pause  — set pause state (body: {"paused": bool}, or toggle if omitted)
    ///   POST /api/editor/step   — advance one frame (requires isPaused == true)
    /// All require the Play Mode category to be enabled.
    /// </summary>
    internal class EditorPlayHandler
    {
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            switch (request.Url.AbsolutePath)
            {
                case "/api/editor/play":  HandlePlay(response);           break;
                case "/api/editor/stop":  HandleStop(response);           break;
                case "/api/editor/pause": HandlePause(request, response); break;
                case "/api/editor/step":  HandleStep(response);           break;
            }
        }

        private static void HandlePlay(HttpListenerResponse response)
        {
            EditorApplication.isPlaying = true;
            RestResponse.Send(response,
                "{\"playing\":true," +
                "\"note\":\"Domain reload may occur. Poll GET /api/editor/status until isPlaying is true.\"}");
        }

        private static void HandleStop(HttpListenerResponse response)
        {
            EditorApplication.isPlaying = false;
            RestResponse.Send(response, "{\"playing\":false}");
        }

        private static void HandlePause(HttpListenerRequest request, HttpListenerResponse response)
        {
            // Body is optional; if omitted, toggle current pause state
            var body = RequestBodyReader.ReadString(request);
            bool targetPaused;

            var pausedStr = RequestBodyReader.GetString(body, "paused");
            if (pausedStr == "true" || pausedStr == "1")
                targetPaused = true;
            else if (pausedStr == "false" || pausedStr == "0")
                targetPaused = false;
            else
                targetPaused = !EditorApplication.isPaused; // toggle

            EditorApplication.isPaused = targetPaused;
            RestResponse.Send(response, $"{{\"paused\":{(targetPaused ? "true" : "false")}}}");
        }

        private static void HandleStep(HttpListenerResponse response)
        {
            if (!EditorApplication.isPaused)
            {
                RestResponse.SendError(response,
                    "EditorApplication.Step() requires isPaused == true. Pause first with POST /api/editor/pause.", 400);
                return;
            }

            EditorApplication.Step();
            RestResponse.Send(response, "{\"stepped\":true}");
        }
    }
}
