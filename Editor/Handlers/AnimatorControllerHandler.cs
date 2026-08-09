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

                // defaultWeight, not weight. It is AnimatorControllerLayer.defaultWeight
                // verbatim, and for layer 0 that is not the weight in effect: the base
                // layer runs at 1 whatever the field holds, and the Animator window shows
                // no weight slider for it. Naming the field after the thing it reports
                // means the number needs no caveat to be read correctly, and isBaseLayer
                // is what tells a client the rule applies without knowing Unity's.
                sb.Append($"\"defaultWeight\":{RestResponse.FormatFloat(layer.defaultWeight)},");
                sb.Append($"\"isBaseLayer\":{(li == 0 ? "true" : "false")},");
                sb.Append($"\"blendingMode\":\"{layer.blendingMode}\",");

                // An AvatarMask is an ordinary asset with its own GUID, unlike a blend
                // tree, so this one is fetchable. Null for an unmasked layer, which is
                // what previously could not be told apart from a masked one at all.
                sb.Append("\"avatarMask\":");
                if (layer.avatarMask != null)
                {
                    var maskPath = AssetDatabase.GetAssetPath(layer.avatarMask);
                    var maskGuid = string.IsNullOrEmpty(maskPath) ? null : AssetDatabase.AssetPathToGUID(maskPath);
                    sb.Append("{\"guid\":");
                    sb.Append(RestResponse.FormatNullableString(string.IsNullOrEmpty(maskGuid) ? null : maskGuid));
                    sb.Append(",\"name\":");
                    sb.Append(RestResponse.FormatNullableString(layer.avatarMask.name));
                    sb.Append("},");
                }
                else
                {
                    sb.Append("null,");
                }

                sb.Append($"\"iKPass\":{RestResponse.FormatBool(layer.iKPass)},");
                sb.Append($"\"syncedLayerIndex\":{layer.syncedLayerIndex},");
                sb.Append($"\"syncedLayerAffectsTiming\":{RestResponse.FormatBool(layer.syncedLayerAffectsTiming)},");

                // States
                sb.Append("\"states\":[");
                var states = sm.states;
                for (int si = 0; si < states.Length; si++)
                {
                    if (si > 0) sb.Append(",");
                    var state = states[si].state;
                    sb.Append("{");
                    sb.Append($"\"name\":\"{RestResponse.EscapeJson(state.name)}\",");
                    sb.Append($"\"isDefault\":{(sm.defaultState == state ? "true" : "false")},");
                    AppendStateSettings(sb, state, states[si].position);

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
            var newIndex = controller.layers.Length - 1;

            // The same optional settings PATCH accepts, so a masked layer is one request
            // rather than a create followed by a patch. "weight" stays accepted alongside
            // "defaultWeight": a request field naming what it sets was never ambiguous,
            // and only the response field was misleading.
            if (!TryApplyLayerSettings(controller, newIndex, body, response, out var applied))
            {
                // The layer exists but the settings did not apply. Take it back rather
                // than answer 400 over a half-created layer.
                controller.RemoveLayer(newIndex);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                return;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"added\":\"{RestResponse.EscapeJson(name)}\",\"layerIndex\":{newIndex}," +
                $"\"applied\":[{applied}]}}",
                201);
        }

        // ── PATCH /api/assets/animator-controllers/{guid}/layers ─────────────

        public void HandleUpdateLayer(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryGetIntValue(body, "layerIndex", out var layerIndex, out var hasIndex) || !hasIndex)
            {
                RestResponse.SendError(response, "Missing or invalid required field: layerIndex", 400);
                return;
            }

            if (!AnimatorLayerRules.TryValidateLayerIndex(layerIndex, controller.layers.Length, out var error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            if (!TryApplyLayerSettings(controller, layerIndex, body, response, out var applied))
                return;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"layerIndex\":{layerIndex},\"applied\":[{applied}]}}");
        }

        // ── DELETE /api/assets/animator-controllers/{guid}/layers ────────────

        public void HandleDeleteLayer(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryGetIntValue(body, "layerIndex", out var layerIndex, out var hasIndex) || !hasIndex)
            {
                RestResponse.SendError(response, "Missing or invalid required field: layerIndex", 400);
                return;
            }

            if (!AnimatorLayerRules.TryValidateDelete(layerIndex, controller.layers.Length, out var error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            var syncTargets = new int[controller.layers.Length];
            for (int i = 0; i < syncTargets.Length; i++)
                syncTargets[i] = controller.layers[i].syncedLayerIndex;

            if (!AnimatorLayerRules.TryValidateDeleteAgainstSyncs(layerIndex, syncTargets, out error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            var removedName = controller.layers[layerIndex].name;

            // A synced layer does not own the state machine RemoveLayer would destroy, so
            // removing one while it is still synced leaves that state machine in the asset
            // with no layer referring to it -- measured on 6000.0.80f1: one layer left and
            // two AnimatorStateMachine sub-assets, in memory and in the .controller file
            // alike. Clearing the sync first hands ownership back, and the removal is then
            // clean.
            if (controller.layers[layerIndex].syncedLayerIndex != AnimatorLayerRules.NotSynced)
            {
                var unsynced = controller.layers;
                unsynced[layerIndex].syncedLayerIndex = AnimatorLayerRules.NotSynced;
                controller.layers = unsynced;
            }

            // Through RemoveLayer rather than by rewriting the layers array: the layer's
            // AnimatorStateMachine is a sub-asset owned by the controller, and RemoveLayer
            // is what destroys it.
            controller.RemoveLayer(layerIndex);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response,
                $"{{\"removed\":\"{RestResponse.EscapeJson(removedName)}\",\"layerIndex\":{layerIndex}," +
                $"\"layerCount\":{controller.layers.Length}}}");
        }

        /// <summary>
        /// Applies the optional layer settings a request carries, validating every value
        /// before it reaches Unity. Sends the error response and answers false when a
        /// value is rejected, so the caller only has to return.
        /// </summary>
        /// <param name="applied">JSON array body naming the fields that were set.</param>
        private static bool TryApplyLayerSettings(
            UnityEditor.Animations.AnimatorController controller,
            int layerIndex,
            string body,
            UnionAirResponse response,
            out string applied)
        {
            applied = "";
            var layers = controller.layers;
            var layer = layers[layerIndex];
            var names = new List<string>();

            if (RequestBodyReader.TryGetStringValue(body, "name", out var newName, out var hasName) && hasName)
            {
                if (string.IsNullOrEmpty(newName))
                {
                    RestResponse.SendError(response, "name must not be empty.", 400);
                    return false;
                }
                layer.name = newName;
                names.Add("name");
            }

            // defaultWeight is the field; weight is kept as the spelling POST already took.
            var weightRead = RequestBodyReader.TryGetFloatValue(body, "defaultWeight", out var weight, out var hasWeight);
            if (!hasWeight)
                weightRead = RequestBodyReader.TryGetFloatValue(body, "weight", out weight, out hasWeight);
            if (hasWeight)
            {
                if (!weightRead)
                {
                    RestResponse.SendError(response, "defaultWeight must be a number.", 400);
                    return false;
                }
                layer.defaultWeight = weight;
                names.Add("defaultWeight");
            }

            if (RequestBodyReader.TryGetStringValue(body, "blendingMode", out var blending, out var hasBlending) && hasBlending)
            {
                if (!TryParseBlendingMode(blending, out var mode))
                {
                    RestResponse.SendError(response, $"Unknown blendingMode: {blending}. Use Override or Additive.", 400);
                    return false;
                }
                layer.blendingMode = mode;
                names.Add("blendingMode");
            }

            if (RequestBodyReader.TryGetBoolValue(body, "iKPass", out var ikPass, out var hasIkPass) && hasIkPass)
            {
                layer.iKPass = ikPass;
                names.Add("iKPass");
            }

            if (RequestBodyReader.TryGetBoolValue(body, "syncedLayerAffectsTiming", out var affectsTiming, out var hasTiming) && hasTiming)
            {
                layer.syncedLayerAffectsTiming = affectsTiming;
                names.Add("syncedLayerAffectsTiming");
            }

            if (RequestBodyReader.TryGetIntValue(body, "syncedLayerIndex", out var synced, out var hasSynced) && hasSynced)
            {
                if (!AnimatorLayerRules.TryValidateSyncedLayerIndex(synced, layerIndex, layers.Length, out var syncError))
                {
                    RestResponse.SendError(response, syncError, 400);
                    return false;
                }
                layer.syncedLayerIndex = synced;
                names.Add("syncedLayerIndex");
            }

            // Omitted leaves the mask alone; explicit null clears it. GetObject cannot
            // tell those apart, which is why this reads through TryGetObjectOrNullValue.
            if (!RequestBodyReader.TryGetObjectOrNullValue(body, "avatarMask", out var maskJson, out var maskIsNull, out var hasMask))
            {
                RestResponse.SendError(response, "avatarMask must be an object such as {\"guid\":\"...\"} or null.", 400);
                return false;
            }
            if (hasMask)
            {
                if (maskIsNull)
                {
                    layer.avatarMask = null;
                    names.Add("avatarMask");
                }
                else
                {
                    var maskGuid = RequestBodyReader.GetString(maskJson, "guid");
                    if (string.IsNullOrEmpty(maskGuid))
                    {
                        RestResponse.SendError(response, "avatarMask requires a guid.", 400);
                        return false;
                    }
                    var maskPath = AssetDatabase.GUIDToAssetPath(maskGuid);
                    var mask = string.IsNullOrEmpty(maskPath) ? null : AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
                    if (mask == null)
                    {
                        RestResponse.SendError(response, $"No AvatarMask found for GUID: {maskGuid}", 404);
                        return false;
                    }
                    layer.avatarMask = mask;
                    names.Add("avatarMask");
                }
            }

            layers[layerIndex] = layer;
            controller.layers = layers;

            var sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{names[i]}\"");
            }
            applied = sb.ToString();
            return true;
        }

        private static bool TryParseBlendingMode(string value, out UnityEditor.Animations.AnimatorLayerBlendingMode mode)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "override": mode = UnityEditor.Animations.AnimatorLayerBlendingMode.Override; return true;
                case "additive": mode = UnityEditor.Animations.AnimatorLayerBlendingMode.Additive; return true;
            }
            mode = UnityEditor.Animations.AnimatorLayerBlendingMode.Override;
            return false;
        }

        // ── POST /api/assets/animator-controllers/{guid}/states ──────────────

        public void HandleAddState(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryValidateObjectFields(body, AnimatorStateRules.AddFields, out var fieldError))
            {
                RestResponse.SendError(response, fieldError, 400);
                return;
            }

            var name = RequestBodyReader.GetString(body, "name");
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }

            if (!TryReadLayerIndex(controller, body, request, response, out var layerIndex)) return;

            // Parsed before the state exists, so a rejected setting leaves nothing behind.
            if (!TryParseStateFields(controller, body, response, out var fields)) return;

            var sm = controller.layers[layerIndex].stateMachine;
            var state = sm.AddState(name);

            ApplyStateFields(controller, sm, state, layerIndex, fields);

            var setAsDefault = RequestBodyReader.GetBool(body, "setAsDefault") ?? false;
            if (setAsDefault) sm.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append("{\"added\":").Append(RestResponse.FormatNullableString(name));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            sb.Append(",\"isDefault\":").Append(RestResponse.FormatBool(sm.defaultState == state));
            AppendUnsupported(sb, AnimatorStateRules.CollectUnsupported(fields.SetBehaviours));
            sb.Append("}");
            RestResponse.Send(response, sb.ToString(), 201);
        }

        // ── PATCH /api/assets/animator-controllers/{guid}/states ─────────────

        public void HandleUpdateState(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            if (!RequestBodyReader.TryValidateObjectFields(body, AnimatorStateRules.UpdateFields, out var fieldError))
            {
                RestResponse.SendError(response, fieldError, 400);
                return;
            }

            var name = RequestBodyReader.GetString(body, "name");
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: name", 400);
                return;
            }

            if (!TryReadLayerIndex(controller, body, request, response, out var layerIndex)) return;

            var sm = controller.layers[layerIndex].stateMachine;
            var state = FindState(sm, name);
            if (state == null)
            {
                RestResponse.SendNotFound(response, $"State not found: {name}");
                return;
            }

            // Everything is checked before the first field is written, so a request that
            // sets several and fails on one leaves the state as it was.
            if (!TryParseStateFields(controller, body, response, out var fields)) return;

            var newName = RequestBodyReader.GetString(body, "newName");
            if (!string.IsNullOrEmpty(newName)) state.name = newName;

            ApplyStateFields(controller, sm, state, layerIndex, fields);

            var setAsDefault = RequestBodyReader.GetBool(body, "setAsDefault");
            if (setAsDefault == true) sm.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append("{\"updated\":").Append(RestResponse.FormatNullableString(state.name));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            AppendUnsupported(sb, AnimatorStateRules.CollectUnsupported(fields.SetBehaviours));
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
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

            if (!TryReadLayerIndex(controller, body, request, response, out var layerIndex)) return;

            // Parsed before the transition exists, so a rejected setting leaves nothing
            // behind. Creating first and validating after would add a transition -- a
            // sub-asset of the controller -- that the caller was told did not happen.
            if (!TryParseTransitionFields(body, response, out var fields)) return;

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

            ApplyTransitionFields(transition, fields);
            var unsupported = AnimatorTransitionRules.CollectUnsupported(
                fields.SetCanTransitionToSelf, fromName == "AnyState");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append("{\"added\":true,\"transitionId\":")
              .Append(RestResponse.FormatNullableString(ObjectIdUtils.GetGlobalObjectId(transition)));
            sb.Append(",\"from\":").Append(RestResponse.FormatNullableString(fromName));
            sb.Append(",\"to\":").Append(RestResponse.FormatNullableString(toName));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            AppendUnsupported(sb, unsupported);
            sb.Append("}");
            RestResponse.Send(response, sb.ToString(), 201);
        }

        // ── PATCH /api/assets/animator-controllers/{guid}/transitions ────────

        public void HandleUpdateTransition(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            if (!TryReadLayerIndex(controller, body, request, response, out var layerIndex)) return;
            if (!TryResolveTransition(controller, layerIndex, body, request, response, out var found)) return;

            // Every value is checked before the first one is written, so a request carrying
            // one bad field does not leave the others applied.
            if (!TryParseTransitionFields(body, response, out var fields)) return;

            ApplyTransitionFields(found.Transition, fields);
            var unsupported = AnimatorTransitionRules.CollectUnsupported(
                fields.SetCanTransitionToSelf, found.Owner == null);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append("{\"updated\":true,\"transitionId\":")
              .Append(RestResponse.FormatNullableString(ObjectIdUtils.GetGlobalObjectId(found.Transition)));
            sb.Append(",\"from\":").Append(RestResponse.FormatNullableString(found.FromName));
            sb.Append(",\"to\":").Append(RestResponse.FormatNullableString(found.ToName));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            AppendUnsupported(sb, unsupported);
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        // ── DELETE /api/assets/animator-controllers/{guid}/transitions ───────

        public void HandleDeleteTransition(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response, out _);
            if (controller == null) return;

            var body = RequestBodyReader.ReadString(request);
            if (!TryReadLayerIndex(controller, body, request, response, out var layerIndex)) return;
            if (!TryResolveTransition(controller, layerIndex, body, request, response, out var found)) return;

            // Read before the removal, because the object is destroyed by it.
            var transitionId = ObjectIdUtils.GetGlobalObjectId(found.Transition);
            var fromName = found.FromName;
            var toName = found.ToName;

            // Through RemoveTransition rather than by rewriting the transitions array. A
            // transition is a sub-asset of the controller, and assigning an array that omits
            // one detaches it without destroying it -- measured on 6000.0.80f1, that left an
            // AnimatorStateTransition in the .controller file with nothing referring to it.
            // These are the APIs that own the sub-asset's lifetime.
            if (found.Owner == null)
                controller.layers[layerIndex].stateMachine.RemoveAnyStateTransition(found.Transition);
            else
                found.Owner.RemoveTransition(found.Transition);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append("{\"removed\":true,\"transitionId\":")
              .Append(RestResponse.FormatNullableString(transitionId));
            sb.Append(",\"from\":").Append(RestResponse.FormatNullableString(fromName));
            sb.Append(",\"to\":").Append(RestResponse.FormatNullableString(toName));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
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

        // ── State settings ───────────────────────────────────────────────────

        /// <summary>
        /// The state settings a request carries, parsed and checked but not applied.
        ///
        /// Separating the two is what makes a request atomic: nothing is written until
        /// every value has been accepted, so a <c>POST</c> rejected for one bad field adds
        /// no state and a <c>PATCH</c> rejected for one leaves the others unapplied.
        /// </summary>
        private struct StateFields
        {
            public bool SetTag; public string Tag;
            public bool SetWriteDefaultValues; public bool WriteDefaultValues;
            public bool SetIkOnFeet; public bool IkOnFeet;
            public bool SetMirror; public bool Mirror;
            public bool SetCycleOffset; public float CycleOffset;
            public bool SetSpeed; public float Speed;
            public bool SetSpeedParameter; public string SpeedParameter;
            public bool SetSpeedParameterActive; public bool SpeedParameterActive;
            public bool SetCycleOffsetParameter; public string CycleOffsetParameter;
            public bool SetCycleOffsetParameterActive; public bool CycleOffsetParameterActive;
            public bool SetMirrorParameter; public string MirrorParameter;
            public bool SetMirrorParameterActive; public bool MirrorParameterActive;
            public bool SetTimeParameter; public string TimeParameter;
            public bool SetTimeParameterActive; public bool TimeParameterActive;
            public bool SetPosition; public Vector2 Position;
            public bool SetMotion; public Motion Motion;
            public bool SetBehaviours;
        }

        private static bool TryParseStateFields(
            AnimatorController controller, string body, UnionAirResponse response, out StateFields fields)
        {
            fields = default(StateFields);
            fields.SetBehaviours = RequestBodyReader.HasTopLevelField(body, "behaviours");

            if (!RequestBodyReader.TryGetStringValue(body, "tag", out fields.Tag, out fields.SetTag))
            {
                RestResponse.SendError(response, "tag must be a string.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetBoolValue(body, "writeDefaultValues", out fields.WriteDefaultValues, out fields.SetWriteDefaultValues))
            {
                RestResponse.SendError(response, "writeDefaultValues must be a boolean.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetBoolValue(body, "iKOnFeet", out fields.IkOnFeet, out fields.SetIkOnFeet))
            {
                RestResponse.SendError(response, "iKOnFeet must be a boolean.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetBoolValue(body, "mirror", out fields.Mirror, out fields.SetMirror))
            {
                RestResponse.SendError(response, "mirror must be a boolean.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetFloatValue(body, "cycleOffset", out fields.CycleOffset, out fields.SetCycleOffset))
            {
                RestResponse.SendError(response, "cycleOffset must be a number.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetFloatValue(body, "speed", out fields.Speed, out fields.SetSpeed))
            {
                RestResponse.SendError(response, "speed must be a number.", 400);
                return false;
            }

            if (!TryReadStateParameter(controller, body, "speedParameter", response,
                    ref fields.SpeedParameter, ref fields.SetSpeedParameter,
                    ref fields.SpeedParameterActive, ref fields.SetSpeedParameterActive)) return false;
            if (!TryReadStateParameter(controller, body, "cycleOffsetParameter", response,
                    ref fields.CycleOffsetParameter, ref fields.SetCycleOffsetParameter,
                    ref fields.CycleOffsetParameterActive, ref fields.SetCycleOffsetParameterActive)) return false;
            if (!TryReadStateParameter(controller, body, "mirrorParameter", response,
                    ref fields.MirrorParameter, ref fields.SetMirrorParameter,
                    ref fields.MirrorParameterActive, ref fields.SetMirrorParameterActive)) return false;
            if (!TryReadStateParameter(controller, body, "timeParameter", response,
                    ref fields.TimeParameter, ref fields.SetTimeParameter,
                    ref fields.TimeParameterActive, ref fields.SetTimeParameterActive)) return false;

            if (!TryReadStatePosition(body, response, ref fields)) return false;
            return TryReadStateMotion(body, response, ref fields);
        }

        /// <summary>
        /// Reads one parameter override and its Active flag together.
        ///
        /// Together because the pair is one decision: a name that does not exist must be
        /// refused whichever half of the request carries it, and activating an override on
        /// a parameter the controller does not have leaves a state that cannot play. An
        /// empty name clears the override and is not looked up.
        /// </summary>
        private static bool TryReadStateParameter(
            AnimatorController controller, string body, string field, UnionAirResponse response,
            ref string value, ref bool valueSet, ref bool active, ref bool activeSet)
        {
            if (!RequestBodyReader.TryGetStringValue(body, field, out value, out valueSet))
            {
                RestResponse.SendError(response, $"{field} must be a string.", 400);
                return false;
            }

            var activeField = AnimatorStateRules.ActiveFieldFor(field);
            if (!RequestBodyReader.TryGetBoolValue(body, activeField, out active, out activeSet))
            {
                RestResponse.SendError(response, $"{activeField} must be a boolean.", 400);
                return false;
            }

            if (!valueSet || string.IsNullOrEmpty(value)) return true;

            foreach (var p in controller.parameters)
                if (p.name == value) return true;

            RestResponse.SendError(response,
                $"{field} '{value}' names no parameter on this controller. Add it first with " +
                "POST /api/assets/animator-controllers/{guid}/parameters.", 400);
            return false;
        }

        private static bool TryReadStatePosition(string body, UnionAirResponse response, ref StateFields fields)
        {
            var positionJson = RequestBodyReader.GetObject(body, "position");
            if (positionJson == null)
            {
                if (!RequestBodyReader.HasTopLevelField(body, "position")) return true;
                RestResponse.SendError(response, "position must be an object such as {\"x\":300,\"y\":120}.", 400);
                return false;
            }

            var x = RequestBodyReader.GetFloat(positionJson, "x");
            var y = RequestBodyReader.GetFloat(positionJson, "y");
            if (!x.HasValue || !y.HasValue)
            {
                RestResponse.SendError(response, "position requires x and y.", 400);
                return false;
            }

            fields.SetPosition = true;
            fields.Position = new Vector2(x.Value, y.Value);
            return true;
        }

        private static bool TryReadStateMotion(string body, UnionAirResponse response, ref StateFields fields)
        {
            var motionJson = RequestBodyReader.GetObject(body, "motion");
            if (motionJson == null)
            {
                if (!RequestBodyReader.HasTopLevelField(body, "motion")) return true;
                RestResponse.SendError(response, "motion must be an object such as {\"guid\":\"...\"}.", 400);
                return false;
            }

            var motionGuid = RequestBodyReader.GetString(motionJson, "guid");
            if (string.IsNullOrEmpty(motionGuid))
            {
                RestResponse.SendError(response, "motion requires a guid.", 400);
                return false;
            }

            var motionPath = AssetDatabase.GUIDToAssetPath(motionGuid);
            var motion = string.IsNullOrEmpty(motionPath) ? null : AssetDatabase.LoadAssetAtPath<Motion>(motionPath);
            if (motion == null)
            {
                RestResponse.SendError(response, $"Motion asset not found for GUID: {motionGuid}", 400);
                return false;
            }

            fields.SetMotion = true;
            fields.Motion = motion;
            return true;
        }

        /// <summary>
        /// Writes parsed settings. Cannot fail, which is the point: every check happened in
        /// <see cref="TryParseStateFields"/>, before anything was mutated.
        /// </summary>
        private static void ApplyStateFields(
            AnimatorController controller, AnimatorStateMachine sm, AnimatorState state,
            int layerIndex, StateFields fields)
        {
            if (fields.SetTag) state.tag = fields.Tag;
            if (fields.SetWriteDefaultValues) state.writeDefaultValues = fields.WriteDefaultValues;
            if (fields.SetIkOnFeet) state.iKOnFeet = fields.IkOnFeet;
            if (fields.SetMirror) state.mirror = fields.Mirror;
            if (fields.SetCycleOffset) state.cycleOffset = fields.CycleOffset;
            if (fields.SetSpeed) state.speed = fields.Speed;

            if (fields.SetSpeedParameter) state.speedParameter = fields.SpeedParameter;
            if (fields.SetSpeedParameterActive) state.speedParameterActive = fields.SpeedParameterActive;
            if (fields.SetCycleOffsetParameter) state.cycleOffsetParameter = fields.CycleOffsetParameter;
            if (fields.SetCycleOffsetParameterActive) state.cycleOffsetParameterActive = fields.CycleOffsetParameterActive;
            if (fields.SetMirrorParameter) state.mirrorParameter = fields.MirrorParameter;
            if (fields.SetMirrorParameterActive) state.mirrorParameterActive = fields.MirrorParameterActive;
            if (fields.SetTimeParameter) state.timeParameter = fields.TimeParameter;
            if (fields.SetTimeParameterActive) state.timeParameterActive = fields.TimeParameterActive;

            if (fields.SetMotion) controller.SetStateEffectiveMotion(state, fields.Motion, layerIndex);

            if (fields.SetPosition) ApplyStatePosition(sm, state, fields.Position);
        }

        /// <summary>
        /// Moves a state in the graph.
        ///
        /// The position is on the <see cref="ChildAnimatorState"/> struct rather than on the
        /// state, so it takes reading the array, mutating the entry, and assigning the whole
        /// array back -- the same shape the layer writes use. The struct's z is preserved
        /// rather than zeroed: the graph does not use it, and nothing here has grounds to
        /// discard what the asset holds.
        /// </summary>
        private static void ApplyStatePosition(AnimatorStateMachine sm, AnimatorState state, Vector2 position)
        {
            var states = sm.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].state != state) continue;
                states[i].position = new Vector3(position.x, position.y, states[i].position.z);
                sm.states = states;
                return;
            }
        }

        /// <summary>
        /// Serializes the settings that decide how a state plays, plus its place in the
        /// graph. Emits a trailing comma, because every caller follows it with more fields.
        /// </summary>
        /// <param name="position">
        /// From the owning <see cref="ChildAnimatorState"/> rather than from the state: the
        /// graph position belongs to the entry in the state machine's array, not to the
        /// state object.
        /// </param>
        private static void AppendStateSettings(StringBuilder sb, AnimatorState state, Vector3 position)
        {
            sb.Append("\"tag\":").Append(RestResponse.FormatNullableString(state.tag)).Append(",");

            // The setting most likely to be the reason a controller misbehaves, and until
            // now the API could neither see nor set it.
            sb.Append($"\"writeDefaultValues\":{RestResponse.FormatBool(state.writeDefaultValues)},");
            sb.Append($"\"iKOnFeet\":{RestResponse.FormatBool(state.iKOnFeet)},");
            sb.Append($"\"mirror\":{RestResponse.FormatBool(state.mirror)},");
            sb.Append($"\"cycleOffset\":{RestResponse.FormatFloat(state.cycleOffset)},");
            sb.Append($"\"speed\":{RestResponse.FormatFloat(state.speed)},");

            // Each name travels with its own Active flag rather than being folded to an
            // empty string when inactive. Unity stores both, and a client cannot reproduce
            // the state from one of them: an inactive parameter name is content the asset
            // holds, and a literal speed beside an active speedParameter is not the speed in
            // effect.
            AppendParameterOverride(sb, "speedParameter", state.speedParameter, state.speedParameterActive);
            AppendParameterOverride(sb, "cycleOffsetParameter", state.cycleOffsetParameter, state.cycleOffsetParameterActive);
            AppendParameterOverride(sb, "mirrorParameter", state.mirrorParameter, state.mirrorParameterActive);
            AppendParameterOverride(sb, "timeParameter", state.timeParameter, state.timeParameterActive);

            // Unity's field is a Vector3, and the Animator window's graph is flat: z is
            // unused there. It is left out of the response rather than reported as a number
            // that means nothing, and a write preserves whatever it holds.
            sb.Append("\"position\":{");
            sb.Append($"\"x\":{RestResponse.FormatFloat(position.x)},");
            sb.Append($"\"y\":{RestResponse.FormatFloat(position.y)}");
            sb.Append("},");

            // Read-only. A state that runs script on entry was previously indistinguishable
            // from one that does not; a null entry is a behaviour whose script is missing,
            // which is worth reporting rather than dropping from the array.
            sb.Append("\"behaviours\":[");
            var behaviours = state.behaviours;
            for (int bi = 0; bi < behaviours.Length; bi++)
            {
                if (bi > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(
                    behaviours[bi] == null ? null : behaviours[bi].GetType().Name));
            }
            sb.Append("],");
        }

        private static void AppendParameterOverride(StringBuilder sb, string field, string parameter, bool active)
        {
            sb.Append($"\"{field}\":").Append(RestResponse.FormatNullableString(parameter)).Append(",");
            sb.Append($"\"{field}Active\":{RestResponse.FormatBool(active)},");
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (var cs in sm.states)
                if (cs.state.name == name) return cs.state;
            return null;
        }

        /// <summary>
        /// Reads the layer a transition write applies to, defaulting to 0.
        ///
        /// The query string is read as a fallback because the address fields are, so a
        /// DELETE sent entirely as query parameters can still name a layer other than the
        /// base one.
        /// </summary>
        private static bool TryReadLayerIndex(
            AnimatorController controller, string body, UnionAirRequest request,
            UnionAirResponse response, out int layerIndex)
        {
            layerIndex = 0;

            var fromBody = RequestBodyReader.GetInt(body, "layerIndex");
            if (fromBody.HasValue)
            {
                layerIndex = fromBody.Value;
            }
            else
            {
                var raw = request.QueryString["layerIndex"];
                if (!string.IsNullOrEmpty(raw) &&
                    !int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out layerIndex))
                {
                    RestResponse.SendError(response, $"layerIndex must be an integer: {raw}", 400);
                    return false;
                }
            }

            if (AnimatorLayerRules.TryValidateLayerIndex(layerIndex, controller.layers.Length, out var error))
                return true;

            RestResponse.SendError(response, error, 400);
            return false;
        }

        // ── Transition addressing ────────────────────────────────────────────

        /// <summary>
        /// A transition together with where it sits.
        ///
        /// The read does not need this, because it walks down from the owner already. A
        /// write addressed by <c>transitionId</c> arrives with the transition and nothing
        /// else, and removing one takes the object that owns it.
        /// </summary>
        private struct TransitionRef
        {
            public AnimatorStateTransition Transition;
            /// <summary>The source state, or null when the transition leaves AnyState.</summary>
            public AnimatorState Owner;
            public string FromName;
            public string ToName;
        }

        private static string DestinationName(AnimatorStateTransition t)
            => t.isExit ? "Exit" : (t.destinationState != null ? t.destinationState.name : null);

        /// <summary>
        /// Every AnimatorStateTransition a layer's top-level state machine carries, in the
        /// order the read reports them. Sub-state machines are not traversed; they are not
        /// described by any endpoint yet, and addressing what cannot be read would be a
        /// contract this issue does not own.
        /// </summary>
        private static List<TransitionRef> EnumerateTransitions(AnimatorStateMachine sm)
        {
            var refs = new List<TransitionRef>();
            foreach (var cs in sm.states)
            {
                foreach (var t in cs.state.transitions)
                {
                    refs.Add(new TransitionRef
                    {
                        Transition = t,
                        Owner = cs.state,
                        FromName = cs.state.name,
                        ToName = DestinationName(t)
                    });
                }
            }
            foreach (var t in sm.anyStateTransitions)
            {
                refs.Add(new TransitionRef
                {
                    Transition = t,
                    Owner = null,
                    FromName = "AnyState",
                    ToName = DestinationName(t)
                });
            }
            return refs;
        }

        /// <summary>
        /// Resolves the one transition a write addresses, by <c>transitionId</c> when the
        /// request carries one and by <c>from</c> plus <c>to</c> otherwise.
        ///
        /// A state pair may carry any number of transitions, so the name pair is an address
        /// only while it resolves to one. When it resolves to several the request is
        /// answered with a 409 naming every candidate, rather than by picking the first --
        /// which is what PATCH used to do -- or by acting on all of them, which is what
        /// DELETE used to do.
        /// </summary>
        private static bool TryResolveTransition(
            AnimatorController controller, int layerIndex, string body,
            UnionAirRequest request, UnionAirResponse response, out TransitionRef found)
        {
            found = default(TransitionRef);
            var sm = controller.layers[layerIndex].stateMachine;

            if (!RequestBodyReader.TryGetStringValue(body, "transitionId", out var transitionId, out var hasId))
            {
                RestResponse.SendError(response, "transitionId must be a string.", 400);
                return false;
            }
            if (!hasId) transitionId = request.QueryString["transitionId"];

            if (!string.IsNullOrEmpty(transitionId))
                return TryResolveById(controller, layerIndex, transitionId, response, out found);

            var fromName = RequestBodyReader.GetString(body, "from") ?? request.QueryString["from"];
            var toName = RequestBodyReader.GetString(body, "to") ?? request.QueryString["to"];
            if (string.IsNullOrEmpty(fromName) || string.IsNullOrEmpty(toName))
            {
                RestResponse.SendError(response,
                    "Address the transition by transitionId, or by from and to.", 400);
                return false;
            }

            var matches = new List<TransitionRef>();
            foreach (var candidate in EnumerateTransitions(sm))
            {
                if (candidate.FromName == fromName && candidate.ToName == toName)
                    matches.Add(candidate);
            }

            if (matches.Count == 0)
            {
                RestResponse.SendNotFound(response,
                    $"Transition not found in layer {layerIndex}: {fromName} -> {toName}");
                return false;
            }

            if (matches.Count > 1)
            {
                SendAmbiguous(response, layerIndex, fromName, toName, matches);
                return false;
            }

            found = matches[0];
            return true;
        }

        private static bool TryResolveById(
            AnimatorController controller, int layerIndex, string transitionId,
            UnionAirResponse response, out TransitionRef found)
        {
            found = default(TransitionRef);

            if (!ObjectIdUtils.TryResolveObject(transitionId, out var obj, out var error, out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return false;
            }

            var transition = obj as AnimatorStateTransition;
            if (transition == null)
            {
                RestResponse.SendError(response,
                    $"transitionId does not resolve to an AnimatorStateTransition: {transitionId}", 422);
                return false;
            }

            foreach (var candidate in EnumerateTransitions(controller.layers[layerIndex].stateMachine))
            {
                if (candidate.Transition != transition) continue;
                found = candidate;
                return true;
            }

            // Resolvable but not in the layer being written. layerIndex defaults to 0, so
            // this is what a caller holding an id from another layer hits first, and the
            // layer it is actually in is the one thing the message can usefully add.
            for (int i = 0; i < controller.layers.Length; i++)
            {
                if (i == layerIndex) continue;
                foreach (var candidate in EnumerateTransitions(controller.layers[i].stateMachine))
                {
                    if (candidate.Transition != transition) continue;
                    RestResponse.SendNotFound(response,
                        $"Transition {transitionId} is not in layer {layerIndex}; it is in layer {i}. " +
                        $"Send layerIndex {i}.");
                    return false;
                }
            }

            RestResponse.SendNotFound(response,
                $"Transition {transitionId} is not in layer {layerIndex} of this controller. " +
                "Sub-state machine transitions are not addressable.");
            return false;
        }

        private static void SendAmbiguous(
            UnionAirResponse response, int layerIndex, string fromName, string toName, List<TransitionRef> matches)
        {
            var sb = new StringBuilder();
            sb.Append("{\"error\":").Append(RestResponse.FormatNullableString(
                AnimatorTransitionRules.AmbiguousAddressMessage(fromName, toName, matches.Count)));
            sb.Append(",\"from\":").Append(RestResponse.FormatNullableString(fromName));
            sb.Append(",\"to\":").Append(RestResponse.FormatNullableString(toName));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            sb.Append(",\"matches\":[");
            for (int i = 0; i < matches.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"transitionId\":").Append(RestResponse.FormatNullableString(
                    ObjectIdUtils.GetGlobalObjectId(matches[i].Transition)));
                // The conditions are what distinguish one route from another, so they are
                // what lets the caller pick without a second request.
                sb.Append(",\"conditions\":");
                AppendConditions(sb, matches[i].Transition.conditions);
                sb.Append("}");
            }
            sb.Append("]}");
            RestResponse.Send(response, sb.ToString(), 409);
        }

        // ── Transition settings ──────────────────────────────────────────────

        /// <summary>
        /// The transition settings a request carries, parsed and checked but not applied.
        ///
        /// Separating the two is what makes a request atomic: nothing is written until
        /// every value has been accepted, so a rejected field leaves the transition as it
        /// was rather than partly updated.
        /// </summary>
        private struct TransitionFields
        {
            public bool SetHasExitTime; public bool HasExitTime;
            public bool SetExitTime; public float ExitTime;
            public bool SetDuration; public float Duration;
            public bool SetOffset; public float Offset;
            public bool SetFixedDuration; public bool FixedDuration;
            public bool SetInterruptionSource; public TransitionInterruptionSource InterruptionSource;
            public bool SetOrderedInterruption; public bool OrderedInterruption;
            public bool SetCanTransitionToSelf; public bool CanTransitionToSelf;
            public bool SetMute; public bool Mute;
            public bool SetSolo; public bool Solo;
            public bool SetConditions; public AnimatorCondition[] Conditions;
        }

        private static bool TryParseTransitionFields(string body, UnionAirResponse response, out TransitionFields fields)
        {
            fields = default(TransitionFields);

            if (!RequestBodyReader.TryGetBoolValue(body, "hasExitTime", out fields.HasExitTime, out fields.SetHasExitTime))
            {
                RestResponse.SendError(response, "hasExitTime must be a boolean.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetFloatValue(body, "exitTime", out fields.ExitTime, out fields.SetExitTime))
            {
                RestResponse.SendError(response, "exitTime must be a number.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetFloatValue(body, "duration", out fields.Duration, out fields.SetDuration))
            {
                RestResponse.SendError(response, "duration must be a number.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetFloatValue(body, "offset", out fields.Offset, out fields.SetOffset))
            {
                RestResponse.SendError(response, "offset must be a number.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetBoolValue(body, "fixedDuration", out fields.FixedDuration, out fields.SetFixedDuration))
            {
                RestResponse.SendError(response, "fixedDuration must be a boolean.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetBoolValue(body, "orderedInterruption", out fields.OrderedInterruption, out fields.SetOrderedInterruption))
            {
                RestResponse.SendError(response, "orderedInterruption must be a boolean.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetBoolValue(body, "canTransitionToSelf", out fields.CanTransitionToSelf, out fields.SetCanTransitionToSelf))
            {
                RestResponse.SendError(response, "canTransitionToSelf must be a boolean.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetBoolValue(body, "mute", out fields.Mute, out fields.SetMute))
            {
                RestResponse.SendError(response, "mute must be a boolean.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetBoolValue(body, "solo", out fields.Solo, out fields.SetSolo))
            {
                RestResponse.SendError(response, "solo must be a boolean.", 400);
                return false;
            }

            if (!RequestBodyReader.TryGetStringValue(body, "interruptionSource", out var sourceName, out fields.SetInterruptionSource))
            {
                RestResponse.SendError(response, "interruptionSource must be a string.", 400);
                return false;
            }
            if (fields.SetInterruptionSource &&
                !AnimatorTransitionRules.TryParseInterruptionSource(sourceName, out fields.InterruptionSource))
            {
                RestResponse.SendError(response,
                    $"Unknown interruptionSource: {sourceName}. Use one of {AnimatorTransitionRules.InterruptionSourceNames}.", 400);
                return false;
            }

            return TryParseConditions(body, response, ref fields);
        }

        /// <summary>
        /// Reads the conditions array, which replaces the transition's conditions wholesale.
        ///
        /// An element that does not parse is rejected rather than skipped. Skipping is what
        /// this used to do, and it produced a transition holding fewer conditions than the
        /// request listed, reported as a plain success.
        /// </summary>
        private static bool TryParseConditions(string body, UnionAirResponse response, ref TransitionFields fields)
        {
            if (!RequestBodyReader.TryGetArrayElements(body, "conditions", out var elements, out var present, out var arrayError))
            {
                RestResponse.SendError(response, arrayError, 400);
                return false;
            }
            if (!present) return true;

            var parsed = new List<AnimatorCondition>();
            for (int i = 0; i < elements.Count; i++)
            {
                var condJson = elements[i];
                var paramName = RequestBodyReader.GetString(condJson, "parameter");
                var modeStr = RequestBodyReader.GetString(condJson, "mode");
                if (string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(modeStr))
                {
                    RestResponse.SendError(response, $"conditions[{i}] requires parameter and mode.", 400);
                    return false;
                }

                if (!AnimatorTransitionRules.TryParseConditionMode(modeStr, out var mode))
                {
                    RestResponse.SendError(response,
                        $"Unknown condition mode in conditions[{i}]: {modeStr}. " +
                        $"Use one of {AnimatorTransitionRules.ConditionModeNames}.", 400);
                    return false;
                }

                // Absent means 0, which is what If and IfNot use and what Unity writes for
                // them. Present and unusable -- quoted, null, NaN -- is a different thing
                // and is refused: reading it as 0 would move a Greater threshold to zero
                // and report success, which is the silent skip this endpoint stopped doing
                // one field above.
                if (!RequestBodyReader.TryGetFloatValue(condJson, "threshold", out var threshold, out _))
                {
                    RestResponse.SendError(response, $"conditions[{i}].threshold must be a number.", 400);
                    return false;
                }

                parsed.Add(new AnimatorCondition
                {
                    parameter = paramName,
                    mode = mode,
                    threshold = threshold
                });
            }

            // An empty array is a request to clear, not a request to leave alone: the array
            // replaces what the transition holds, and it can replace it with nothing.
            fields.SetConditions = true;
            fields.Conditions = parsed.ToArray();
            return true;
        }

        /// <summary>
        /// Writes parsed settings. Cannot fail, which is the point: every check happened in
        /// <see cref="TryParseTransitionFields"/>, before anything was mutated.
        /// </summary>
        private static void ApplyTransitionFields(AnimatorStateTransition transition, TransitionFields fields)
        {
            if (fields.SetHasExitTime) transition.hasExitTime = fields.HasExitTime;
            if (fields.SetExitTime) transition.exitTime = fields.ExitTime;
            if (fields.SetDuration) transition.duration = fields.Duration;
            if (fields.SetOffset) transition.offset = fields.Offset;
            if (fields.SetFixedDuration) transition.hasFixedDuration = fields.FixedDuration;
            if (fields.SetInterruptionSource) transition.interruptionSource = fields.InterruptionSource;
            if (fields.SetOrderedInterruption) transition.orderedInterruption = fields.OrderedInterruption;
            if (fields.SetCanTransitionToSelf) transition.canTransitionToSelf = fields.CanTransitionToSelf;
            if (fields.SetMute) transition.mute = fields.Mute;
            if (fields.SetSolo) transition.solo = fields.Solo;
            if (fields.SetConditions) transition.conditions = fields.Conditions;
        }

        private static void AppendUnsupported(StringBuilder sb, List<string> unsupported)
        {
            sb.Append(",\"unsupported\":[");
            for (int i = 0; i < unsupported.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(unsupported[i]));
            }
            sb.Append("]");
        }

        private static void AppendConditions(StringBuilder sb, AnimatorCondition[] conditions)
        {
            sb.Append("[");
            for (int ci = 0; ci < conditions.Length; ci++)
            {
                if (ci > 0) sb.Append(",");
                var c = conditions[ci];
                sb.Append("{");
                sb.Append($"\"parameter\":\"{RestResponse.EscapeJson(c.parameter)}\",");
                sb.Append($"\"mode\":\"{c.mode}\",");
                sb.Append($"\"threshold\":{RestResponse.FormatFloat(c.threshold)}");
                sb.Append("}");
            }
            sb.Append("]");
        }

        private static void AppendTransition(StringBuilder sb, AnimatorStateTransition t)
        {
            sb.Append("{");
            sb.Append("\"transitionId\":").Append(
                RestResponse.FormatNullableString(ObjectIdUtils.GetGlobalObjectId(t))).Append(",");
            sb.Append("\"to\":").Append(RestResponse.FormatNullableString(DestinationName(t))).Append(",");
            sb.Append($"\"hasExitTime\":{RestResponse.FormatBool(t.hasExitTime)},");
            sb.Append($"\"exitTime\":{RestResponse.FormatFloat(t.exitTime)},");

            // duration and fixedDuration travel together because neither means anything
            // alone: with fixedDuration true the duration is seconds, and with it false the
            // same number is a fraction of the source state.
            sb.Append($"\"duration\":{RestResponse.FormatFloat(t.duration)},");
            sb.Append($"\"fixedDuration\":{RestResponse.FormatBool(t.hasFixedDuration)},");
            sb.Append($"\"offset\":{RestResponse.FormatFloat(t.offset)},");
            sb.Append($"\"interruptionSource\":\"{t.interruptionSource}\",");
            sb.Append($"\"orderedInterruption\":{RestResponse.FormatBool(t.orderedInterruption)},");
            sb.Append($"\"canTransitionToSelf\":{RestResponse.FormatBool(t.canTransitionToSelf)},");
            sb.Append($"\"mute\":{RestResponse.FormatBool(t.mute)},");
            sb.Append($"\"solo\":{RestResponse.FormatBool(t.solo)},");
            sb.Append("\"conditions\":");
            AppendConditions(sb, t.conditions);
            sb.Append("}");
        }
    }
}
