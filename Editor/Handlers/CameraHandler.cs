using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class CameraHandler : IRequestHandler
    {
        private const int MaxWidth  = 1920;
        private const int MaxHeight = 1080;

        /// <summary>
        /// Determines whether this handler can process the request.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <returns>True when this handler supports the request.</returns>
        public bool CanHandle(HttpListenerRequest request)
            => request.HttpMethod == "GET" &&
               (request.Url.AbsolutePath == "/api/cameras" ||
                request.Url.AbsolutePath == "/api/cameras/capture" ||
                request.Url.AbsolutePath == "/api/cameras/capture/image");

        /// <summary>
        /// Processes the request and writes the HTTP response.
        /// </summary>
        /// <param name="request">Incoming HTTP request.</param>
        /// <param name="response">HTTP response to write.</param>
        public void Handle(HttpListenerRequest request, HttpListenerResponse response)
        {
            switch (request.Url.AbsolutePath)
            {
                case "/api/cameras/capture/image": HandleCaptureImage(request, response); break;
                case "/api/cameras/capture":        HandleCapture(request, response);      break;
                default:                            HandleList(response);                  break;
            }
        }

        // ── Camera list ────────────────────────────────────────────────────────

        private static void HandleList(HttpListenerResponse response)
        {
            var cameras = new List<(Camera cam, string path)>();

            var scene = EditorSceneManager.GetActiveScene();
            foreach (var (go, goPath) in SceneUtils.GetAllGameObjects(scene))
            {
                var cam = go.GetComponent<Camera>();
                if (cam != null)
                    cameras.Add((cam, goPath));
            }

            var sb = new StringBuilder();
            sb.Append("{\"count\":");
            sb.Append(cameras.Count);
            sb.Append(",\"cameras\":[");

            for (int i = 0; i < cameras.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var (cam, path) = cameras[i];
                sb.Append("{");
                sb.Append($"\"path\":\"{RestResponse.EscapeJson(path)}\",");
                sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(cam.gameObject))}\",");
                sb.Append($"\"componentGlobalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(cam))}\",");
                sb.Append($"\"name\":\"{RestResponse.EscapeJson(cam.name)}\",");
                sb.Append($"\"enabled\":{Bool(cam.enabled)},");
                sb.Append($"\"depth\":{F(cam.depth)},");
                sb.Append($"\"fieldOfView\":{F(cam.fieldOfView)},");
                sb.Append($"\"isOrthographic\":{Bool(cam.orthographic)},");
                sb.Append($"\"tag\":\"{RestResponse.EscapeJson(cam.tag)}\"");
                sb.Append("}");
            }

            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── Capture ────────────────────────────────────────────────────────────

        private static void HandleCapture(HttpListenerRequest request, HttpListenerResponse response)
        {
            var query = request.QueryString;

            // --- path (required) ---
            var path = query["path"] ?? "";
            var globalObjectId = query["globalObjectId"] ?? "";
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(globalObjectId))
            {
                RestResponse.SendError(response, "Query parameter 'path' or 'globalObjectId' is required.", 400);
                return;
            }

            // --- resolution ---
            int width  = ParseClamp(query["width"],  640, 1, MaxWidth);
            int height = ParseClamp(query["height"], 360, 1, MaxHeight);

            // --- format / quality ---
            var format  = (query["format"] ?? "jpeg").ToLowerInvariant();
            if (format != "png" && format != "jpeg") format = "jpeg";
            int quality = ParseClamp(query["quality"], 85, 1, 100);

            // --- find camera ---
            Camera camera = FindCamera(path, globalObjectId, response);
            if (camera == null)
            {
                return;
            }
            path = GameObjectUtils.GetPath(camera.gameObject);

            // --- render ---
            byte[] bytes;
            try
            {
                bytes = RenderCamera(camera, width, height, format, quality);
            }
            catch (Exception ex)
            {
                RestResponse.SendError(response, $"Render failed: {ex.Message}", 500);
                return;
            }

            string base64   = Convert.ToBase64String(bytes);
            string mimeType = format == "png" ? "image/png" : "image/jpeg";

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"cameraPath\":\"{RestResponse.EscapeJson(path)}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(camera.gameObject))}\",");
            sb.Append($"\"componentGlobalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(camera))}\",");
            sb.Append($"\"width\":{width},");
            sb.Append($"\"height\":{height},");
            sb.Append($"\"format\":\"{RestResponse.EscapeJson(format)}\",");
            sb.Append($"\"mimeType\":\"{RestResponse.EscapeJson(mimeType)}\",");
            sb.Append($"\"data\":\"{RestResponse.EscapeJson(base64)}\"");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        private static void HandleCaptureImage(HttpListenerRequest request, HttpListenerResponse response)
        {
            var query = request.QueryString;

            var path = query["path"] ?? "";
            var globalObjectId = query["globalObjectId"] ?? "";
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(globalObjectId))
            {
                RestResponse.SendError(response, "Query parameter 'path' or 'globalObjectId' is required.", 400);
                return;
            }

            int width  = ParseClamp(query["width"],  640, 1, MaxWidth);
            int height = ParseClamp(query["height"], 360, 1, MaxHeight);

            var format  = (query["format"] ?? "jpeg").ToLowerInvariant();
            if (format != "png" && format != "jpeg") format = "jpeg";
            int quality = ParseClamp(query["quality"], 85, 1, 100);

            Camera camera = FindCamera(path, globalObjectId, response);
            if (camera == null)
            {
                return;
            }

            byte[] bytes;
            try
            {
                bytes = RenderCamera(camera, width, height, format, quality);
            }
            catch (Exception ex)
            {
                RestResponse.SendError(response, $"Render failed: {ex.Message}", 500);
                return;
            }

            string mimeType = format == "png" ? "image/png" : "image/jpeg";
            RestResponse.SendBinary(response, bytes, mimeType);
        }

        private static Camera FindCamera(string path, string globalObjectId, HttpListenerResponse response)
        {
            if (!string.IsNullOrEmpty(globalObjectId))
            {
                if (!ObjectIdUtils.TryResolveGameObjectOrComponent(globalObjectId, out var go, out var component, out var error, out var statusCode))
                {
                    RestResponse.SendError(response, error, statusCode);
                    return null;
                }

                var camera = component as Camera;
                if (camera != null) return camera;

                if (go != null)
                    camera = go.GetComponent<Camera>();

                if (camera == null)
                    RestResponse.SendError(response, $"No Camera component found for globalObjectId '{globalObjectId}'.", 404);

                return camera;
            }

            var scene = EditorSceneManager.GetActiveScene();
            foreach (var (go, goPath) in SceneUtils.GetAllGameObjects(scene))
            {
                if (goPath == path)
                {
                    var camera = go.GetComponent<Camera>();
                    if (camera == null)
                        RestResponse.SendError(response, $"No Camera component found at path '{path}'.", 404);
                    return camera;
                }
            }

            RestResponse.SendError(response, $"No Camera component found at path '{path}'.", 404);
            return null;
        }

        private static byte[] RenderCamera(Camera cam, int width, int height, string format, int quality)
        {
            var prevTarget    = cam.targetTexture;
            var prevActive    = RenderTexture.active;

            var rt  = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);

            try
            {
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                return format == "png" ? tex.EncodeToPNG() : tex.EncodeToJPG(quality);
            }
            finally
            {
                cam.targetTexture    = prevTarget;
                RenderTexture.active = prevActive;

                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static int ParseClamp(string s, int defaultVal, int min, int max)
        {
            if (!int.TryParse(s, out int v)) return defaultVal;
            return Math.Max(min, Math.Min(max, v));
        }

        private static string Bool(bool b) => b ? "true" : "false";
        private static string F(float v)   => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("G", CultureInfo.InvariantCulture);
    }
}
