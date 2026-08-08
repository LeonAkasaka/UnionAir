using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles AnimatorController asset operations:
    ///   POST   /api/assets/animator-controllers                        — create
    ///   GET    /api/assets/animator-controllers/{guid}                 — read
    ///   POST   /api/assets/animator-controllers/{guid}/parameters      — add/update parameter
    ///   DELETE /api/assets/animator-controllers/{guid}/parameters      — remove parameter
    ///   POST   /api/assets/animator-controllers/{guid}/layers          — add layer
    ///   POST   /api/assets/animator-controllers/{guid}/states          — add state
    ///   PATCH  /api/assets/animator-controllers/{guid}/states          — update state
    ///   DELETE /api/assets/animator-controllers/{guid}/states          — delete state
    ///   POST   /api/assets/animator-controllers/{guid}/transitions     — add transition
    ///   PATCH  /api/assets/animator-controllers/{guid}/transitions     — update transition
    ///   DELETE /api/assets/animator-controllers/{guid}/transitions     — delete transition
    /// </summary>
    internal class AnimatorControllerHandler
    {
        // ── POST /api/assets/animator-controllers ────────────────────────────

        public void HandleCreate(UnionAirRequest request, UnionAirResponse response)
        {
            var body = RequestBodyReader.ReadString(request);
            var assetPath = RequestBodyReader.GetString(body, "assetPath");
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendError(response, "Missing required field: assetPath", 400);
                return;
            }
            if (!assetPath.EndsWith(".controller"))
            {
                RestResponse.SendError(response, "assetPath must end with .controller", 400);
                return;
            }

            AssetUtils.EnsureDirectory(System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'));

            var controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
            AssetDatabase.SaveAssets();

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            RestResponse.Send(response,
                $"{{\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",\"guid\":\"{RestResponse.EscapeJson(guid)}\"," +
                $"\"layerCount\":{controller.layers.Length}}}",
                201);
        }

        // ── GET /api/assets/animator-controllers/{guid} ──────────────────────

        public void HandleRead(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out var assetPath);
            if (controller == null) return;

            // Shared across every motion in this response: several states commonly
            // resolve to the same imported model file, and counting its clips is a
            // sub-asset scan worth doing once per path rather than once per state.
            var clipCountByPath = new Dictionary<string, int>();

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",");
            sb.Append($"\"guid\":\"{RestResponse.EscapeJson(guid)}\",");

            // Parameters
            sb.Append("\"parameters\":[");
            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var p = parameters[i];
                sb.Append("{");
                sb.Append($"\"name\":\"{RestResponse.EscapeJson(p.name)}\",");
                sb.Append($"\"type\":\"{p.type}\"");
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float:
                        sb.Append($",\"defaultFloat\":{RestResponse.FormatFloat(p.defaultFloat)}");
                        break;
                    case AnimatorControllerParameterType.Int:
                        sb.Append($",\"defaultInt\":{p.defaultInt}");
                        break;
                    case AnimatorControllerParameterType.Bool:
                        sb.Append($",\"defaultBool\":{(p.defaultBool ? "true" : "false")}");
                        break;
                }
                sb.Append("}");
            }
            sb.Append("],");

            // Layers
            sb.Append("\"layers\":[");
            var layers = controller.layers;
            for (int li = 0; li < layers.Length; li++)
            {
                if (li > 0) sb.Append(",");
                var layer = layers[li];
                var sm = layer.stateMachine;
                sb.Append("{");
                sb.Append($"\"name\":\"{RestResponse.EscapeJson(layer.name)}\",");
                sb.Append($"\"index\":{li},");
                sb.Append($"\"weight\":{RestResponse.FormatFloat(layer.defaultWeight)},");
                sb.Append($"\"blendingMode\":\"{layer.blendingMode}\",");

                // States
                sb.Append("\"states\":[");
                var states = sm.states;
                for (int si = 0; si < states.Length; si++)
                {
                    if (si > 0) sb.Append(",");
                    var state = states[si].state;
                    sb.Append("{");
                    sb.Append($"\"name\":\"{RestResponse.EscapeJson(state.name)}\",");
                    sb.Append($"\"speed\":{RestResponse.FormatFloat(state.speed)},");
                    sb.Append($"\"isDefault\":{(sm.defaultState == state ? "true" : "false")},");

                    // Motion
                    sb.Append("\"motion\":");
                    MotionJson.Append(sb, controller.GetStateEffectiveMotion(state, li), clipCountByPath);
                    sb.Append(",");

                    // Transitions
                    sb.Append("\"transitions\":[");
                    var transitions = state.transitions;
                    for (int ti = 0; ti < transitions.Length; ti++)
                    {
                        if (ti > 0) sb.Append(",");
                        AppendTransition(sb, transitions[ti]);
                    }
                    sb.Append("]}");
                }
                sb.Append("],");

                // AnyState transitions
                sb.Append("\"anyStateTransitions\":[");
                var anyTransitions = sm.anyStateTransitions;
                for (int ti = 0; ti < anyTransitions.Length; ti++)
                {
                    if (ti > 0) sb.Append(",");
                    AppendTransition(sb, anyTransitions[ti]);
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── POST /api/assets/animator-controllers/{guid}/parameters ──────────

        public void HandleAddParameter(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            var name = RequestBodyReader.GetString(body, "name");
            var typeStr = RequestBodyReader.GetString(body, "type");
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }
            if (string.IsNullOrEmpty(typeStr))
            {
                RestResponse.SendError(response, "Missing required field: type (Float, Int, Bool, Trigger)", 400);
                return;
            }

            AnimatorControllerParameterType paramType;
            switch (typeStr.ToLowerInvariant())
            {
                case "float":   paramType = AnimatorControllerParameterType.Float;   break;
                case "int":     paramType = AnimatorControllerParameterType.Int;     break;
                case "bool":    paramType = AnimatorControllerParameterType.Bool;    break;
                case "trigger": paramType = AnimatorControllerParameterType.Trigger; break;
                default:
                    RestResponse.SendError(response, $"Unknown parameter type: {typeStr}. Use Float, Int, Bool, or Trigger.", 400);
                    return;
            }

            // Remove existing param with same name to allow update
            var existing = FindParameter(controller, name);
            if (existing != null)
                controller.RemoveParameter(existing);

            controller.AddParameter(name, paramType);

            // Apply optional default value by updating the parameter array
            var allParams = controller.parameters;
            for (int i = 0; i < allParams.Length; i++)
            {
                if (allParams[i].name != name) continue;
                switch (paramType)
                {
                    case AnimatorControllerParameterType.Float:
                        var df = RequestBodyReader.GetFloat(body, "defaultValue");
                        if (df.HasValue) allParams[i].defaultFloat = df.Value;
                        break;
                    case AnimatorControllerParameterType.Int:
                        var di = RequestBodyReader.GetInt(body, "defaultValue");
                        if (di.HasValue) allParams[i].defaultInt = di.Value;
                        break;
                    case AnimatorControllerParameterType.Bool:
                        var db = RequestBodyReader.GetBool(body, "defaultValue");
                        if (db.HasValue) allParams[i].defaultBool = db.Value;
                        break;
                }
                break;
            }
            controller.parameters = allParams;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"added\":\"{RestResponse.EscapeJson(name)}\",\"type\":\"{RestResponse.EscapeJson(typeStr)}\"}}",
                201);
        }

        // ── DELETE /api/assets/animator-controllers/{guid}/parameters ────────

        public void HandleDeleteParameter(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            var name = RequestBodyReader.GetString(body, "name") ?? request.QueryString["name"];
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }

            var param = FindParameter(controller, name);
            if (param == null)
            {
                RestResponse.SendNotFound(response, $"Parameter not found: {name}");
                return;
            }

            controller.RemoveParameter(param);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response, $"{{\"removed\":\"{RestResponse.EscapeJson(name)}\"}}");
        }

        // ── POST /api/assets/animator-controllers/{guid}/layers ──────────────

        public void HandleAddLayer(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            var name = RequestBodyReader.GetString(body, "name");
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }

            controller.AddLayer(name);

            var weight = RequestBodyReader.GetFloat(body, "weight");
            if (weight.HasValue)
            {
                var layers = controller.layers;
                layers[layers.Length - 1].defaultWeight = weight.Value;
                controller.layers = layers;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"added\":\"{RestResponse.EscapeJson(name)}\",\"layerIndex\":{controller.layers.Length - 1}}}",
                201);
        }

        // ── POST /api/assets/animator-controllers/{guid}/states ──────────────

        public void HandleAddState(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            var name = RequestBodyReader.GetString(body, "name");
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }

            var layerIndex = RequestBodyReader.GetInt(body, "layerIndex") ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                RestResponse.SendError(response, $"layerIndex {layerIndex} is out of range (0-{controller.layers.Length - 1})", 400);
                return;
            }

            var sm = controller.layers[layerIndex].stateMachine;
            var state = sm.AddState(name);

            var speed = RequestBodyReader.GetFloat(body, "speed");
            if (speed.HasValue) state.speed = speed.Value;

            var motionObj = RequestBodyReader.GetObject(body, "motion");
            if (!string.IsNullOrEmpty(motionObj))
            {
                var motionGuid = RequestBodyReader.GetString(motionObj, "guid");
                if (!string.IsNullOrEmpty(motionGuid))
                {
                    var motionPath = AssetDatabase.GUIDToAssetPath(motionGuid);
                    var motion = AssetDatabase.LoadAssetAtPath<Motion>(motionPath);
                    if (motion == null)
                    {
                        RestResponse.SendError(response, $"Motion asset not found for GUID: {motionGuid}", 400);
                        return;
                    }
                    controller.SetStateEffectiveMotion(state, motion, layerIndex);
                }
            }

            var setAsDefault = RequestBodyReader.GetBool(body, "setAsDefault") ?? false;
            if (setAsDefault) sm.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"added\":\"{RestResponse.EscapeJson(name)}\",\"layerIndex\":{layerIndex},\"isDefault\":{(sm.defaultState == state ? "true" : "false")}}}",
                201);
        }

        // ── PATCH /api/assets/animator-controllers/{guid}/states ─────────────

        public void HandleUpdateState(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            var name = RequestBodyReader.GetString(body, "name");
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }

            var layerIndex = RequestBodyReader.GetInt(body, "layerIndex") ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                RestResponse.SendError(response, $"layerIndex {layerIndex} is out of range", 400);
                return;
            }

            var sm = controller.layers[layerIndex].stateMachine;
            var state = FindState(sm, name);
            if (state == null)
            {
                RestResponse.SendNotFound(response, $"State not found: {name}");
                return;
            }

            var newName = RequestBodyReader.GetString(body, "newName");
            if (!string.IsNullOrEmpty(newName)) state.name = newName;

            var speed = RequestBodyReader.GetFloat(body, "speed");
            if (speed.HasValue) state.speed = speed.Value;

            var setAsDefault = RequestBodyReader.GetBool(body, "setAsDefault");
            if (setAsDefault == true) sm.defaultState = state;

            var motionObj = RequestBodyReader.GetObject(body, "motion");
            if (!string.IsNullOrEmpty(motionObj))
            {
                var motionGuid = RequestBodyReader.GetString(motionObj, "guid");
                if (!string.IsNullOrEmpty(motionGuid))
                {
                    var motionPath = AssetDatabase.GUIDToAssetPath(motionGuid);
                    var motion = AssetDatabase.LoadAssetAtPath<Motion>(motionPath);
                    if (motion == null)
                    {
                        RestResponse.SendError(response, $"Motion asset not found for GUID: {motionGuid}", 400);
                        return;
                    }
                    controller.SetStateEffectiveMotion(state, motion, layerIndex);
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"updated\":\"{RestResponse.EscapeJson(state.name)}\",\"layerIndex\":{layerIndex}}}");
        }

        // ── DELETE /api/assets/animator-controllers/{guid}/states ────────────

        public void HandleDeleteState(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            var name = RequestBodyReader.GetString(body, "name") ?? request.QueryString["name"];
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }

            var layerIndex = RequestBodyReader.GetInt(body, "layerIndex") ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                RestResponse.SendError(response, $"layerIndex {layerIndex} is out of range", 400);
                return;
            }

            var sm = controller.layers[layerIndex].stateMachine;
            var state = FindState(sm, name);
            if (state == null)
            {
                RestResponse.SendNotFound(response, $"State not found: {name}");
                return;
            }

            // RemoveState destroys the state's own motion tree but not the trees nested
            // inside it: measured on 6000.0.80f1, removing a state whose blend tree had one
            // nested child left one BlendTree in the .controller file with no state and no
            // parent referring to it. A flat tree is cleaned up, which is why this is easy
            // to miss. The subtree is collected while it is still reachable and whatever
            // survives the removal is destroyed after.
            var doomed = new List<UnityEditor.Animations.BlendTree>();
            BlendTreeHandler.CollectTrees(
                controller.GetStateEffectiveMotion(state, layerIndex) as UnityEditor.Animations.BlendTree, doomed);

            sm.RemoveState(state);

            var destroyed = 0;
            foreach (var tree in doomed)
            {
                if (tree == null) continue;
                Object.DestroyImmediate(tree, true);
                destroyed++;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"removed\":\"{RestResponse.EscapeJson(name)}\",\"layerIndex\":{layerIndex}," +
                $"\"destroyedBlendTrees\":{destroyed}}}");
        }

        // ── POST /api/assets/animator-controllers/{guid}/transitions ─────────

        public void HandleAddTransition(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            var fromName = RequestBodyReader.GetString(body, "from");
            var toName = RequestBodyReader.GetString(body, "to");

            if (string.IsNullOrEmpty(fromName) || string.IsNullOrEmpty(toName))
            {
                RestResponse.SendError(response, "Missing required fields: from, to", 400);
                return;
            }

            var layerIndex = RequestBodyReader.GetInt(body, "layerIndex") ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                RestResponse.SendError(response, $"layerIndex {layerIndex} is out of range", 400);
                return;
            }

            var sm = controller.layers[layerIndex].stateMachine;
            AnimatorStateTransition transition;

            if (fromName == "AnyState")
            {
                if (toName == "Exit")
                {
                    RestResponse.SendError(response, "AnyState to Exit transition is not valid", 400);
                    return;
                }
                var toState = FindState(sm, toName);
                if (toState == null)
                {
                    RestResponse.SendNotFound(response, $"Destination state not found: {toName}");
                    return;
                }
                transition = sm.AddAnyStateTransition(toState);
            }
            else
            {
                var fromState = FindState(sm, fromName);
                if (fromState == null)
                {
                    RestResponse.SendNotFound(response, $"Source state not found: {fromName}");
                    return;
                }

                if (toName == "Exit")
                {
                    transition = fromState.AddExitTransition();
                }
                else
                {
                    var toState = FindState(sm, toName);
                    if (toState == null)
                    {
                        RestResponse.SendNotFound(response, $"Destination state not found: {toName}");
                        return;
                    }
                    transition = fromState.AddTransition(toState);
                }
            }

            ApplyTransitionSettings(transition, body);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"added\":true,\"from\":\"{RestResponse.EscapeJson(fromName)}\",\"to\":\"{RestResponse.EscapeJson(toName)}\",\"layerIndex\":{layerIndex}}}",
                201);
        }

        // ── PATCH /api/assets/animator-controllers/{guid}/transitions ────────

        public void HandleUpdateTransition(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            var fromName = RequestBodyReader.GetString(body, "from");
            var toName = RequestBodyReader.GetString(body, "to");

            if (string.IsNullOrEmpty(fromName) || string.IsNullOrEmpty(toName))
            {
                RestResponse.SendError(response, "Missing required fields: from, to", 400);
                return;
            }

            var layerIndex = RequestBodyReader.GetInt(body, "layerIndex") ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                RestResponse.SendError(response, $"layerIndex {layerIndex} is out of range", 400);
                return;
            }

            var sm = controller.layers[layerIndex].stateMachine;
            var transition = FindTransition(sm, fromName, toName);
            if (transition == null)
            {
                RestResponse.SendNotFound(response, $"Transition not found: {fromName} -> {toName}");
                return;
            }

            ApplyTransitionSettings(transition, body);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"updated\":true,\"from\":\"{RestResponse.EscapeJson(fromName)}\",\"to\":\"{RestResponse.EscapeJson(toName)}\",\"layerIndex\":{layerIndex}}}");
        }

        // ── DELETE /api/assets/animator-controllers/{guid}/transitions ───────

        public void HandleDeleteTransition(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            var fromName = RequestBodyReader.GetString(body, "from") ?? request.QueryString["from"];
            var toName = RequestBodyReader.GetString(body, "to") ?? request.QueryString["to"];

            if (string.IsNullOrEmpty(fromName) || string.IsNullOrEmpty(toName))
            {
                RestResponse.SendError(response, "Missing required fields: from, to", 400);
                return;
            }

            var layerIndex = RequestBodyReader.GetInt(body, "layerIndex") ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                RestResponse.SendError(response, $"layerIndex {layerIndex} is out of range", 400);
                return;
            }

            var sm = controller.layers[layerIndex].stateMachine;

            if (fromName == "AnyState")
            {
                var anyTransitions = new List<AnimatorStateTransition>(sm.anyStateTransitions);
                var removed = anyTransitions.RemoveAll(t => t.destinationState != null && t.destinationState.name == toName);
                if (removed == 0)
                {
                    RestResponse.SendNotFound(response, $"AnyState -> {toName} transition not found");
                    return;
                }
                sm.anyStateTransitions = anyTransitions.ToArray();
            }
            else
            {
                var fromState = FindState(sm, fromName);
                if (fromState == null)
                {
                    RestResponse.SendNotFound(response, $"Source state not found: {fromName}");
                    return;
                }

                var stateTransitions = new List<AnimatorStateTransition>(fromState.transitions);
                int removed;
                if (toName == "Exit")
                    removed = stateTransitions.RemoveAll(t => t.isExit);
                else
                    removed = stateTransitions.RemoveAll(t => t.destinationState != null && t.destinationState.name == toName);

                if (removed == 0)
                {
                    RestResponse.SendNotFound(response, $"Transition {fromName} -> {toName} not found");
                    return;
                }
                fromState.transitions = stateTransitions.ToArray();
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"removed\":true,\"from\":\"{RestResponse.EscapeJson(fromName)}\",\"to\":\"{RestResponse.EscapeJson(toName)}\",\"layerIndex\":{layerIndex}}}");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static AnimatorController LoadController(string guid, UnionAirResponse response, out string assetPath)
        {
            assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, $"No asset found for GUID: {guid}");
                return null;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
                RestResponse.SendError(response, $"Asset is not an AnimatorController: {assetPath}", 400);

            return controller;
        }

        private static AnimatorControllerParameter FindParameter(AnimatorController controller, string name)
        {
            foreach (var p in controller.parameters)
                if (p.name == name) return p;
            return null;
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (var cs in sm.states)
                if (cs.state.name == name) return cs.state;
            return null;
        }

        private static AnimatorStateTransition FindTransition(AnimatorStateMachine sm, string fromName, string toName)
        {
            if (fromName == "AnyState")
            {
                foreach (var t in sm.anyStateTransitions)
                    if (t.destinationState != null && t.destinationState.name == toName)
                        return t;
                return null;
            }

            var fromState = FindState(sm, fromName);
            if (fromState == null) return null;

            foreach (var t in fromState.transitions)
            {
                if (toName == "Exit" && t.isExit) return t;
                if (t.destinationState != null && t.destinationState.name == toName) return t;
            }
            return null;
        }

        private static void ApplyTransitionSettings(AnimatorStateTransition transition, string body)
        {
            var hasExitTime = RequestBodyReader.GetBool(body, "hasExitTime");
            if (hasExitTime.HasValue) transition.hasExitTime = hasExitTime.Value;

            var exitTime = RequestBodyReader.GetFloat(body, "exitTime");
            if (exitTime.HasValue) transition.exitTime = exitTime.Value;

            var duration = RequestBodyReader.GetFloat(body, "duration");
            if (duration.HasValue) transition.duration = duration.Value;

            var offset = RequestBodyReader.GetFloat(body, "offset");
            if (offset.HasValue) transition.offset = offset.Value;

            var conditions = RequestBodyReader.GetArray(body, "conditions");
            if (conditions == null || conditions.Count == 0) return;

            var condList = new List<AnimatorCondition>();
            foreach (var condJson in conditions)
            {
                var paramName = RequestBodyReader.GetString(condJson, "parameter");
                var modeStr = RequestBodyReader.GetString(condJson, "mode");
                if (string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(modeStr))
                    continue;

                AnimatorConditionMode mode;
                switch (modeStr.ToLowerInvariant())
                {
                    case "if":       mode = AnimatorConditionMode.If;       break;
                    case "ifnot":    mode = AnimatorConditionMode.IfNot;    break;
                    case "greater":  mode = AnimatorConditionMode.Greater;  break;
                    case "less":     mode = AnimatorConditionMode.Less;     break;
                    case "equals":   mode = AnimatorConditionMode.Equals;   break;
                    case "notequal": mode = AnimatorConditionMode.NotEqual; break;
                    default: continue;
                }

                var threshold = RequestBodyReader.GetFloat(condJson, "threshold") ?? 0f;
                condList.Add(new AnimatorCondition
                {
                    parameter = paramName,
                    mode = mode,
                    threshold = threshold
                });
            }
            transition.conditions = condList.ToArray();
        }

        private static void AppendTransition(StringBuilder sb, AnimatorStateTransition t)
        {
            sb.Append("{");
            if (t.isExit)
                sb.Append("\"to\":\"Exit\",");
            else if (t.destinationState != null)
                sb.Append($"\"to\":\"{RestResponse.EscapeJson(t.destinationState.name)}\",");
            else
                sb.Append("\"to\":null,");
            sb.Append($"\"hasExitTime\":{(t.hasExitTime ? "true" : "false")},");
            sb.Append($"\"exitTime\":{RestResponse.FormatFloat(t.exitTime)},");
            sb.Append($"\"duration\":{RestResponse.FormatFloat(t.duration)},");
            sb.Append("\"conditions\":[");
            for (int ci = 0; ci < t.conditions.Length; ci++)
            {
                if (ci > 0) sb.Append(",");
                var c = t.conditions[ci];
                sb.Append("{");
                sb.Append($"\"parameter\":\"{RestResponse.EscapeJson(c.parameter)}\",");
                sb.Append($"\"mode\":\"{c.mode}\",");
                sb.Append($"\"threshold\":{RestResponse.FormatFloat(c.threshold)}");
                sb.Append("}");
            }
            sb.Append("]}");
        }
    }
}
