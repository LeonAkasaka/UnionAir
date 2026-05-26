using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Unity EditorWindow used to control the UnionAir server and inspect discovered API routes.
    /// </summary>
    public class UnionAirWindow : EditorWindow
    {
        private const int MaxLogLines = 100;
        private const string PrefKeyTab = "UnionAir.UI.Tab";
        private const string PrefKeyCategoryExpandedPrefix = "UnionAir.UI.CategoryExpanded.";

        private readonly List<string> _log = new List<string>();
        private Vector2 _scroll;
        private Vector2 _logScroll;
        private int _portInput;
        private int _tab;

        /// <summary>
        /// Opens the UnionAir REST Bridge window.
        /// </summary>
        [MenuItem("Window/UnionAir/REST Bridge")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnionAirWindow>("UnionAir");
            window.minSize = new Vector2(360, 420);
            window.Show();
        }

        private void OnEnable()
        {
            _portInput = UnionAirSettings.Port;
            _tab = EditorPrefs.GetInt(PrefKeyTab, 0);
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
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("UnionAir - REST Bridge", EditorStyles.boldLabel);

            var newTab = GUILayout.Toolbar(_tab, new[] { "Server", "Built-in API", "Custom Handlers" });
            if (newTab != _tab)
            {
                _tab = newTab;
                EditorPrefs.SetInt(PrefKeyTab, _tab);
            }

            EditorGUILayout.Space(6);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 1: DrawBuiltInApiTab(); break;
                case 2: DrawCustomHandlersTab(); break;
                default: DrawServerTab(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawServerTab()
        {
            var server = UnionAirInit.Server;
            var isRunning = server.IsRunning;
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 190f;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Status");
                var prevColor = GUI.color;
                GUI.color = isRunning ? Color.green : Color.gray;
                EditorGUILayout.LabelField(isRunning ? $"Running (port {server.Port})" : "Stopped", EditorStyles.boldLabel);
                GUI.color = prevColor;
            }

            if (isRunning)
            {
                var baseUrl = $"http://localhost:{server.Port}/api/";
                DrawCopyableUrl("Base URL", baseUrl);
                DrawCopyableUrl("Help URL", baseUrl + "help");
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(isRunning))
                _portInput = EditorGUILayout.IntField("Port", _portInput);

            UnionAirSettings.AutoStart =
                EditorGUILayout.Toggle("Auto Start on Load", UnionAirSettings.AutoStart);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Play Mode Safety", EditorStyles.boldLabel);
            UnionAirSettings.AllowPlayModeSceneChanges =
                EditorGUILayout.Toggle("Allow Play Mode Scene Changes", UnionAirSettings.AllowPlayModeSceneChanges);
            EditorGUILayout.HelpBox(
                "When disabled, scene-object write endpoints are rejected during Play Mode even if the request includes allowWhilePlaying=true.",
                MessageType.Info);

            EditorGUILayout.Space(8);
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

            EditorGUILayout.Space(12);
            DrawRequestLog();
            EditorGUIUtility.labelWidth = oldLabelWidth;
        }

        private void DrawBuiltInApiTab()
        {
            DrawEndpointGroup(FindCategory(UnionAirRouteSource.Builtin, UnionAirEndpointCategories.Read));
            DrawEndpointGroup(FindCategory(UnionAirRouteSource.Builtin, UnionAirEndpointCategories.SceneWrite));
            DrawEndpointGroup(FindCategory(UnionAirRouteSource.Builtin, UnionAirEndpointCategories.AssetWrite));
            DrawEndpointGroup(FindCategory(UnionAirRouteSource.Builtin, UnionAirEndpointCategories.PlayMode));
            DrawEndpointGroup(FindCategory(UnionAirRouteSource.Builtin, UnionAirEndpointCategories.EditorActions));
        }

        private void DrawCustomHandlersTab()
        {
            var oldEnabled = UnionAirSettings.CustomHandlersEnabled;
            var newEnabled = EditorGUILayout.Toggle("Enable Custom Handlers", oldEnabled);
            if (newEnabled != oldEnabled)
            {
                UnionAirSettings.CustomHandlersEnabled = newEnabled;
                UnionAirRouteRegistry.Refresh();
            }

            if (GUILayout.Button("Rescan Custom Handlers"))
                UnionAirRouteRegistry.Refresh();

            EditorGUILayout.Space(8);

            var categories = new SortedDictionary<string, List<UnionAirEndpointDescriptor>>();
            foreach (var endpoint in UnionAirRouteRegistry.Descriptors)
            {
                if (endpoint.Source != UnionAirRouteSource.Custom)
                    continue;

                if (!categories.TryGetValue(endpoint.Category, out var list))
                {
                    list = new List<UnionAirEndpointDescriptor>();
                    categories[endpoint.Category] = list;
                }
                list.Add(endpoint);
            }

            if (categories.Count == 0)
            {
                EditorGUILayout.HelpBox("No custom handlers discovered. Add an Editor class in another assembly with UnionAirControllerAttribute and UnionAirEndpointAttribute.", MessageType.Info);
                return;
            }

            foreach (var pair in categories)
            {
                var category = FindCategory(UnionAirRouteSource.Custom, pair.Key);
                var expanded = DrawCategoryHeader(category);
                if (!expanded)
                {
                    EditorGUILayout.Space(4);
                    continue;
                }

                var controllers = new SortedDictionary<string, List<UnionAirEndpointDescriptor>>();
                foreach (var endpoint in pair.Value)
                {
                    if (!controllers.TryGetValue(endpoint.ControllerRoute, out var endpoints))
                    {
                        endpoints = new List<UnionAirEndpointDescriptor>();
                        controllers[endpoint.ControllerRoute] = endpoints;
                    }
                    endpoints.Add(endpoint);
                }

                foreach (var controller in controllers)
                {
                    EditorGUILayout.LabelField($"/api/custom/{controller.Key}", EditorStyles.miniBoldLabel);
                    controller.Value.Sort(CompareEndpointsForDisplay);
                    foreach (var endpoint in controller.Value)
                    {
                        DrawEndpointRow(endpoint);
                        if (!string.IsNullOrEmpty(endpoint.Error))
                            EditorGUILayout.HelpBox(endpoint.Error, MessageType.Error);
                    }
                }

                if (category != null && !string.IsNullOrEmpty(category.Error))
                    EditorGUILayout.HelpBox(category.Error, MessageType.Warning);
                EditorGUILayout.Space(4);
            }
        }

        private void DrawRequestLog()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Request Log", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                    _log.Clear();
            }

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
            foreach (var line in _log)
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawCopyableUrl(string label, string url)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
                EditorGUILayout.SelectableLabel(url, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(46)))
                    GUIUtility.systemCopyBuffer = url;
            }
        }

        private static void DrawEndpointGroup(UnionAirCategoryDefinition category)
        {
            if (!DrawCategoryHeader(category))
                return;

            var endpoints = new List<UnionAirEndpointDescriptor>();
            foreach (var endpoint in UnionAirRouteRegistry.Descriptors)
            {
                if (endpoint.Source == category.Source && endpoint.Category == category.Id)
                    endpoints.Add(endpoint);
            }

            endpoints.Sort(CompareEndpointsForDisplay);
            foreach (var endpoint in endpoints)
                DrawEndpointRow(endpoint);

            EditorGUILayout.Space(6);
        }

        private static UnionAirCategoryDefinition FindCategory(UnionAirRouteSource source, string id)
        {
            foreach (var category in UnionAirRouteRegistry.Categories)
            {
                if (category.Source == source && category.Id == id)
                    return category;
            }
            return null;
        }

        private static bool DrawCategoryHeader(UnionAirCategoryDefinition category)
        {
            if (category == null)
                return false;

            using (new EditorGUILayout.HorizontalScope())
            {
                var prefKey = PrefKeyCategoryExpandedPrefix + category.Key;
                var expanded = EditorPrefs.GetBool(prefKey, true);
                var foldoutRect = GUILayoutUtility.GetRect(14f, EditorGUIUtility.singleLineHeight, GUILayout.Width(14));
                var newExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                if (newExpanded != expanded)
                {
                    expanded = newExpanded;
                    EditorPrefs.SetBool(prefKey, expanded);
                }

                var canToggle = category.CanDisable &&
                                (category.Source != UnionAirRouteSource.Custom ||
                                 UnionAirSettings.CustomHandlersEnabled);
                using (new EditorGUI.DisabledScope(!canToggle))
                {
                    var newEnabled = EditorGUILayout.Toggle(
                        category.Enabled,
                        GUILayout.Width(18));
                    if (newEnabled != category.Enabled)
                    {
                        UnionAirSettings.SetCategoryEnabled(
                            category.Key,
                            newEnabled,
                            category.EnabledByDefault);
                        UnionAirRouteRegistry.Refresh();
                    }
                }

                EditorGUILayout.LabelField(
                    $"{category.DisplayName} ({RiskLabel(category.Risk)})",
                    EditorStyles.boldLabel);

                return expanded;
            }
        }

        private static int CompareEndpointsForDisplay(UnionAirEndpointDescriptor left, UnionAirEndpointDescriptor right)
        {
            var result = string.Compare(left.Path, right.Path, System.StringComparison.Ordinal);
            if (result != 0) return result;

            result = MethodRank(left.Method).CompareTo(MethodRank(right.Method));
            if (result != 0) return result;

            return string.Compare(left.Method, right.Method, System.StringComparison.Ordinal);
        }

        private static int MethodRank(string method)
        {
            switch (method)
            {
                case "GET": return 0;
                case "POST": return 1;
                case "PATCH": return 2;
                case "DELETE": return 3;
                default: return 4;
            }
        }

        private static void DrawEndpointRow(UnionAirEndpointDescriptor endpoint)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var state = endpoint.Enabled ? "" : " [disabled]";
                EditorGUILayout.LabelField($"{endpoint.Method,-6} {endpoint.Path}{state}", EditorStyles.miniLabel);
                if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(46)))
                    GUIUtility.systemCopyBuffer = $"http://localhost:{UnionAirSettings.Port}{endpoint.Path}";
            }
        }

        private static string RiskLabel(UnionAirEndpointRisk risk)
        {
            if (risk == UnionAirEndpointRisk.None)
                return "read-only";

            var label = "";
            AppendRiskLabel(ref label, risk, UnionAirEndpointRisk.SceneUpdate, "scene");
            AppendRiskLabel(ref label, risk, UnionAirEndpointRisk.AssetUpdate, "asset");
            AppendRiskLabel(ref label, risk, UnionAirEndpointRisk.PlayMode, "play mode");
            AppendRiskLabel(ref label, risk, UnionAirEndpointRisk.Custom, "custom");
            AppendRiskLabel(ref label, risk, UnionAirEndpointRisk.RequestDependent, "request-dependent");
            AppendRiskLabel(ref label, risk, UnionAirEndpointRisk.EditorState, "editor state");
            return label;
        }

        private static void AppendRiskLabel(
            ref string label,
            UnionAirEndpointRisk risk,
            UnionAirEndpointRisk flag,
            string value)
        {
            if ((risk & flag) == 0)
                return;

            label = string.IsNullOrEmpty(label) ? value : label + ", " + value;
        }
    }
}
