using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Shows one captured exchange: the request above, the response below.
    /// </summary>
    /// <remarks>
    /// A window of its own rather than a pane inside the REST Bridge window, because bodies are
    /// routinely larger than a settings window docked narrow can usefully display. Everything it
    /// renders comes from <see cref="RequestLogFormatter"/>, so the arrangement here can change
    /// without touching how anything is described.
    /// </remarks>
    public class UnionAirRequestDetailWindow : EditorWindow
    {
        /// <summary>
        /// Identifier of the displayed entry.
        /// </summary>
        /// <remarks>
        /// The identifier rather than the entry: an EditorWindow is serialized across a domain
        /// reload but the capture store is not, so a window restored afterwards refers to a record
        /// that no longer exists. Looking it up on every repaint is what lets that be reported
        /// instead of rendering stale content.
        /// </remarks>
        [SerializeField] private long _entryId = -1;

        private Vector2 _requestScroll;
        private Vector2 _responseScroll;
        private int _lastVersion = -1;

        /// <summary>
        /// Opens the detail window on an entry, reusing the open window when there is one.
        /// </summary>
        internal static void ShowEntry(long entryId)
        {
            // GetWindow returns the existing instance when one is open, which is exactly the
            // "update rather than open another" behavior, with no instance tracking of our own.
            var window = GetWindow<UnionAirRequestDetailWindow>("UnionAir Request");
            window.minSize = new Vector2(420, 320);
            window._entryId = entryId;
            window.Repaint();
        }

        private void OnInspectorUpdate()
        {
            // A deferred response completes on a thread pool thread, so the change cannot be
            // pushed here; the window polls instead.
            var version = RequestLogStore.Instance.Version;
            if (version == _lastVersion) return;
            _lastVersion = version;
            Repaint();
        }

        private void OnGUI()
        {
            if (_entryId < 0)
            {
                EditorGUILayout.HelpBox(
                    "Select an entry in the Request Log to inspect it.", MessageType.Info);
                return;
            }

            var entry = RequestLogStore.Instance.Find(_entryId);
            if (entry == null)
            {
                EditorGUILayout.HelpBox(
                    "This record is no longer available. Captured requests are held for the " +
                    "current Editor session only and are lost on a domain reload.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                RequestLogFormatter.SummaryLine(entry), EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            var sectionHeight = Mathf.Max(80f, (position.height - 150f) * 0.5f);
            DrawRequest(entry, sectionHeight);
            EditorGUILayout.Space(6);
            DrawResponse(entry, sectionHeight);
        }

        private void DrawRequest(RequestLogEntry entry, float height)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Request", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(entry.RequestBody)))
                {
                    if (GUILayout.Button("Copy Body", EditorStyles.miniButton, GUILayout.Width(80)))
                        GUIUtility.systemCopyBuffer = entry.RequestBody;
                    if (GUILayout.Button("Save...", EditorStyles.miniButton, GUILayout.Width(60)))
                        SaveBody(entry, entry.RequestBody, false);
                }

                using (new EditorGUI.DisabledScope(!RequestLogFormatter.CanBuildCurl(entry)))
                {
                    // Split button: the main half copies for whichever shell is selected, and the
                    // arrow changes the selection. No quoting form works in every shell, so the
                    // choice has to exist, but it is made once rather than on every copy.
                    var shell = UnionAirSettings.CurlShell;
                    var copy = new GUIContent(
                        "Copy as curl",
                        "Quoted for " + RequestLogFormatter.ShellLabel(shell));
                    if (GUILayout.Button(copy, EditorStyles.miniButtonLeft, GUILayout.Width(90)))
                        GUIUtility.systemCopyBuffer =
                            RequestLogFormatter.BuildCurl(entry, shell);

                    // Escaped rather than written literally: sources here are UTF-8 without a BOM,
                    // which a Shift-JIS machine reads as mojibake.
                    var arrow = new GUIContent("\u25BE", "Choose the shell to quote for");
                    if (GUILayout.Button(arrow, EditorStyles.miniButtonRight, GUILayout.Width(18)))
                        ShowCurlMenu();
                }
            }

            EditorGUILayout.SelectableLabel(
                RequestLogFormatter.RequestSummary(entry),
                EditorStyles.miniLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight * 3f));

            bool clipped;
            var body = RequestLogFormatter.RequestBodyText(entry, out clipped);
            DrawBody(body, clipped, ref _requestScroll, height);
        }

        private void DrawResponse(RequestLogEntry entry, float height)
        {
            var hasBody = entry.Completed && entry.ResponseBodyCaptured && entry.ResponseBody != null;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Response", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!hasBody))
                {
                    if (GUILayout.Button("Copy Body", EditorStyles.miniButton, GUILayout.Width(80)))
                        GUIUtility.systemCopyBuffer =
                            System.Text.Encoding.UTF8.GetString(entry.ResponseBody);
                    if (GUILayout.Button("Save...", EditorStyles.miniButton, GUILayout.Width(60)))
                        SaveBody(
                            entry,
                            System.Text.Encoding.UTF8.GetString(entry.ResponseBody),
                            true);
                }
            }

            EditorGUILayout.LabelField(
                RequestLogFormatter.ResponseSummary(entry), EditorStyles.miniLabel);

            bool clipped;
            var body = RequestLogFormatter.ResponseBodyText(entry, out clipped);
            DrawBody(body, clipped, ref _responseScroll, height);
        }

        private static void DrawBody(string body, bool clipped, ref Vector2 scroll, float height)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(height));
            EditorGUILayout.SelectableLabel(
                body,
                EditorStyles.textArea,
                GUILayout.ExpandHeight(true),
                GUILayout.ExpandWidth(true));
            EditorGUILayout.EndScrollView();

            if (clipped)
                EditorGUILayout.HelpBox(
                    "Only the first " + RequestLogFormatter.MaxDisplayChars +
                    " characters are shown. Copy or save to get the whole body.",
                    MessageType.Info);
        }

        private static void ShowCurlMenu()
        {
            var current = UnionAirSettings.CurlShell;
            var menu = new GenericMenu();
            AddShellItem(menu, current, CurlShell.Bash);
            AddShellItem(menu, current, CurlShell.PowerShell7);
            AddShellItem(menu, current, CurlShell.WindowsPowerShell);
            menu.ShowAsContext();
        }

        private static void AddShellItem(GenericMenu menu, CurlShell current, CurlShell shell)
        {
            menu.AddItem(
                new GUIContent(RequestLogFormatter.ShellLabel(shell)),
                current == shell,
                () => UnionAirSettings.CurlShell = shell);
        }

        private static void SaveBody(RequestLogEntry entry, string body, bool response)
        {
            // A concrete extension rather than a wildcard: Unity appends the filter to the name
            // it is given, and "*" produces a file the OS does not associate with anything.
            var suggested = RequestLogFormatter.SuggestFileName(entry, response);
            var extension = System.IO.Path.GetExtension(suggested).TrimStart('.');

            var path = EditorUtility.SaveFilePanel(
                response ? "Save Response Body" : "Save Request Body",
                "",
                System.IO.Path.GetFileNameWithoutExtension(suggested),
                extension);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                System.IO.File.WriteAllText(path, body, new System.Text.UTF8Encoding(false));
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Save Failed", ex.Message, "OK");
            }
        }
    }
}
