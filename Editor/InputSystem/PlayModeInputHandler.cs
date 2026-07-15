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
        static Mouse    _virtualMouse;
        static readonly Dictionary<Key, int> _heldKeys = new Dictionary<Key, int>();
        static readonly Dictionary<GamepadButton, int> _heldGamepadButtons = new Dictionary<GamepadButton, int>();
        static readonly Dictionary<MouseButton, int> _heldMouseButtons = new Dictionary<MouseButton, int>();
        static readonly Dictionary<string, List<HeldButton>> _heldButtonsByAction = new Dictionary<string, List<HeldButton>>();
        static GamepadState _gamepadState;
        static float _setLeftTrigger;
        static float _setRightTrigger;
        static Vector2 _mousePosition;
        static PointerSequence _activeSequence;

        const double PointerSequenceTimeoutSeconds = 5.0;

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
        /// Performs a Button <see cref="UnityEngine.InputSystem.InputAction"/> via a virtual device.
        /// The action is looked up by name (case-insensitive). Supported modes:
        /// <c>tap</c>, <c>press</c>, <c>release</c>.
        /// Responds with 409 when not in Play mode, 404 when the action is not found.
        /// </summary>
        public static void HandlePerform(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!EditorApplication.isPlaying)
            {
                RestResponse.SendError(response, "Not in Play mode.", 409);
                return;
            }
            if (!EnsureNoActiveSequence(response)) return;

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

            if (!isButton)
            {
                RestResponse.SendError(response,
                    $"Control type '{controlType}' uses POST /api/playmode/input/set. POST /api/playmode/input/perform is for Button actions.", 400);
                return;
            }

            if (RequestBodyReader.GetString(body, "value") != null)
            {
                RestResponse.SendError(response, "Button perform uses 'mode', not 'value'.", 400);
                return;
            }

            var mode = (RequestBodyReader.GetString(body, "mode") ?? "tap").ToLowerInvariant();
            if (mode == "tap")
            {
                if (!TryPressFirstSupportedButton(action, out var pressed, out var target, out var error))
                {
                    RestResponse.SendError(response, error, 422);
                    return;
                }
                ReleaseButton(target);

                RestResponse.Send(response,
                    $"{{\"success\":true,\"action\":\"{RestResponse.EscapeJson(actionName)}\",\"controlType\":\"Button\",\"mode\":\"tap\",\"pressedBinding\":\"{RestResponse.EscapeJson(pressed.BindingPath)}\",\"pressedControl\":\"{RestResponse.EscapeJson(pressed.ControlPath)}\",\"releasedControl\":\"{RestResponse.EscapeJson(pressed.ControlPath)}\"}}");
            }
            else if (mode == "press")
            {
                if (!TryPressFirstSupportedButton(action, out var pressed, out var target, out var error))
                {
                    RestResponse.SendError(response, error, 422);
                    return;
                }
                AddHeldButton(action, pressed, target);

                RestResponse.Send(response,
                    $"{{\"success\":true,\"action\":\"{RestResponse.EscapeJson(actionName)}\",\"controlType\":\"Button\",\"mode\":\"press\",\"pressedBinding\":\"{RestResponse.EscapeJson(pressed.BindingPath)}\",\"pressedControl\":\"{RestResponse.EscapeJson(pressed.ControlPath)}\"}}");
            }
            else if (mode == "release")
            {
                var released = ReleaseHeldButtons(action);
                RestResponse.Send(response,
                    $"{{\"success\":true,\"action\":\"{RestResponse.EscapeJson(actionName)}\",\"controlType\":\"Button\",\"mode\":\"release\",\"releasedControls\":[{JoinQuoted(released)}]}}");
            }
            else
            {
                RestResponse.SendError(response, "Invalid mode. Expected tap, press, or release.", 400);
            }
        }

        /// <summary>
        /// Sets an Axis, Vector2, or Stick <see cref="UnityEngine.InputSystem.InputAction"/>
        /// value on a virtual device. Values remain active until the next set or cleanup.
        /// </summary>
        public static void HandleSet(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!EditorApplication.isPlaying)
            {
                RestResponse.SendError(response, "Not in Play mode.", 409);
                return;
            }
            if (!EnsureNoActiveSequence(response)) return;

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
                RestResponse.SendError(response,
                    "Button actions use POST /api/playmode/input/perform.", 400);
                return;
            }

            if (isVector2)
            {
                var valueToken = GetArrayToken(body, "value");
                if (string.IsNullOrEmpty(valueToken) || !TryParseVector2(valueToken, out var x, out var y))
                {
                    RestResponse.SendError(response, "Invalid value for Vector2. Expected [x, y].", 400);
                    return;
                }

                if (!TrySetVector2(action, x, y, out var result, out var error))
                {
                    RestResponse.SendError(response, error, 422);
                    return;
                }

                var xi = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var yi = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                RestResponse.Send(response,
                    $"{{\"success\":true,\"action\":\"{RestResponse.EscapeJson(actionName)}\",\"controlType\":\"Vector2\",\"value\":[{xi},{yi}],\"setBinding\":\"{RestResponse.EscapeJson(result.BindingPath)}\",\"setControl\":\"{RestResponse.EscapeJson(result.ControlPath)}\"}}");
            }
            else if (isAxis)
            {
                if (!TryParseRequiredFloat(body, "value", out var value))
                {
                    RestResponse.SendError(response, "Invalid value for Axis. Expected a finite number.", 400);
                    return;
                }

                if (!TrySetAxis(action, value, out var result, out var error))
                {
                    RestResponse.SendError(response, error, 422);
                    return;
                }

                var vi = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                RestResponse.Send(response,
                    $"{{\"success\":true,\"action\":\"{RestResponse.EscapeJson(actionName)}\",\"controlType\":\"Axis\",\"value\":{vi},\"setBinding\":\"{RestResponse.EscapeJson(result.BindingPath)}\",\"setControl\":\"{RestResponse.EscapeJson(result.ControlPath)}\"}}");
            }
            else
            {
                RestResponse.SendError(response,
                    $"Unsupported control type: '{controlType}'. Supported set types: Vector2, Stick, Axis.", 400);
            }
        }

        /// <summary>
        /// Simulates a mouse click, press, release, or move at a screen coordinate through
        /// the virtual mouse, spreading the phases across real player-loop frames so the
        /// running game observes them like genuine input (raycast-based hit detection included).
        /// The response is deferred and sent after the final phase has been consumed.
        /// </summary>
        public static void HandlePointer(UnionAirRequestContext ctx)
        {
            var response = ctx.Response;
            if (!EditorApplication.isPlaying)
            {
                RestResponse.SendError(response, "Not in Play mode.", 409);
                return;
            }
            if (EditorApplication.isPaused)
            {
                RestResponse.SendError(response, "The editor is paused; player frames are not advancing.", 409);
                return;
            }
            if (_activeSequence != null)
            {
                RestResponse.SendError(response, "Another pointer operation is in progress.", 409);
                return;
            }

            var body = RequestBodyReader.ReadString(ctx.Request);

            var mode = (RequestBodyReader.GetString(body, "mode") ?? "tap").ToLowerInvariant();
            if (mode != "tap" && mode != "press" && mode != "release" && mode != "move")
            {
                RestResponse.SendError(response, "Invalid mode. Expected tap, press, release, or move.", 400);
                return;
            }

            var buttonName = (RequestBodyReader.GetString(body, "button") ?? "left").ToLowerInvariant();
            MouseButton button;
            switch (buttonName)
            {
                case "left": button = MouseButton.Left; break;
                case "right": button = MouseButton.Right; break;
                case "middle": button = MouseButton.Middle; break;
                default:
                    RestResponse.SendError(response, "Invalid button. Expected left, right, or middle.", 400);
                    return;
            }

            var holdFramesPresent = RequestBodyReader.GetString(body, "holdFrames") != null;
            var holdFrames = RequestBodyReader.GetInt(body, "holdFrames");
            if (holdFramesPresent)
            {
                if (mode != "tap")
                {
                    RestResponse.SendError(response, "holdFrames is only valid with mode tap.", 400);
                    return;
                }
                if (!holdFrames.HasValue || holdFrames.Value < 1 || holdFrames.Value > 300)
                {
                    RestResponse.SendError(response, "holdFrames must be an integer between 1 and 300.", 400);
                    return;
                }
            }

            var hasPositionField =
                RequestBodyReader.GetObject(body, "position") != null ||
                RequestBodyReader.GetObject(body, "normalizedPosition") != null;
            Vector2 position;
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            if (mode != "release" || hasPositionField)
            {
                if (!ScreenPointUtils.TryResolve(body, out position, out screenWidth, out screenHeight,
                        out var error, out var statusCode))
                {
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }
            }
            else
            {
                position = _mousePosition;
            }

            var seq = new PointerSequence
            {
                Response = response,
                Mode = mode,
                Button = button,
                ButtonName = buttonName,
                Position = position,
                ScreenWidth = screenWidth,
                ScreenHeight = screenHeight,
                HoldFrames = holdFrames ?? 1,
                Phase = 0,
                PhaseQueuedFrame = Time.frameCount,
                FramesToWait = 1,
                LastObservedFrame = Time.frameCount,
                Deadline = EditorApplication.timeSinceStartup + PointerSequenceTimeoutSeconds
            };

            // Phase 0 runs on the request frame.
            if (mode == "release")
            {
                seq.Released = _heldMouseButtons.ContainsKey(button);
                _mousePosition = position;
                Decrement(_heldMouseButtons, button);
                QueueMouseStateNoUpdate();
                seq.ReleaseFrame = Time.frameCount;
            }
            else
            {
                // Move first so the position is observable before any press
                // (PhysicsRaycaster and pointer actions raycast at this point).
                _mousePosition = position;
                QueueMouseStateNoUpdate();
            }

            ctx.Defer();
            _activeSequence = seq;
            EditorApplication.update += PumpSequence;
        }

        static void PumpSequence()
        {
            var seq = _activeSequence;
            if (seq == null)
            {
                EditorApplication.update -= PumpSequence;
                return;
            }

            // The deadline detects stalled player frames, not slow ones: refresh it
            // whenever a frame advances so a long holdFrames at a low frame rate does
            // not get mistaken for a stall.
            if (Time.frameCount != seq.LastObservedFrame)
            {
                seq.LastObservedFrame = Time.frameCount;
                seq.Deadline = EditorApplication.timeSinceStartup + PointerSequenceTimeoutSeconds;
            }
            else if (EditorApplication.timeSinceStartup > seq.Deadline)
            {
                AbortSequence(
                    "Timed out waiting for player frames to advance. Focus the Game view or check the Input System package's Background Behavior setting.",
                    500);
                return;
            }

            if (Time.frameCount - seq.PhaseQueuedFrame < seq.FramesToWait)
                return;

            switch (seq.Mode)
            {
                case "tap":
                    if (seq.Phase == 0)
                    {
                        Increment(_heldMouseButtons, seq.Button);
                        seq.PressOutstanding = true;
                        QueueMouseStateNoUpdate();
                        seq.PressFrame = Time.frameCount;
                        AdvancePhase(seq, seq.HoldFrames);
                    }
                    else if (seq.Phase == 1)
                    {
                        Decrement(_heldMouseButtons, seq.Button);
                        seq.PressOutstanding = false;
                        QueueMouseStateNoUpdate();
                        seq.ReleaseFrame = Time.frameCount;
                        AdvancePhase(seq, 1);
                    }
                    else
                    {
                        CompleteSequence(
                            $"{{\"success\":true,\"mode\":\"tap\",\"button\":\"{seq.ButtonName}\",{PositionJson(seq)},\"pressFrame\":{seq.PressFrame},\"releaseFrame\":{seq.ReleaseFrame}}}");
                    }
                    break;

                case "press":
                    if (seq.Phase == 0)
                    {
                        Increment(_heldMouseButtons, seq.Button);
                        // A completed press intentionally leaves the button held, so this
                        // is not treated as outstanding once the sequence responds.
                        QueueMouseStateNoUpdate();
                        seq.PressFrame = Time.frameCount;
                        AdvancePhase(seq, 1);
                    }
                    else
                    {
                        CompleteSequence(
                            $"{{\"success\":true,\"mode\":\"press\",\"button\":\"{seq.ButtonName}\",{PositionJson(seq)},\"pressFrame\":{seq.PressFrame}}}");
                    }
                    break;

                case "release":
                    CompleteSequence(
                        $"{{\"success\":true,\"mode\":\"release\",\"button\":\"{seq.ButtonName}\",{PositionJson(seq)},\"releaseFrame\":{seq.ReleaseFrame},\"released\":{RestResponse.FormatBool(seq.Released)}}}");
                    break;

                default: // move
                    CompleteSequence(
                        $"{{\"success\":true,\"mode\":\"move\",{PositionJson(seq)}}}");
                    break;
            }
        }

        // A pointer sequence relies on the player loop consuming its queued mouse events;
        // perform/set call InputSystem.Update() directly, which would flush those events
        // outside the player loop and make the game miss the click. Reject until it finishes.
        static bool EnsureNoActiveSequence(HttpListenerResponse response)
        {
            if (_activeSequence == null) return true;
            RestResponse.SendError(response, "A pointer operation is in progress.", 409);
            return false;
        }

        static void AdvancePhase(PointerSequence seq, int framesToWait)
        {
            seq.Phase++;
            seq.PhaseQueuedFrame = Time.frameCount;
            seq.FramesToWait = framesToWait;
        }

        static string PositionJson(PointerSequence seq)
            => $"\"position\":{{\"x\":{RestResponse.FormatFloat(seq.Position.x)},\"y\":{RestResponse.FormatFloat(seq.Position.y)}}}," +
               $"\"screenSize\":{{\"width\":{seq.ScreenWidth},\"height\":{seq.ScreenHeight}}}";

        static void CompleteSequence(string json, int statusCode = 200)
        {
            var seq = _activeSequence;
            _activeSequence = null;
            EditorApplication.update -= PumpSequence;
            if (seq == null) return;
            try { RestResponse.Send(seq.Response, json, statusCode); } catch { /* client may have disconnected */ }
            try { seq.Response.Close(); } catch { /* ignored */ }
        }

        // Aborts the active sequence without leaving virtual input state behind:
        // any press the sequence had queued but not yet released is released first,
        // so an interrupted tap does not leave the mouse button stuck down.
        static void AbortSequence(string message, int statusCode)
        {
            var seq = _activeSequence;
            if (seq != null && seq.PressOutstanding)
            {
                Decrement(_heldMouseButtons, seq.Button);
                seq.PressOutstanding = false;
                QueueMouseStateNoUpdate();
            }
            CompleteSequence($"{{\"error\":\"{RestResponse.EscapeJson(message)}\"}}", statusCode);
        }

        sealed class PointerSequence
        {
            public HttpListenerResponse Response;
            public string Mode;
            public MouseButton Button;
            public string ButtonName;
            public Vector2 Position;
            public int ScreenWidth;
            public int ScreenHeight;
            public int HoldFrames;
            public int Phase;
            public int PhaseQueuedFrame;
            public int FramesToWait;
            public int LastObservedFrame;
            public double Deadline;
            public int PressFrame;
            public int ReleaseFrame;
            public bool Released;
            public bool PressOutstanding;
        }

        /// <summary>
        /// Aborts any in-flight pointer sequence, answering its deferred HTTP response so
        /// the client is not left hanging. Called by <see cref="PlayModeInputInit"/> before
        /// a domain reload, which would otherwise wipe the sequence state and its update hook.
        /// </summary>
        public static void AbortActiveSequence()
        {
            if (_activeSequence != null)
                AbortSequence("The pointer sequence was interrupted by a domain reload.", 503);
        }

        /// <summary>
        /// Removes virtual devices from the Input System.
        /// Called by <see cref="PlayModeInputInit"/> on <c>ExitingPlayMode</c>.
        /// </summary>
        public static void Cleanup()
        {
            ResetVirtualInputState();
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
            if (_virtualMouse != null)
            {
                if (_virtualMouse.added) InputSystem.RemoveDevice(_virtualMouse);
                _virtualMouse = null;
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
            foreach (var pi in Object.FindObjectsByType<PlayerInput>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
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

        static Mouse EnsureVirtualMouse()
        {
            if (_virtualMouse != null && _virtualMouse.added)
                return _virtualMouse;
            var existing = InputSystem.GetDevice("UnionAirVirtualMouse") as Mouse;
            _virtualMouse = existing ?? InputSystem.AddDevice<Mouse>("UnionAirVirtualMouse");
            return _virtualMouse;
        }

        static bool TryPressFirstSupportedButton(
            InputAction action,
            out InputSimulationResult result,
            out ButtonTarget target,
            out string error)
        {
            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite) continue;
                var path = binding.effectivePath;
                if (string.IsNullOrEmpty(path)) continue;

                if (TryResolveButtonBinding(path, out target))
                {
                    PressButton(target);
                    result = new InputSimulationResult(path, target.ControlPath);
                    error = null;
                    return true;
                }
            }

            result = default(InputSimulationResult);
            target = default(ButtonTarget);
            error = "No supported Button binding found. Supported virtual button bindings: Keyboard, Gamepad, Mouse, Pointer press.";
            return false;
        }

        static bool TryResolveButtonBinding(string bindingPath, out ButtonTarget target)
        {
            target = default(ButtonTarget);
            if (!TryParseDeviceControlPath(bindingPath, out var deviceName, out var controlPath))
                return false;

            switch (deviceName.ToLowerInvariant())
            {
                case "keyboard":
                    if (!System.Enum.TryParse<Key>(controlPath, true, out var key))
                        return false;
                    target = ButtonTarget.Keyboard(key, controlPath);
                    return true;
                case "gamepad":
                    if (!TryParseGamepadButton(controlPath, out var gamepadButton))
                        return false;
                    target = ButtonTarget.Gamepad(gamepadButton, controlPath);
                    return true;
                case "mouse":
                    if (!TryParseMouseButton(controlPath, out var mouseButton))
                        return false;
                    target = ButtonTarget.Mouse(mouseButton, controlPath);
                    return true;
                case "pointer":
                    if (!ControlPathEquals(controlPath, "press"))
                        return false;
                    target = ButtonTarget.Mouse(MouseButton.Left, "leftButton");
                    return true;
                default:
                    return false;
            }
        }

        static bool TryParseDeviceControlPath(string path, out string deviceName, out string controlPath)
        {
            deviceName = null;
            controlPath = null;

            if (string.IsNullOrEmpty(path) || path[0] != '<') return false;

            int deviceEnd = path.IndexOf('>');
            if (deviceEnd <= 1 || deviceEnd + 2 > path.Length || path[deviceEnd + 1] != '/') return false;

            deviceName = path.Substring(1, deviceEnd - 1);
            controlPath = path.Substring(deviceEnd + 2);
            return !string.IsNullOrEmpty(controlPath);
        }

        static bool TrySetVector2(
            InputAction action,
            float x,
            float y,
            out InputSimulationResult result,
            out string error)
        {
            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite) continue;
                var path = binding.effectivePath;
                if (string.IsNullOrEmpty(path)) continue;

                if (TryParseDeviceControlPath(path, out var deviceName, out var controlPath) &&
                    deviceName.ToLowerInvariant() == "gamepad")
                {
                    if (ControlPathEquals(controlPath, "leftStick"))
                    {
                        _gamepadState.leftStick = new Vector2(x, y);
                        QueueGamepadState();
                        result = new InputSimulationResult(path, "/UnionAirVirtualGamepad/leftStick");
                        error = null;
                        return true;
                    }
                    if (ControlPathEquals(controlPath, "rightStick"))
                    {
                        _gamepadState.rightStick = new Vector2(x, y);
                        QueueGamepadState();
                        result = new InputSimulationResult(path, "/UnionAirVirtualGamepad/rightStick");
                        error = null;
                        return true;
                    }
                }
            }

            result = default(InputSimulationResult);
            error = "No supported Vector2/Stick binding found. Supported set bindings: <Gamepad>/leftStick, <Gamepad>/rightStick.";
            return false;
        }

        static bool TrySetAxis(
            InputAction action,
            float value,
            out InputSimulationResult result,
            out string error)
        {
            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite) continue;
                var path = binding.effectivePath;
                if (string.IsNullOrEmpty(path)) continue;

                if (TryParseDeviceControlPath(path, out var deviceName, out var controlPath) &&
                    deviceName.ToLowerInvariant() == "gamepad")
                {
                    if (ControlPathEquals(controlPath, "leftTrigger"))
                    {
                        _setLeftTrigger = value;
                        QueueGamepadState();
                        result = new InputSimulationResult(path, "/UnionAirVirtualGamepad/leftTrigger");
                        error = null;
                        return true;
                    }
                    if (ControlPathEquals(controlPath, "rightTrigger"))
                    {
                        _setRightTrigger = value;
                        QueueGamepadState();
                        result = new InputSimulationResult(path, "/UnionAirVirtualGamepad/rightTrigger");
                        error = null;
                        return true;
                    }
                    if (ControlPathEquals(controlPath, "leftStick/x"))
                    {
                        _gamepadState.leftStick = new Vector2(value, _gamepadState.leftStick.y);
                        QueueGamepadState();
                        result = new InputSimulationResult(path, "/UnionAirVirtualGamepad/leftStick/x");
                        error = null;
                        return true;
                    }
                    if (ControlPathEquals(controlPath, "leftStick/y"))
                    {
                        _gamepadState.leftStick = new Vector2(_gamepadState.leftStick.x, value);
                        QueueGamepadState();
                        result = new InputSimulationResult(path, "/UnionAirVirtualGamepad/leftStick/y");
                        error = null;
                        return true;
                    }
                    if (ControlPathEquals(controlPath, "rightStick/x"))
                    {
                        _gamepadState.rightStick = new Vector2(value, _gamepadState.rightStick.y);
                        QueueGamepadState();
                        result = new InputSimulationResult(path, "/UnionAirVirtualGamepad/rightStick/x");
                        error = null;
                        return true;
                    }
                    if (ControlPathEquals(controlPath, "rightStick/y"))
                    {
                        _gamepadState.rightStick = new Vector2(_gamepadState.rightStick.x, value);
                        QueueGamepadState();
                        result = new InputSimulationResult(path, "/UnionAirVirtualGamepad/rightStick/y");
                        error = null;
                        return true;
                    }
                }
            }

            result = default(InputSimulationResult);
            error = "No supported Axis binding found. Supported set bindings: <Gamepad>/leftTrigger, <Gamepad>/rightTrigger, and Gamepad stick x/y axes.";
            return false;
        }

        static void PressButton(ButtonTarget target)
        {
            switch (target.Device)
            {
                case VirtualButtonDevice.Keyboard:
                    Increment(_heldKeys, target.Key);
                    QueueKeyboardState();
                    break;
                case VirtualButtonDevice.Gamepad:
                    Increment(_heldGamepadButtons, target.GamepadButton);
                    QueueGamepadState();
                    break;
                case VirtualButtonDevice.Mouse:
                    Increment(_heldMouseButtons, target.MouseButton);
                    QueueMouseState();
                    break;
            }
        }

        static void ReleaseButton(ButtonTarget target)
        {
            switch (target.Device)
            {
                case VirtualButtonDevice.Keyboard:
                    Decrement(_heldKeys, target.Key);
                    QueueKeyboardState();
                    break;
                case VirtualButtonDevice.Gamepad:
                    Decrement(_heldGamepadButtons, target.GamepadButton);
                    QueueGamepadState();
                    break;
                case VirtualButtonDevice.Mouse:
                    Decrement(_heldMouseButtons, target.MouseButton);
                    QueueMouseState();
                    break;
            }
        }

        static void QueueKeyboardState()
        {
            var kb = EnsureVirtualKeyboard();
            var keys = new Key[_heldKeys.Count];
            _heldKeys.Keys.CopyTo(keys, 0);
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys));
            InputSystem.Update();
        }

        static void QueueGamepadState()
        {
            var gp = EnsureVirtualGamepad();
            ushort buttons = 0;
            bool leftTriggerHeld = false;
            bool rightTriggerHeld = false;
            foreach (var button in _heldGamepadButtons.Keys)
            {
                if (button == GamepadButton.LeftTrigger)
                {
                    leftTriggerHeld = true;
                    continue;
                }
                if (button == GamepadButton.RightTrigger)
                {
                    rightTriggerHeld = true;
                    continue;
                }
                buttons |= (ushort)(1 << (int)button);
            }
            _gamepadState.buttons = buttons;
            _gamepadState.leftTrigger = leftTriggerHeld ? 1f : _setLeftTrigger;
            _gamepadState.rightTrigger = rightTriggerHeld ? 1f : _setRightTrigger;
            InputSystem.QueueStateEvent(gp, _gamepadState);
            InputSystem.Update();
        }

        static void QueueMouseState()
        {
            QueueMouseStateNoUpdate();
            InputSystem.Update();
        }

        // The pointer sequence relies on the player loop's own input update so that
        // presses are observable via wasPressedThisFrame; it must not call InputSystem.Update().
        static void QueueMouseStateNoUpdate()
        {
            var mouse = EnsureVirtualMouse();
            ushort buttons = 0;
            foreach (var button in _heldMouseButtons.Keys)
                buttons |= (ushort)(1 << (int)button);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = buttons, position = _mousePosition });
        }

        static void Increment<T>(Dictionary<T, int> counts, T key)
        {
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        static void Decrement<T>(Dictionary<T, int> counts, T key)
        {
            if (!counts.TryGetValue(key, out var count)) return;
            if (count <= 1)
                counts.Remove(key);
            else
                counts[key] = count - 1;
        }

        static void AddHeldButton(InputAction action, InputSimulationResult result, ButtonTarget target)
        {
            var key = GetActionKey(action);
            if (!_heldButtonsByAction.TryGetValue(key, out var held))
            {
                held = new List<HeldButton>();
                _heldButtonsByAction[key] = held;
            }
            held.Add(new HeldButton(result.ControlPath, target));
        }

        static List<string> ReleaseHeldButtons(InputAction action)
        {
            var key = GetActionKey(action);
            var released = new List<string>();
            if (!_heldButtonsByAction.TryGetValue(key, out var held)) return released;

            foreach (var button in held)
            {
                ReleaseButton(button.Target);
                released.Add(button.ControlPath);
            }

            _heldButtonsByAction.Remove(key);
            return released;
        }

        static void ResetVirtualInputState()
        {
            if (_activeSequence != null)
                AbortSequence("Play mode ended during the pointer sequence.", 409);

            _mousePosition = default(Vector2);
            _heldKeys.Clear();
            _heldGamepadButtons.Clear();
            _heldMouseButtons.Clear();
            _heldButtonsByAction.Clear();
            _gamepadState = new GamepadState();
            _setLeftTrigger = 0f;
            _setRightTrigger = 0f;

            if (_virtualKeyboard != null && _virtualKeyboard.added)
            {
                InputSystem.QueueStateEvent(_virtualKeyboard, new KeyboardState());
                InputSystem.Update();
            }
            if (_virtualGamepad != null && _virtualGamepad.added)
            {
                InputSystem.QueueStateEvent(_virtualGamepad, new GamepadState());
                InputSystem.Update();
            }
            if (_virtualMouse != null && _virtualMouse.added)
            {
                InputSystem.QueueStateEvent(_virtualMouse, new MouseState());
                InputSystem.Update();
            }
        }

        static string GetActionKey(InputAction action)
            => action.id.ToString();

        static string JoinQuoted(List<string> values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(RestResponse.EscapeJson(values[i])).Append("\"");
            }
            return sb.ToString();
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
                && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, inv, out y)
                && IsFinite(x)
                && IsFinite(y);
        }

        static bool TryParseRequiredFloat(string json, string key, out float value)
        {
            var valueStr = RequestBodyReader.GetString(json, key);
            if (string.IsNullOrEmpty(valueStr))
            {
                value = 0f;
                return false;
            }

            return float.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value)
                && !float.IsInfinity(value);
        }

        static bool TryParseGamepadButton(string controlPath, out GamepadButton button)
        {
            switch (controlPath.ToLowerInvariant())
            {
                case "buttonsouth": button = GamepadButton.South; return true;
                case "buttoneast": button = GamepadButton.East; return true;
                case "buttonwest": button = GamepadButton.West; return true;
                case "buttonnorth": button = GamepadButton.North; return true;
                case "leftshoulder": button = GamepadButton.LeftShoulder; return true;
                case "rightshoulder": button = GamepadButton.RightShoulder; return true;
                case "lefttrigger": button = GamepadButton.LeftTrigger; return true;
                case "righttrigger": button = GamepadButton.RightTrigger; return true;
                case "leftstickpress": button = GamepadButton.LeftStick; return true;
                case "rightstickpress": button = GamepadButton.RightStick; return true;
                case "start": button = GamepadButton.Start; return true;
                case "select": button = GamepadButton.Select; return true;
                case "dpad/up": button = GamepadButton.DpadUp; return true;
                case "dpad/down": button = GamepadButton.DpadDown; return true;
                case "dpad/left": button = GamepadButton.DpadLeft; return true;
                case "dpad/right": button = GamepadButton.DpadRight; return true;
                default: button = default(GamepadButton); return false;
            }
        }

        static bool TryParseMouseButton(string controlPath, out MouseButton button)
        {
            switch (controlPath.ToLowerInvariant())
            {
                case "leftbutton": button = MouseButton.Left; return true;
                case "rightbutton": button = MouseButton.Right; return true;
                case "middlebutton": button = MouseButton.Middle; return true;
                case "forwardbutton": button = MouseButton.Forward; return true;
                case "backbutton": button = MouseButton.Back; return true;
                default: button = default(MouseButton); return false;
            }
        }

        static bool ControlPathEquals(string actual, string expected)
            => string.Equals(actual, expected, System.StringComparison.OrdinalIgnoreCase);

        static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        enum VirtualButtonDevice
        {
            Keyboard,
            Gamepad,
            Mouse
        }

        readonly struct ButtonTarget
        {
            private ButtonTarget(VirtualButtonDevice device, Key key, GamepadButton gamepadButton, MouseButton mouseButton, string controlName)
            {
                Device = device;
                Key = key;
                GamepadButton = gamepadButton;
                MouseButton = mouseButton;
                ControlName = controlName;
            }

            public VirtualButtonDevice Device { get; }
            public Key Key { get; }
            public GamepadButton GamepadButton { get; }
            public MouseButton MouseButton { get; }
            public string ControlName { get; }
            public string ControlPath
            {
                get
                {
                    switch (Device)
                    {
                        case VirtualButtonDevice.Keyboard:
                            return $"/UnionAirVirtualKeyboard/{ControlName}";
                        case VirtualButtonDevice.Gamepad:
                            return $"/UnionAirVirtualGamepad/{ControlName}";
                        case VirtualButtonDevice.Mouse:
                            return $"/UnionAirVirtualMouse/{ControlName}";
                        default:
                            return "";
                    }
                }
            }

            public static ButtonTarget Keyboard(Key key, string controlName)
                => new ButtonTarget(VirtualButtonDevice.Keyboard, key, default(GamepadButton), default(MouseButton), controlName);

            public static ButtonTarget Gamepad(GamepadButton button, string controlName)
                => new ButtonTarget(VirtualButtonDevice.Gamepad, default(Key), button, default(MouseButton), controlName);

            public static ButtonTarget Mouse(MouseButton button, string controlName)
                => new ButtonTarget(VirtualButtonDevice.Mouse, default(Key), default(GamepadButton), button, controlName);
        }

        readonly struct InputSimulationResult
        {
            public InputSimulationResult(string bindingPath, string controlPath)
            {
                BindingPath = bindingPath;
                ControlPath = controlPath;
            }

            public string BindingPath { get; }
            public string ControlPath { get; }
        }

        readonly struct HeldButton
        {
            public HeldButton(string controlPath, ButtonTarget target)
            {
                ControlPath = controlPath;
                Target = target;
            }

            public string ControlPath { get; }
            public ButtonTarget Target { get; }
        }
    }
}
