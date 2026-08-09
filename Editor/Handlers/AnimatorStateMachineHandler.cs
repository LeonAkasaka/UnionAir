using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles sub-state machine authoring:
    ///   POST   /api/assets/animator-controllers/{guid}/state-machines             — create
    ///   DELETE /api/assets/animator-controllers/{guid}/state-machines             — remove
    ///   POST   /api/assets/animator-controllers/{guid}/state-machine-transitions  — connect
    ///   DELETE /api/assets/animator-controllers/{guid}/state-machine-transitions  — disconnect
    ///
    /// A state machine is a sub-asset owned by the controller, addressed by the layer and
    /// then a <c>stateMachinePath</c> of names from that layer's root. The connections it
    /// needs are <see cref="AnimatorTransition"/>, a different type from the one the state
    /// endpoints deal in, which is why they are authored here rather than there.
    /// </summary>
    internal class AnimatorStateMachineHandler
    {
        // ── POST .../state-machines ──────────────────────────────────────────

        public void HandleCreate(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = AnimatorStateMachineAddress.LoadController(guid, response);
            if (controller == null) return;

            // Opened before the first mutation so that this request is one undo entry.
            var undoGroup = UndoGroups.Begin("UnionAir: Add Animator State Machine");

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryValidateObjectFields(
                    body, new[] { "layerIndex", "stateMachinePath", "name", "position" }, out var fieldError))
            {
                RestResponse.SendError(response, fieldError, 400);
                return;
            }

            if (!TryReadLayerIndex(controller, body, response, out var layerIndex)) return;
            if (!AnimatorStateMachineAddress.TryResolve(controller, layerIndex, body, response, out var parent)) return;

            var name = RequestBodyReader.GetString(body, "name");
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }

            if (!TryReadPosition(body, response, out var position, out var hasPosition)) return;

            // Measured on 6000.0.80f1: AddStateMachine does not duplicate a sibling name, it
            // quietly hands back a different one. So the choice here is between reporting a
            // name the caller did not ask for and refusing. Refusing, because a path
            // addresses by name and a caller who asked for "Melee" and got "Melee 0" is
            // holding an address that does not work. A genuine duplicate still arises from
            // renaming in the Animator window, which is what the resolver's 409 is for.
            foreach (var sibling in parent.stateMachines)
            {
                if (sibling.stateMachine == null || sibling.stateMachine.name != name) continue;
                RestResponse.SendError(response,
                    $"'{name}' already names a state machine here, and stateMachinePath addresses by name, " +
                    "so a second one could not be addressed. Use a different name.", 409);
                return;
            }

            // AddStateMachine owns the sub-asset. Building one with `new` and adding it by
            // hand is what leaves a controller whose file holds a machine nothing points at.
            var created = hasPosition
                ? parent.AddStateMachine(name, new Vector3(position.x, position.y, 0f))
                : parent.AddStateMachine(name);

            Save(controller, undoGroup);

            RequestBodyReader.TryGetStringArray(body, "stateMachinePath", out var parentPath);
            var path = new List<string>(parentPath) { created.name };

            var sb = new StringBuilder();
            sb.Append("{\"added\":").Append(RestResponse.FormatNullableString(created.name));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            sb.Append(",\"stateMachinePath\":").Append(PathJson(path));
            sb.Append("}");
            RestResponse.Send(response, sb.ToString(), 201);
        }

        // ── DELETE .../state-machines ────────────────────────────────────────

        public void HandleDelete(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = AnimatorStateMachineAddress.LoadController(guid, response);
            if (controller == null) return;

            // Opened before the first mutation so that this request is one undo entry.
            var undoGroup = UndoGroups.Begin("UnionAir: Delete Animator State Machine");

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryValidateObjectFields(
                    body, new[] { "layerIndex", "stateMachinePath", "recursive" }, out var fieldError))
            {
                RestResponse.SendError(response, fieldError, 400);
                return;
            }

            if (!TryReadLayerIndex(controller, body, response, out var layerIndex)) return;
            if (!AnimatorStateMachineAddress.TryResolveParent(
                    controller, layerIndex, body, response, out var parent, out var path)) return;

            if (path.Length == 0)
            {
                RestResponse.SendError(response,
                    "stateMachinePath is empty, which names the layer's root state machine. " +
                    "A layer's root cannot be removed; delete the layer instead.", 400);
                return;
            }

            var name = path[path.Length - 1];
            AnimatorStateMachine target = null;
            var matches = 0;
            foreach (var child in parent.stateMachines)
            {
                if (child.stateMachine == null || child.stateMachine.name != name) continue;
                matches++;
                if (target == null) target = child.stateMachine;
            }

            if (matches == 0)
            {
                RestResponse.SendNotFound(response,
                    AnimatorStateMachineRules.NotFoundMessage(path, path.Length - 1));
                return;
            }
            if (matches > 1)
            {
                RestResponse.SendError(response,
                    AnimatorStateMachineRules.AmbiguousMessage(path, path.Length - 1, matches), 409);
                return;
            }

            if (!RequestBodyReader.TryGetBoolValue(body, "recursive", out var recursive, out _))
            {
                RestResponse.SendError(response, "recursive must be a boolean.", 400);
                return;
            }

            AnimatorStateMachineRules.CountContents(target, out var stateCount, out var machineCount);
            if (!recursive && (stateCount > 0 || machineCount > 0))
            {
                SendNonEmpty(response, layerIndex, path, target, stateCount, machineCount);
                return;
            }

            // Blend trees are sub-assets of the controller, not of the state, and Unity does
            // not collect the nested ones -- the same gap #66 measured for DELETE .../states.
            // They are gathered while the states are still reachable and destroyed after.
            var doomed = new List<BlendTree>();
            CollectBlendTrees(controller, target, layerIndex, doomed);

            // RemoveStateMachine owns the removal: it destroys the machine and the states,
            // transitions, and nested machines it holds, all sub-assets of the controller.
            parent.RemoveStateMachine(target);

            var destroyed = 0;
            foreach (var tree in doomed)
            {
                if (tree == null) continue;
                Object.DestroyImmediate(tree, true);
                destroyed++;
            }

            Save(controller, undoGroup);

            var sb = new StringBuilder();
            sb.Append("{\"removed\":").Append(RestResponse.FormatNullableString(name));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            sb.Append(",\"stateMachinePath\":").Append(PathJson(new List<string>(path)));
            sb.Append(",\"removedStates\":").Append(stateCount);
            sb.Append(",\"removedStateMachines\":").Append(machineCount);
            sb.Append(",\"destroyedBlendTrees\":").Append(destroyed);
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>
        /// Answers a delete that would take content the request did not acknowledge.
        ///
        /// Deleting a state machine is not the same size of operation as deleting a state:
        /// it takes every state, transition, and nested machine inside it. The contents are
        /// listed so a caller who addressed the wrong machine sees what it was about to
        /// lose rather than the aftermath.
        /// </summary>
        private static void SendNonEmpty(
            UnionAirResponse response, int layerIndex, string[] path,
            AnimatorStateMachine target, int stateCount, int machineCount)
        {
            var sb = new StringBuilder();
            sb.Append("{\"error\":").Append(RestResponse.FormatNullableString(
                $"State machine '{target.name}' holds {stateCount} state(s) and {machineCount} nested " +
                "state machine(s) in total, which removing it would take with it. " +
                "Send recursive true to confirm."));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            sb.Append(",\"stateMachinePath\":").Append(PathJson(new List<string>(path)));
            sb.Append(",\"totalStates\":").Append(stateCount);
            sb.Append(",\"totalStateMachines\":").Append(machineCount);

            // The direct children are listed by name because they are what a caller can
            // recognise; the totals above are what the removal actually costs.
            sb.Append(",\"states\":[");
            var states = target.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(states[i].state == null ? null : states[i].state.name));
            }
            sb.Append("],\"stateMachines\":[");
            var machines = target.stateMachines;
            for (int i = 0; i < machines.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(
                    machines[i].stateMachine == null ? null : machines[i].stateMachine.name));
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString(), 409);
        }

        /// <summary>
        /// Collects every blend tree held by the states of a machine and of the machines
        /// beneath it, deepest last, so they can be destroyed once the subtree is detached.
        /// </summary>
        private static void CollectBlendTrees(
            AnimatorController controller, AnimatorStateMachine sm, int layerIndex, List<BlendTree> into)
        {
            foreach (var child in sm.states)
            {
                if (child.state == null) continue;
                BlendTreeHandler.CollectTrees(
                    controller.GetStateEffectiveMotion(child.state, layerIndex) as BlendTree, into);
            }
            foreach (var child in sm.stateMachines)
            {
                if (child.stateMachine == null) continue;
                CollectBlendTrees(controller, child.stateMachine, layerIndex, into);
            }
        }

        // ── POST .../state-machine-transitions ───────────────────────────────

        public void HandleAddTransition(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = AnimatorStateMachineAddress.LoadController(guid, response);
            if (controller == null) return;

            // Opened before the first mutation so that this request is one undo entry.
            var undoGroup = UndoGroups.Begin("UnionAir: Add State Machine Transition");

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryValidateObjectFields(
                    body,
                    new[] { "layerIndex", "stateMachinePath", "from", "to", "toStateMachine", "toExit", "solo", "mute", "conditions" },
                    out var fieldError))
            {
                RestResponse.SendError(response, fieldError, 400);
                return;
            }

            if (!TryReadLayerIndex(controller, body, response, out var layerIndex)) return;
            if (!AnimatorStateMachineAddress.TryResolve(controller, layerIndex, body, response, out var owner)) return;

            var fromName = RequestBodyReader.GetString(body, "from");
            if (string.IsNullOrEmpty(fromName))
            {
                RestResponse.SendError(response,
                    "Missing required field: from. Use \"Entry\" for an entry transition, " +
                    "or the name of a state machine nested in the addressed one.", 400);
                return;
            }

            if (!TryResolveDestination(owner, body, response,
                    out var toState, out var toMachine, out var toExit)) return;

            if (!AnimatorTransitionRequest.TryParseConditions(body, response, out var conditions, out var hasConditions))
                return;

            AnimatorTransition transition;
            if (fromName == "Entry")
            {
                if (toExit)
                {
                    RestResponse.SendError(response,
                        "An entry transition cannot target Exit: Entry chooses where the state machine starts.", 400);
                    return;
                }
                transition = toState != null
                    ? owner.AddEntryTransition(toState)
                    : owner.AddEntryTransition(toMachine);
            }
            else
            {
                AnimatorStateMachine source = null;
                var matches = 0;
                foreach (var child in owner.stateMachines)
                {
                    if (child.stateMachine == null || child.stateMachine.name != fromName) continue;
                    matches++;
                    if (source == null) source = child.stateMachine;
                }

                if (matches == 0)
                {
                    RestResponse.SendNotFound(response,
                        $"No state machine named '{fromName}' is nested in the addressed one. " +
                        "Use \"Entry\", or the name of a nested state machine.");
                    return;
                }
                if (matches > 1)
                {
                    RestResponse.SendError(response,
                        $"{matches} nested state machines are named '{fromName}', so the source is ambiguous.", 409);
                    return;
                }

                if (toExit)
                    transition = owner.AddStateMachineExitTransition(source);
                else if (toState != null)
                    transition = owner.AddStateMachineTransition(source, toState);
                else
                    transition = owner.AddStateMachineTransition(source, toMachine);
            }

            if (hasConditions) transition.conditions = conditions;

            if (RequestBodyReader.TryGetBoolValue(body, "solo", out var solo, out var hasSolo) && hasSolo)
                transition.solo = solo;
            if (RequestBodyReader.TryGetBoolValue(body, "mute", out var mute, out var hasMute) && hasMute)
                transition.mute = mute;

            Save(controller, undoGroup);

            var sb = new StringBuilder();
            sb.Append("{\"added\":true,\"transitionId\":").Append(
                RestResponse.FormatNullableString(ObjectIdUtils.GetGlobalObjectId(transition)));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            sb.Append(",\"from\":").Append(RestResponse.FormatNullableString(fromName));
            sb.Append("}");
            RestResponse.Send(response, sb.ToString(), 201);
        }

        // ── DELETE .../state-machine-transitions ─────────────────────────────

        public void HandleDeleteTransition(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = AnimatorStateMachineAddress.LoadController(guid, response);
            if (controller == null) return;

            // Opened before the first mutation so that this request is one undo entry.
            var undoGroup = UndoGroups.Begin("UnionAir: Delete State Machine Transition");

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryValidateObjectFields(
                    body, new[] { "layerIndex", "transitionId" }, out var fieldError))
            {
                RestResponse.SendError(response, fieldError, 400);
                return;
            }

            if (!TryReadLayerIndex(controller, body, response, out var layerIndex)) return;

            var transitionId = RequestBodyReader.GetString(body, "transitionId") ?? request.QueryString["transitionId"];
            if (string.IsNullOrEmpty(transitionId))
            {
                RestResponse.SendError(response,
                    "Missing required field: transitionId. These transitions have no name pair to address them by.", 400);
                return;
            }

            if (!ObjectIdUtils.TryResolveObject(transitionId, out var obj, out var error, out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            var transition = obj as AnimatorTransition;
            if (transition == null)
            {
                RestResponse.SendError(response,
                    $"transitionId does not resolve to an AnimatorTransition: {transitionId}. " +
                    "A transition between states is removed through DELETE .../transitions.", 422);
                return;
            }

            if (!TryRemoveTransition(controller.layers[layerIndex].stateMachine, transition, out var kind))
            {
                RestResponse.SendNotFound(response,
                    $"Transition {transitionId} is not in layer {layerIndex} of this controller.");
                return;
            }

            Save(controller, undoGroup);
            RestResponse.Send(response,
                $"{{\"removed\":true,\"transitionId\":{RestResponse.FormatNullableString(transitionId)}," +
                $"\"kind\":{RestResponse.FormatNullableString(kind)},\"layerIndex\":{layerIndex}}}");
        }

        /// <summary>
        /// Finds and removes an <see cref="AnimatorTransition"/> anywhere in a layer.
        ///
        /// Through Unity's own Remove methods rather than by rewriting the arrays: the
        /// transition is a sub-asset of the controller, and an array assignment that omits
        /// one detaches it without destroying it -- the leak #67 measured and fixed for
        /// state transitions applies unchanged here.
        /// </summary>
        private static bool TryRemoveTransition(
            AnimatorStateMachine sm, AnimatorTransition transition, out string kind)
        {
            kind = null;

            foreach (var t in sm.entryTransitions)
            {
                if (t != transition) continue;
                sm.RemoveEntryTransition(transition);
                kind = "entry";
                return true;
            }

            foreach (var child in sm.stateMachines)
            {
                if (child.stateMachine == null) continue;
                foreach (var t in sm.GetStateMachineTransitions(child.stateMachine))
                {
                    if (t != transition) continue;
                    sm.RemoveStateMachineTransition(child.stateMachine, transition);
                    kind = "stateMachine";
                    return true;
                }
            }

            foreach (var child in sm.stateMachines)
            {
                if (child.stateMachine == null) continue;
                if (TryRemoveTransition(child.stateMachine, transition, out kind)) return true;
            }

            return false;
        }

        // ── Shared ───────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the destination of an <see cref="AnimatorTransition"/>.
        ///
        /// Three separate fields rather than one overloaded name, for the reason the read
        /// gives a destination a discriminator: a name alone cannot say whether it means a
        /// state or a state machine, and a controller is free to use the same name for both.
        /// </summary>
        private static bool TryResolveDestination(
            AnimatorStateMachine owner, string body, UnionAirResponse response,
            out AnimatorState state, out AnimatorStateMachine machine, out bool exit)
        {
            state = null;
            machine = null;
            exit = false;

            var toName = RequestBodyReader.GetString(body, "to");
            if (!RequestBodyReader.TryGetStringArray(body, "toStateMachine", out var toMachinePath))
            {
                RestResponse.SendError(response,
                    "toStateMachine must be an array of state machine names.", 400);
                return false;
            }
            var hasMachine = RequestBodyReader.HasTopLevelField(body, "toStateMachine");
            if (!RequestBodyReader.TryGetBoolValue(body, "toExit", out exit, out var hasExit))
            {
                RestResponse.SendError(response, "toExit must be a boolean.", 400);
                return false;
            }
            if (!hasExit) exit = false;

            var given = (string.IsNullOrEmpty(toName) ? 0 : 1) + (hasMachine ? 1 : 0) + (exit ? 1 : 0);
            if (given == 0)
            {
                RestResponse.SendError(response,
                    "Missing destination. Send exactly one of: to (a state name), " +
                    "toStateMachine (a path), or toExit true.", 400);
                return false;
            }
            if (given > 1)
            {
                RestResponse.SendError(response,
                    "Send exactly one destination: to, toStateMachine, or toExit.", 400);
                return false;
            }

            if (exit) return true;

            if (hasMachine)
            {
                var result = AnimatorStateMachineRules.TryResolve(
                    owner, toMachinePath, out machine, out var depth, out var matches);
                if (result == AnimatorStateMachineRules.PathResult.Ambiguous)
                {
                    RestResponse.SendError(response,
                        AnimatorStateMachineRules.AmbiguousMessage(toMachinePath, depth, matches), 409);
                    return false;
                }
                if (result != AnimatorStateMachineRules.PathResult.Resolved)
                {
                    RestResponse.SendNotFound(response,
                        AnimatorStateMachineRules.NotFoundMessage(toMachinePath, depth));
                    return false;
                }
                if (toMachinePath.Length == 0)
                {
                    RestResponse.SendError(response,
                        "toStateMachine is empty, which names the machine the transition already belongs to.", 400);
                    return false;
                }
                return true;
            }

            foreach (var child in owner.states)
            {
                if (child.state == null || child.state.name != toName) continue;
                state = child.state;
                return true;
            }

            RestResponse.SendNotFound(response, $"Destination state not found: {toName}");
            return false;
        }

        private static bool TryReadLayerIndex(
            AnimatorController controller, string body, UnionAirResponse response, out int layerIndex)
        {
            layerIndex = RequestBodyReader.GetInt(body, "layerIndex") ?? 0;
            if (AnimatorLayerRules.TryValidateLayerIndex(layerIndex, controller.layers.Length, out var error))
                return true;

            RestResponse.SendError(response, error, 400);
            return false;
        }

        private static bool TryReadPosition(
            string body, UnionAirResponse response, out Vector2 position, out bool present)
        {
            position = Vector2.zero;
            present = false;

            var json = RequestBodyReader.GetObject(body, "position");
            if (json == null)
            {
                if (!RequestBodyReader.HasTopLevelField(body, "position")) return true;
                RestResponse.SendError(response, "position must be an object such as {\"x\":300,\"y\":120}.", 400);
                return false;
            }

            var x = RequestBodyReader.GetFloat(json, "x");
            var y = RequestBodyReader.GetFloat(json, "y");
            if (!x.HasValue || !y.HasValue)
            {
                RestResponse.SendError(response, "position requires x and y.", 400);
                return false;
            }

            position = new Vector2(x.Value, y.Value);
            present = true;
            return true;
        }

        private static string PathJson(List<string> path)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(path[i]));
            }
            return sb.Append("]").ToString();
        }

        /// <summary>
        /// Closes the undo group this request opened, then saves. The collapse is what
        /// makes one request one undo entry; see
        /// <see cref="AnimatorControllerHandler"/> for why opening one is necessary even
        /// though Unity registers the undo itself.
        /// </summary>
        private static void Save(AnimatorController controller, int undoGroup)
        {
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }
    }
}
