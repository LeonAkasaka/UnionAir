using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Applies scheduled replay events to the same virtual devices the immediate input endpoints
    /// drive.
    /// </summary>
    /// <remarks>
    /// This half of the class exists so the replay shares one set of held-input dictionaries and
    /// one set of binding-resolution rules with <c>perform</c>, <c>set</c>, and <c>pointer</c>.
    /// A replay press is therefore releasable by <c>POST /api/playmode/input/perform</c> with mode
    /// release, and vice versa.
    /// </remarks>
    internal static partial class PlayModeInputHandler
    {
        /// <summary>Whether a pointer sequence currently owns the virtual devices.</summary>
        internal static bool HasActivePointerSequence => _activeSequence != null;

        /// <summary>
        /// Applies every event due on one frame and queues a single snapshot per touched device.
        /// </summary>
        /// <remarks>
        /// The whole point of applying the frame's events together is the single flush at the end:
        /// each snapshot is built from the current dictionaries, so simultaneous presses merge
        /// into one device state instead of a sequence in which the last one wins. The flush must
        /// not call <c>InputSystem.Update()</c> — this runs inside the player loop's own input
        /// update, and flushing out of band is exactly what would break the frame timing.
        /// </remarks>
        internal static void ApplyReplayFrame(
            IList<InputReplayEventSpec> inputs,
            List<InputReplayDueEvent> due,
            int relativeFrame,
            int unityFrame)
        {
            var mask = VirtualDeviceMask.None;

            foreach (var item in due)
            {
                if (item.Index < 0 || item.Index >= inputs.Count) continue;

                string control;
                string error;
                mask |= ApplyReplayEvent(inputs[item.Index], out control, out error);
                InputReplayService.ReportApplied(
                    item.Index, relativeFrame, unityFrame, item.Late, control, error);
            }

            FlushDevices(mask, false);
        }

        /// <summary>
        /// Releases everything the virtual devices currently hold, for an aborted replay that
        /// would otherwise leave input stuck down.
        /// </summary>
        internal static void ReleaseAllHeldInput()
        {
            var mask = VirtualDeviceMask.None;
            if (_heldKeys.Count > 0) { _heldKeys.Clear(); mask |= VirtualDeviceMask.Keyboard; }
            if (_heldGamepadButtons.Count > 0) { _heldGamepadButtons.Clear(); mask |= VirtualDeviceMask.Gamepad; }
            if (_heldMouseButtons.Count > 0) { _heldMouseButtons.Clear(); mask |= VirtualDeviceMask.Mouse; }
            _heldButtonsByAction.Clear();

            FlushDevices(mask, true);
        }

        // ── Per-event application ───────────────────────────────────────────

        static VirtualDeviceMask ApplyReplayEvent(
            InputReplayEventSpec spec, out string control, out string error)
        {
            control = "";
            error = null;

            switch (spec.type)
            {
                case InputReplayEventType.Perform: return ApplyReplayPerform(spec, out control, out error);
                case InputReplayEventType.Set:     return ApplyReplaySet(spec, out control, out error);
                case InputReplayEventType.Pointer: return ApplyReplayPointer(spec, out control, out error);
                default:
                    error = $"Unknown event type '{spec.type}'.";
                    return VirtualDeviceMask.None;
            }
        }

        static VirtualDeviceMask ApplyReplayPerform(
            InputReplayEventSpec spec, out string control, out string error)
        {
            control = "";

            var action = FindAction(spec.action, out var candidates);
            if (action == null)
            {
                error = DescribeActionLookupFailure(spec.action, candidates);
                return VirtualDeviceMask.None;
            }

            var controlType = action.expectedControlType ?? "";
            if (action.type != InputActionType.Button && controlType != "Button")
            {
                error = $"Action '{spec.action}' has control type '{controlType}'; type 'perform' is for Button actions.";
                return VirtualDeviceMask.None;
            }

            if (spec.mode == "release")
            {
                List<string> released;
                var releaseMask = MutateReleaseHeldButtons(action, out released);
                control = released.Count > 0 ? string.Join(", ", released.ToArray()) : "";
                error = null;
                return releaseMask;
            }

            InputSimulationResult resolved;
            ButtonTarget target;
            if (!TryResolveFirstSupportedButton(action, out resolved, out target, out error))
                return VirtualDeviceMask.None;

            var mask = MutateButton(target, true);
            AddHeldButton(action, resolved, target);
            control = resolved.ControlPath;
            error = null;
            return mask;
        }

        static VirtualDeviceMask ApplyReplaySet(
            InputReplayEventSpec spec, out string control, out string error)
        {
            control = "";

            var action = FindAction(spec.action, out var candidates);
            if (action == null)
            {
                error = DescribeActionLookupFailure(spec.action, candidates);
                return VirtualDeviceMask.None;
            }

            var controlType = action.expectedControlType ?? "";
            var isVector2 = controlType == "Vector2" || controlType == "Stick";
            var isAxis = controlType == "Axis";

            InputSimulationResult resolved;
            VirtualDeviceMask mask;

            if (isVector2)
            {
                if (spec.valueKind != InputReplayValueKind.Vector2)
                {
                    error = $"Action '{spec.action}' has control type '{controlType}' and needs a [x, y] value.";
                    return VirtualDeviceMask.None;
                }
                if (!TrySetVector2(action, spec.valueX, spec.valueY, out resolved, out mask, out error))
                    return VirtualDeviceMask.None;
            }
            else if (isAxis)
            {
                if (spec.valueKind != InputReplayValueKind.Scalar)
                {
                    error = $"Action '{spec.action}' has control type 'Axis' and needs a single number value.";
                    return VirtualDeviceMask.None;
                }
                if (!TrySetAxis(action, spec.valueX, out resolved, out mask, out error))
                    return VirtualDeviceMask.None;
            }
            else
            {
                error = $"Unsupported control type '{controlType}'. Supported set types: Vector2, Stick, Axis.";
                return VirtualDeviceMask.None;
            }

            control = resolved.ControlPath;
            error = null;
            return mask;
        }

        static VirtualDeviceMask ApplyReplayPointer(
            InputReplayEventSpec spec, out string control, out string error)
        {
            control = "";
            error = null;

            if (spec.pointKind != InputReplayPointKind.None)
            {
                var point = new ScreenPointRequest(
                    spec.pointKind == InputReplayPointKind.Normalized,
                    spec.originTopLeft,
                    spec.pointX,
                    spec.pointY);

                Vector2 resolved;
                int statusCode;
                if (!ScreenPointUtils.Resolve(point, Screen.width, Screen.height,
                        out resolved, out error, out statusCode))
                    return VirtualDeviceMask.None;

                _mousePosition = resolved;
            }

            if (spec.mode == "move")
            {
                control = "/UnionAirVirtualMouse/position";
                return VirtualDeviceMask.Mouse;
            }

            MouseButton button;
            if (!TryParseReplayMouseButton(spec.button, out button))
            {
                error = $"Invalid button '{spec.button}'.";
                return VirtualDeviceMask.None;
            }

            if (spec.mode == "press") Increment(_heldMouseButtons, button);
            else Decrement(_heldMouseButtons, button);

            control = "/UnionAirVirtualMouse/" + MouseButtonControlName(button);
            return VirtualDeviceMask.Mouse;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        static string DescribeActionLookupFailure(string identifier, List<string> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return $"Action not found: '{identifier}'";

            return $"Action name is ambiguous: '{identifier}'. Use Map/Action. Candidates: " +
                   string.Join(", ", candidates.ToArray());
        }

        static bool TryParseReplayMouseButton(string name, out MouseButton button)
        {
            switch (name)
            {
                case "left":   button = MouseButton.Left; return true;
                case "right":  button = MouseButton.Right; return true;
                case "middle": button = MouseButton.Middle; return true;
                default:       button = default(MouseButton); return false;
            }
        }

        static string MouseButtonControlName(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Left:   return "leftButton";
                case MouseButton.Right:  return "rightButton";
                case MouseButton.Middle: return "middleButton";
                default: return button.ToString();
            }
        }
    }
}
