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

            sm.RemoveState(state);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            RestResponse.Send(response, $"{{\"removed\":\"{RestResponse.EscapeJson(name)}\",\"layerIndex\":{layerIndex}}}");
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
