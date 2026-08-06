using System;
using System.Collections.Specialized;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal class EditorCaptureHandler
    {
        private const int MaxWidth  = 1920;
        private const int MaxHeight = 1080;

        public void HandleCapture(UnionAirRequest request, UnionAirResponse response)
        {
            if (!TryCapture(request, response,
                    out var source, out var cameraName,
                    out var width, out var height, out var format, out var quality, out var bytes))
                return;

            var base64   = Convert.ToBase64String(bytes);
            var mimeType = format == "png" ? "image/png" : "image/jpeg";
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"source\":\"{RestResponse.EscapeJson(source)}\",");
            if (cameraName != null)
                sb.Append($"\"cameraName\":\"{RestResponse.EscapeJson(cameraName)}\",");
            sb.Append($"\"width\":{width},");
            sb.Append($"\"height\":{height},");
            sb.Append($"\"format\":\"{RestResponse.EscapeJson(format)}\",");
            sb.Append($"\"mimeType\":\"{RestResponse.EscapeJson(mimeType)}\",");
            sb.Append($"\"image\":\"{RestResponse.EscapeJson(base64)}\"");
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        public void HandleCaptureImage(UnionAirRequest request, UnionAirResponse response)
        {
            if (!TryCapture(request, response,
                    out _, out _, out _, out _, out var format, out _, out var bytes))
                return;

            var mimeType = format == "png" ? "image/png" : "image/jpeg";
            RestResponse.SendBinary(response, bytes, mimeType);
        }

        private static bool TryCapture(
            UnionAirRequest request, UnionAirResponse response,
            out string source, out string cameraName,
            out int width, out int height, out string format, out int quality, out byte[] bytes)
        {
            source = null; cameraName = null;
            width = 0; height = 0; format = "jpeg"; quality = 85; bytes = null;

            var query = request.QueryString;
            format  = (query["format"] ?? "jpeg").ToLowerInvariant();
            if (format != "png" && format != "jpeg") format = "jpeg";
            quality = ParseClamp(query["quality"], 85, 1, 100);

            if (EditorApplication.isPlaying)
                return TryCapturePlayMode(response, query, format, quality,
                    ref source, ref cameraName, ref width, ref height, ref bytes);
            else
                return TryCaptureEditMode(response, query, format, quality,
                    ref source, ref cameraName, ref width, ref height, ref bytes);
        }

        // Play mode: tries to read the GameView's RenderTexture directly via reflection
        // (UnityEditor.PlayModeView.m_RenderTexture), which contains the fully composited
        // frame including Screen Space Overlay Canvas.
        // Falls back to ScreenCapture.CaptureScreenshotAsTexture() if reflection fails.
        private static bool TryCapturePlayMode(
            UnionAirResponse response, NameValueCollection query,
            string format, int quality,
            ref string source, ref string cameraName,
            ref int width, ref int height, ref byte[] bytes)
        {
            try
            {
                var rt = TryGetGameViewRenderTexture();

                if (rt != null && rt.IsCreated())
                {
                    var nativeW = rt.width;
                    var nativeH = rt.height;
                    width  = ParseClamp(query["width"],  nativeW, 1, MaxWidth);
                    height = ParseClamp(query["height"], nativeH, 1, MaxHeight);

                    var cam = Camera.main;
                    cameraName = cam != null ? cam.name : null;
                    source = "screen";

                    bytes = width != nativeW || height != nativeH
                        ? ScaleAndEncode(rt, width, height, format, quality, flipVertical: true)
                        : ReadEncodeFromRT(rt, nativeW, nativeH, format, quality, flipVertical: true);

                    return true;
                }

                // Fallback: ScreenCapture (requires Game View to be focused/visible)
                Texture2D screenTex = null;
                try
                {
                    screenTex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (screenTex == null)
                    {
                        RestResponse.SendError(response, "Screen capture returned null.", 500);
                        return false;
                    }

                    var nativeW = screenTex.width;
                    var nativeH = screenTex.height;
                    width  = ParseClamp(query["width"],  nativeW, 1, MaxWidth);
                    height = ParseClamp(query["height"], nativeH, 1, MaxHeight);

                    var cam = Camera.main;
                    cameraName = cam != null ? cam.name : null;
                    source = "screen";

                    bytes = width != nativeW || height != nativeH
                        ? ScaleAndEncode(screenTex, width, height, format, quality)
                        : (format == "png" ? screenTex.EncodeToPNG() : screenTex.EncodeToJPG(quality));

                    return true;
                }
                finally
                {
                    if (screenTex != null) UnityEngine.Object.DestroyImmediate(screenTex);
                }
            }
            catch (Exception ex)
            {
                RestResponse.SendError(response, $"Screen capture failed: {ex.Message}", 500);
                return false;
            }
        }

        // Accesses the internal RenderTexture (GameView.m_RenderTexture) that the Unity Editor
        // renders the game to. This RT contains the fully composited frame including
        // Screen Space Overlay Canvas. Falls back to scanning all RT fields if the known
        // field name is not found (to guard against future Unity API changes).
        // Note: m_TargetTexture is intentionally skipped — that is the user-assigned
        // "Target Texture" override in the Game View settings, not the render output.
        private static RenderTexture TryGetGameViewRenderTexture()
        {
            var playModeViewType = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
            if (playModeViewType == null) return null;

            var windows = Resources.FindObjectsOfTypeAll(playModeViewType);
            if (windows == null || windows.Length == 0) return null;

            var gameView = windows[0] as EditorWindow;
            if (gameView == null) return null;

            var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor") ?? playModeViewType;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

            // Fast path: known field name in Unity 6
            var knownField = gameViewType.GetField("m_RenderTexture", flags);
            if (knownField != null)
            {
                var rt = knownField.GetValue(gameView) as RenderTexture;
                if (rt != null && rt.IsCreated()) return rt;
            }

            // Fallback: scan all RenderTexture fields, skipping the user-assigned target override
            foreach (var field in gameViewType.GetFields(flags))
            {
                if (field.FieldType != typeof(RenderTexture)) continue;
                if (field.Name == "m_TargetTexture") continue;
                var rt = field.GetValue(gameView) as RenderTexture;
                if (rt != null && rt.IsCreated()) return rt;
            }

            return null;
        }

        // Reads and encodes pixels directly from a RenderTexture.
        // RenderTexture.active is always restored and Texture2D released in finally.
        // flipVertical: GameView.m_RenderTexture is stored top-down (DirectX convention),
        // so the result of ReadPixels must be flipped to match the display orientation.
        private static byte[] ReadEncodeFromRT(RenderTexture rt, int width, int height, string format, int quality, bool flipVertical = false)
        {
            var prevActive = RenderTexture.active;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                if (flipVertical) FlipTextureVertically(tex);
                return format == "png" ? tex.EncodeToPNG() : tex.EncodeToJPG(quality);
            }
            finally
            {
                RenderTexture.active = prevActive;
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static bool TryCaptureEditMode(
            UnionAirResponse response, NameValueCollection query,
            string format, int quality,
            ref string source, ref string cameraName,
            ref int width, ref int height, ref byte[] bytes)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null)
            {
                RestResponse.SendError(response, "No Scene View is currently open.", 503);
                return false;
            }

            width  = ParseClamp(query["width"],  640, 1, MaxWidth);
            height = ParseClamp(query["height"], 360, 1, MaxHeight);
            cameraName = sv.camera.name;
            source = "sceneView";

            try
            {
                bytes = CameraHandler.RenderCamera(sv.camera, width, height, format, quality);
                return true;
            }
            catch (Exception ex)
            {
                RestResponse.SendError(response, $"Render failed: {ex.Message}", 500);
                return false;
            }
        }

        // Overload: scale from a RenderTexture source.
        // flipVertical: same top-down convention issue as ReadEncodeFromRT.
        private static byte[] ScaleAndEncode(RenderTexture src, int width, int height, string format, int quality, bool flipVertical = false)
        {
            var prevActive = RenderTexture.active;
            var rt  = new RenderTexture(width, height, 0);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                if (flipVertical) FlipTextureVertically(tex);
                return format == "png" ? tex.EncodeToPNG() : tex.EncodeToJPG(quality);
            }
            finally
            {
                RenderTexture.active = prevActive;
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        // Scales src to (width x height) using a temporary RenderTexture, then encodes.
        // RenderTexture.active is always restored and all resources released in finally.
        private static byte[] ScaleAndEncode(Texture2D src, int width, int height, string format, int quality)
        {
            var prevActive = RenderTexture.active;
            var rt  = new RenderTexture(width, height, 0);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                return format == "png" ? tex.EncodeToPNG() : tex.EncodeToJPG(quality);
            }
            finally
            {
                RenderTexture.active = prevActive;
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        // Flips a Texture2D vertically in-place using GetPixels32/SetPixels32.
        // Required when reading from RenderTextures stored in top-down (DirectX) convention.
        private static void FlipTextureVertically(Texture2D tex)
        {
            var pixels = tex.GetPixels32();
            int w = tex.width;
            int h = tex.height;
            for (int y = 0; y < h / 2; y++)
            {
                int y2 = h - 1 - y;
                for (int x = 0; x < w; x++)
                {
                    var tmp           = pixels[y  * w + x];
                    pixels[y  * w + x] = pixels[y2 * w + x];
                    pixels[y2 * w + x] = tmp;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
        }

        private static int ParseClamp(string s, int defaultVal, int min, int max)
        {
            if (!int.TryParse(s, out int v)) return defaultVal;
            return Math.Max(min, Math.Min(max, v));
        }
    }
}
