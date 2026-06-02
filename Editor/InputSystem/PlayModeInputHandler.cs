using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles discovery and simulation of Unity Input System actions
    /// while the Editor is in Play mode.
    /// This class lives in the optional InputSystem assembly and is only compiled
    /// when the com.unity.inputsystem package is present.
    /// </summary>
    internal static class PlayModeInputHandler
    {
        static Gamepad  _virtualGamepad;
        static Keyboard _virtualKeyboard;

        // ── Public API (called from PlayModeInputController) ─────────────────

        /// <summary>
        /// Returns all <see cref="UnityEngine.InputSystem.InputAction"/>s currently active in the
        /// running scene, collected from <c>InputSystem.ListEnabledActions()</c> and any
        /// <c>PlayerInput</c> components. Responds with 409 when not in Play mode.
        /// </summary>
        public static void HandleActions(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!EditorApplication.isPlaying)
            {
                RestResponse.SendError(response, "Not in Play mode.", 409);
                return;
            }

            var actions = CollectAllActions();
            var sb = new StringBuilder();
            sb.Append("{\"actions\":[");
            bool first = true;
            int count = 0;
            foreach (var action in actions)
            {
                if (!first) sb.Append(",");
                first = false;
                count++;
                sb.Append("{\"name\":\"").Append(RestResponse.EscapeJson(action.name)).Append("\"");
                sb.Append(",\"map\":\"").Append(RestResponse.EscapeJson(action.actionMap?.name ?? "")).Append("\"");
                sb.Append(",\"actionType\":\"").Append(action.type).Append("\"");
                sb.Append(",\"expectedControlType\":\"").Append(RestResponse.EscapeJson(action.expectedControlType ?? "")).Append("\"");
                sb.Append(",\"bindings\":[");
                bool firstBinding = true;
                foreach (var binding in action.bindings)
                {
                    if (binding.isComposite) continue;
                    var path = binding.isPartOfComposite ? binding.path : binding.effectivePath;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!firstBinding) sb.Append(",");
                    firstBinding = false;
                    sb.Append("\"").Append(RestResponse.EscapeJson(path)).Append("\"");
                }
                sb.Append("]}");
            }
            sb.Append("],\"count\":").Append(count).Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>
        /// Simulates an <see cref="UnityEngine.InputSystem.InputAction"/> via a virtual Gamepad or
        /// Keyboard device. The action is looked up by name (case-insensitive). Supported control
        /// types: <c>Button</c>, <c>Vector2</c>/<c>Stick</c>, <c>Axis</c>.
        /// Responds with 409 when not in Play mode, 404 when the action is not found.
        /// </summary>
        public static void HandlePerform(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!EditorApplication.isPlaying)
            {
                RestResponse.SendError(response, "Not in Play mode.", 409);
                return;
            }

            var body = RequestBodyReader.ReadString(request);
            var actionName = RequestBodyReader.GetString(body, "action");
            if (string.IsNullOrEmpty(actionName))
            {
                RestResponse.SendError(response, "Required field 'action' is missing.", 400);
                return;
            }

            var action = FindAction(actionName);
            if (action == null)
            {
                RestResponse.SendError(response, $"Action not found: '{actionName}'", 404);
                return;
            }

            var controlType = action.expectedControlType ?? "";
            bool isButton  = action.type == InputActionType.Button || controlType == "Button";
            bool isVector2 = controlType == "Vector2" || controlType == "Stick";
            bool isAxis    = controlType == "Axis";

            if (isButton)
            {
                SimulateButton(action);
                RestResponse.Send(response,
                    $"{{\"success\":true,\"action\":\"{RestResponse.EscapeJson(actionName)}\",\"controlType\":\"Button\"}}");
            }
            else if (isVector2)
            {
                var valueToken = GetArrayToken(body, "value");
                float x = 0f, y = 0f;
                if (!string.IsNullOrEmpty(valueToken) && !TryParseVector2(valueToken, out x, out y))
                {
                    RestResponse.SendError(response, "Invalid value for Vector2. Expected [x, y].", 400);
                    return;
                }
                SimulateVector2(x, y);
                var xi = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var yi = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                RestResponse.Send(response,
                    $"{{\"success\":true,\"action\":\"{RestResponse.EscapeJson(actionName)}\",\"controlType\":\"Vector2\",\"value\":[{xi},{yi}]}}");
            }
            else if (isAxis)
            {
                var valueStr = RequestBodyReader.GetString(body, "value");
                float v = 0f;
                if (!string.IsNullOrEmpty(valueStr))
                    float.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v);
                SimulateAxis(v);
                var vi = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                RestResponse.Send(response,
                    $"{{\"success\":true,\"action\":\"{RestResponse.EscapeJson(actionName)}\",\"controlType\":\"Axis\",\"value\":{vi}}}");
            }
            else
            {
                RestResponse.SendError(response,
                    $"Unsupported control type: '{controlType}'. Supported types: Button, Vector2, Stick, Axis.", 400);
            }
        }

        /// <summary>
        /// Removes virtual devices from the Input System.
        /// Called by <see cref="PlayModeInputInit"/> on <c>ExitingPlayMode</c>.
        /// </summary>
        public static void Cleanup()
        {
            if (_virtualGamepad != null)
            {
                if (_virtualGamepad.added) InputSystem.RemoveDevice(_virtualGamepad);
                _virtualGamepad = null;
            }
            if (_virtualKeyboard != null)
            {
                if (_virtualKeyboard.added) InputSystem.RemoveDevice(_virtualKeyboard);
                _virtualKeyboard = null;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────────

        static IEnumerable<InputAction> CollectAllActions()
        {
            var seen   = new HashSet<string>();
            var result = new List<InputAction>();

            // ListEnabledActions covers actions that are currently active.
            try
            {
                foreach (var a in InputSystem.ListEnabledActions())
                {
                    var key = $"{a.actionMap?.name}/{a.name}";
                    if (seen.Add(key)) result.Add(a);
                }
            }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning($"[UnionAir] ListEnabledActions failed: {e.Message}"); }

            // PlayerInput components expose actions that may not be enabled yet.
            foreach (var pi in Object.FindObjectsByType<PlayerInput>(FindObjectsInactive.Exclude))
            {
                if (pi.actions == null) continue;
                foreach (var map in pi.actions.actionMaps)
                    foreach (var a in map.actions)
                    {
                        var key = $"{map.name}/{a.name}";
                        if (seen.Add(key)) result.Add(a);
                    }
            }

            return result;
        }

        static InputAction FindAction(string name)
        {
            foreach (var a in CollectAllActions())
                if (string.Equals(a.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return a;
            return null;
        }

        static Gamepad EnsureVirtualGamepad()
        {
            if (_virtualGamepad != null && _virtualGamepad.added)
                return _virtualGamepad;
            var existing = InputSystem.GetDevice("UnionAirVirtualGamepad") as Gamepad;
            _virtualGamepad = existing ?? InputSystem.AddDevice<Gamepad>("UnionAirVirtualGamepad");
            return _virtualGamepad;
        }

        static Keyboard EnsureVirtualKeyboard()
        {
            if (_virtualKeyboard != null && _virtualKeyboard.added)
                return _virtualKeyboard;
            var existing = InputSystem.GetDevice("UnionAirVirtualKeyboard") as Keyboard;
            _virtualKeyboard = existing ?? InputSystem.AddDevice<Keyboard>("UnionAirVirtualKeyboard");
            return _virtualKeyboard;
        }

        static void SimulateButton(InputAction action)
        {
            // Use a virtual Keyboard for keyboard-bound actions, Gamepad otherwise.
            bool prefersKeyboard = false;
            var  keyToPress      = Key.Space;

            foreach (var binding in action.bindings)
            {
                var path = binding.effectivePath;
                if (string.IsNullOrEmpty(path)) continue;
                if (path.StartsWith("<Keyboard>/"))
                {
                    prefersKeyboard = true;
                    var keyName = path.Substring("<Keyboard>/".Length);
                    if (!System.Enum.TryParse<Key>(keyName, ignoreCase: true, out keyToPress))
                        keyToPress = Key.Space;
                    break;
                }
                if (path.StartsWith("<Gamepad>/")) break;
            }

            if (prefersKeyboard)
            {
                var kb = EnsureVirtualKeyboard();
                InputSystem.QueueStateEvent(kb, new KeyboardState(keyToPress));
                InputSystem.Update();
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                InputSystem.Update();
            }
            else
            {
                var gp    = EnsureVirtualGamepad();
                var press = new GamepadState
                {
                    buttons = (ushort)(1 << (int)GamepadButton.South)
                };
                InputSystem.QueueStateEvent(gp, press);
                InputSystem.Update();
                InputSystem.QueueStateEvent(gp, new GamepadState());
                InputSystem.Update();
            }
        }

        static void SimulateVector2(float x, float y)
        {
            var gp = EnsureVirtualGamepad();
            InputSystem.QueueStateEvent(gp, new GamepadState { leftStick = new Vector2(x, y) });
            InputSystem.Update();
        }

        static void SimulateAxis(float value)
        {
            var gp = EnsureVirtualGamepad();
            InputSystem.QueueStateEvent(gp, new GamepadState { leftTrigger = value });
            InputSystem.Update();
        }

        /// <summary>Extracts a raw "[...]" array token from a JSON body for the given key.</summary>
        static string GetArrayToken(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var search = $"\"{key}\"";
            int idx = json.IndexOf(search, System.StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx + search.Length);
            if (colon < 0) return null;
            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            if (start >= json.Length || json[start] != '[') return null;
            int depth = 0, end = start;
            while (end < json.Length)
            {
                if      (json[end] == '[') depth++;
                else if (json[end] == ']') { depth--; if (depth == 0) break; }
                end++;
            }
            return json.Substring(start, end - start + 1);
        }

        static bool TryParseVector2(string token, out float x, out float y)
        {
            x = 0; y = 0;
            token = token.Trim();
            if (!token.StartsWith("[") || !token.EndsWith("]")) return false;
            var inner = token.Substring(1, token.Length - 2);
            var parts = inner.Split(',');
            if (parts.Length < 2) return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, inv, out x)
                && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, inv, out y);
        }
    }
}
