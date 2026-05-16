using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    public class UnionAirWindow : EditorWindow
    {
        private const int MaxLogLines = 100;
        private const string PrefKeyReadFold       = "UnionAir.UI.Foldout.Read";
        private const string PrefKeyWriteFold      = "UnionAir.UI.Foldout.Write";
        private const string PrefKeyAssetWriteFold = "UnionAir.UI.Foldout.AssetWrite";
        private const string PrefKeyPlayModeFold   = "UnionAir.UI.Foldout.PlayMode";

        private readonly List<string> _log = new List<string>();
        private Vector2 _logScroll;
        private int _portInput;

        private bool _showReadEndpoints;
        private bool _showWriteEndpoints;
        private bool _showAssetWriteEndpoints;
        private bool _showPlayModeEndpoints;

        [MenuItem("Window/UnionAir/REST Bridge")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnionAirWindow>("UnionAir");
            window.minSize = new Vector2(320, 380);
            window.Show();
        }

        private void OnEnable()
        {
            _portInput = UnionAirSettings.Port;
            _showReadEndpoints       = EditorPrefs.GetBool(PrefKeyReadFold,       false);
            _showWriteEndpoints      = EditorPrefs.GetBool(PrefKeyWriteFold,      false);
            _showAssetWriteEndpoints = EditorPrefs.GetBool(PrefKeyAssetWriteFold, false);
            _showPlayModeEndpoints   = EditorPrefs.GetBool(PrefKeyPlayModeFold,   false);
            UnionAirInit.Server.OnRequest += AddLog;
        }

        private void OnDisable()
        {
            UnionAirInit.Server.OnRequest -= AddLog;
        }

        private void AddLog(string message)
        {
            _log.Add($"[{System.DateTime.Now:HH:mm:ss}] {message}");
            if (_log.Count > MaxLogLines)
                _log.RemoveAt(0);
            Repaint();
        }

        private void OnGUI()
        {
            var server = UnionAirInit.Server;
            var isRunning = server.IsRunning;
            var writeEnabled = UnionAirSettings.WriteEnabled;
            var assetWriteEnabled = UnionAirSettings.AssetWriteEnabled;
            var playModeEnabled = UnionAirSettings.PlayModeEnabled;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("UnionAir — REST Bridge", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // ── Status ──────────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Status");
                var prevColor = GUI.color;
                GUI.color = isRunning ? Color.green : Color.gray;
                var statusLabel = isRunning
                        ? $"● Running  (port {server.Port}){(writeEnabled ? "  [WRITE]" : "")}{(assetWriteEnabled ? "  [ASSET-WRITE]" : "")}{(playModeEnabled ? "  [PLAY-MODE]" : "")}"
                    : "○ Stopped";
                EditorGUILayout.LabelField(statusLabel, EditorStyles.boldLabel);
                GUI.color = prevColor;
            }

            if (isRunning)
                EditorGUILayout.LabelField("Base URL",
                    $"http://localhost:{server.Port}/api/", EditorStyles.miniLabel);

            EditorGUILayout.Space(6);

            // ── Settings ─────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(isRunning))
                _portInput = EditorGUILayout.IntField("Port", _portInput);

            UnionAirSettings.AutoStart =
                EditorGUILayout.Toggle("Auto Start on Load", UnionAirSettings.AutoStart);

            EditorGUILayout.Space(6);

            // ── Write API ────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Write API", EditorStyles.boldLabel);

            var warningStyle = new GUIStyle(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "⚠ Write operations modify the scene. All changes are Undo-able within the Editor.",
                warningStyle);

            var prevBg = GUI.backgroundColor;
            if (writeEnabled) GUI.backgroundColor = new Color(1f, 0.5f, 0.4f);
            var newWriteEnabled = EditorGUILayout.Toggle("Enable Write API", writeEnabled);
            GUI.backgroundColor = prevBg;

            if (newWriteEnabled != writeEnabled)
                UnionAirSettings.WriteEnabled = newWriteEnabled;

            EditorGUILayout.Space(4);

            // ── Asset Write API ───────────────────────────────────────────────
            EditorGUILayout.LabelField("Asset Write API", EditorStyles.boldLabel);

            EditorGUILayout.LabelField(
                "⚠ Asset Write operations modify files on disk (prefabs, materials, scene save).",
                warningStyle);

            if (assetWriteEnabled) GUI.backgroundColor = new Color(1f, 0.5f, 0.4f);
            var newAssetWriteEnabled = EditorGUILayout.Toggle("Enable Asset Write API", assetWriteEnabled);
            GUI.backgroundColor = prevBg;

            if (newAssetWriteEnabled != assetWriteEnabled)
                UnionAirSettings.AssetWriteEnabled = newAssetWriteEnabled;

            EditorGUILayout.Space(6);

            // ── Play Mode API ─────────────────────────────────────────────────
            EditorGUILayout.LabelField("Play Mode API", EditorStyles.boldLabel);

            EditorGUILayout.LabelField(
                "⚠ Play Mode API starts/stops game execution. May trigger a domain reload — server restarts briefly.",
                warningStyle);

            if (playModeEnabled) GUI.backgroundColor = new Color(1f, 0.5f, 0.4f);
            var newPlayModeEnabled = EditorGUILayout.Toggle("Enable Play Mode API", playModeEnabled);
            GUI.backgroundColor = prevBg;

            if (newPlayModeEnabled != playModeEnabled)
                UnionAirSettings.PlayModeEnabled = newPlayModeEnabled;

            EditorGUILayout.Space(6);

            // ── Controls ─────────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(isRunning))
                {
                    if (GUILayout.Button("Start"))
                    {
                        UnionAirSettings.Port = _portInput;
                        server.Start(_portInput);
                    }
                }
                using (new EditorGUI.DisabledScope(!isRunning))
                {
                    if (GUILayout.Button("Stop"))
                        server.Stop();
                }
                if (GUILayout.Button("Restart"))
                {
                    server.Stop();
                    UnionAirSettings.Port = _portInput;
                    server.Start(_portInput);
                }
            }

            EditorGUILayout.Space(6);

            // ── Endpoints Reference ──────────────────────────────────────────────
            var port = UnionAirSettings.Port;

            var newShowRead = EditorGUILayout.Foldout(_showReadEndpoints, "Endpoints (Read)", true);
            if (newShowRead != _showReadEndpoints)
            {
                _showReadEndpoints = newShowRead;
                EditorPrefs.SetBool(PrefKeyReadFold, _showReadEndpoints);
            }
            if (_showReadEndpoints)
            {
                DrawEndpointRow("GET",    "/api/health",                           null,                                         port);
                DrawEndpointRow("GET",    "/api/editor/status",                    null,                                         port);
                DrawEndpointRow("GET",    "/api/editor/logs",                      "[?type=&search=&limit=]",                    port);
                DrawEndpointRow("GET",    "/api/cameras",                          null,                                         port);
                DrawEndpointRow("GET",    "/api/cameras/capture",                  "?path=<path>[&width=&height=&format=&quality=]", port);
                DrawEndpointRow("GET",    "/api/cameras/capture/image",            "?path=<path>[&width=&height=&format=&quality=]", port);
                DrawEndpointRow("GET",    "/api/scene",                            null,                                         port);
                DrawEndpointRow("GET",    "/api/scene/hierarchy",                  "[?depth=N&compact=true&limit=N&path=<path>]", port);
                DrawEndpointRow("GET",    "/api/scene/stats",                      null,                                         port);
                DrawEndpointRow("GET",    "/api/gameobjects",                      "?path=<path>",                               port);
                DrawEndpointRow("GET",    "/api/assets",                           "[?path=&type=&search=]",                     port);
                DrawEndpointRow("GET",    "/api/assets/<guid>",                    null,                                         port);
                DrawEndpointRow("GET",    "/api/assets/dependents",                "?guid=<guid>",                               port);
                DrawEndpointRow("GET",    "/api/search/gameobjects",               "[?name=&component=&tag=&layer=&active=&assetGuid=]", port);
                DrawEndpointRow("GET",    "/api/search/asset-refs",                "?guid=<guid>",                               port);
            }

            EditorGUILayout.Space(2);

            using (new EditorGUI.DisabledScope(!writeEnabled))
            {
                var newShowWrite = EditorGUILayout.Foldout(_showWriteEndpoints, "Endpoints (Write — requires Enable Write API)", true);
                if (newShowWrite != _showWriteEndpoints)
                {
                    _showWriteEndpoints = newShowWrite;
                    EditorPrefs.SetBool(PrefKeyWriteFold, _showWriteEndpoints);
                }
                if (_showWriteEndpoints)
                {
                    DrawEndpointRow("POST",   "/api/gameobjects",                  "body:{name,parentPath?}",                    port);
                    DrawEndpointRow("DELETE", "/api/gameobjects",                  "?path=",                                     port);
                    DrawEndpointRow("PATCH",  "/api/gameobjects",                  "?path=  body:{name?,isActive?,tag?,layer?,transform?}", port);
                    DrawEndpointRow("POST",   "/api/gameobjects/batch",            "body:{operations:[{op,…}]}",                 port);
                    DrawEndpointRow("POST",   "/api/gameobjects/primitive",        "body:{type,name?,parentPath?}",              port);
                    DrawEndpointRow("POST",   "/api/gameobjects/instantiate",      "body:{guid|assetPath,name?,parentPath?}",    port);
                    DrawEndpointRow("POST",   "/api/gameobjects/reparent",         "body:{path,parentPath?}",                    port);
                    DrawEndpointRow("POST",   "/api/gameobjects/components",       "body:{path,type}",                           port);
                    DrawEndpointRow("DELETE", "/api/gameobjects/components",       "?path=&type=",                               port);
                    DrawEndpointRow("PATCH",  "/api/gameobjects/components",       "?path=&type=  body:{properties:{}}",         port);
                }
            }

            EditorGUILayout.Space(2);

            using (new EditorGUI.DisabledScope(!assetWriteEnabled))
            {
                var newShowAsset = EditorGUILayout.Foldout(_showAssetWriteEndpoints, "Endpoints (Asset Write — requires Enable Asset Write API)", true);
                if (newShowAsset != _showAssetWriteEndpoints)
                {
                    _showAssetWriteEndpoints = newShowAsset;
                    EditorPrefs.SetBool(PrefKeyAssetWriteFold, _showAssetWriteEndpoints);
                }
                if (_showAssetWriteEndpoints)
                {
                    DrawEndpointRow("POST",   "/api/scene/save",                   null,                                         port);
                    DrawEndpointRow("POST",   "/api/editor/refresh",               null,                                         port);
                    DrawEndpointRow("POST",   "/api/assets/prefabs",               "body:{goPath,assetPath,mode?}",              port);
                    DrawEndpointRow("POST",   "/api/assets/prefabs/apply",         "body:{goPath}",                              port);
                    DrawEndpointRow("POST",   "/api/assets/prefabs/revert",        "body:{goPath}",                              port);
                    DrawEndpointRow("POST",   "/api/assets/materials",             "body:{assetPath,shader?}",                   port);
                    DrawEndpointRow("PATCH",  "/api/assets/materials",             "?guid=  body:{properties:{}}",               port);
                    DrawEndpointRow("DELETE", "/api/assets/<guid>",                null,                                         port);
                    DrawEndpointRow("POST",   "/api/assets/move",                  "body:{guid,newPath}",                        port);
                }
            }

            EditorGUILayout.Space(2);

            using (new EditorGUI.DisabledScope(!playModeEnabled))
            {
                var newShowPlay = EditorGUILayout.Foldout(_showPlayModeEndpoints, "Endpoints (Play Mode — requires Enable Play Mode API)", true);
                if (newShowPlay != _showPlayModeEndpoints)
                {
                   _showPlayModeEndpoints = newShowPlay;
                   EditorPrefs.SetBool(PrefKeyPlayModeFold, _showPlayModeEndpoints);
                }
                if (_showPlayModeEndpoints)
                {
                   DrawEndpointRow("POST",   "/api/editor/play",                  null,                                         port);
                   DrawEndpointRow("POST",   "/api/editor/stop",                  null,                                         port);
                   DrawEndpointRow("POST",   "/api/editor/pause",                 "[body:{paused:bool}  omit=toggle]",           port);
                   DrawEndpointRow("POST",   "/api/editor/step",                  "[requires isPaused]",                        port);
                }
            }

            EditorGUILayout.Space(6);

            // ── Request Log ──────────────────────────────────────────────────────
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Request Log", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear", GUILayout.Width(50)))
                    _log.Clear();
            }

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
            foreach (var line in _log)
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawEndpointRow(string method, string path, string hint, int port)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var label = hint != null ? $"{method,-6} {path}  {hint}" : $"{method,-6} {path}";
                EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                if (GUILayout.Button("⧉", EditorStyles.miniButton, GUILayout.Width(22)))
                    GUIUtility.systemCopyBuffer = $"http://localhost:{port}{path}";
            }
        }
    }
}
