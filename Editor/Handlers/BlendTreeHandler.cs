using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles blend tree authoring:
    ///   POST   /api/assets/animator-controllers/{guid}/blend-trees   — create a tree or a child
    ///   PATCH  /api/assets/animator-controllers/{guid}/blend-trees   — update a tree or a child
    ///   DELETE /api/assets/animator-controllers/{guid}/blend-trees   — remove a tree or a child
    ///
    /// A blend tree has no GUID of its own, so it is addressed by where it sits: the
    /// owning layer and state, then a childPath of child indices from that state's root
    /// tree. The path is positional, which is a property of the asset rather than a
    /// choice made here -- Unity gives a ChildMotion no identity beyond its index.
    /// </summary>
    internal class BlendTreeHandler
    {
        // ── POST ─────────────────────────────────────────────────────────────

        public void HandleCreate(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response);
            if (controller == null) return;

            // Opened before the first mutation so that this request is one undo entry.
            var undoGroup = UndoGroups.Begin("UnionAir: Create Blend Tree");

            var body = RequestBodyReader.ReadString(request);
            if (!TryResolveState(controller, body, response, out var layerIndex, out var state)) return;

            var addChild = RequestBodyReader.GetBool(body, "addChild") ?? false;
            var rootMotion = controller.GetStateEffectiveMotion(state, layerIndex);

            if (!addChild)
            {
                if (rootMotion is BlendTree)
                {
                    RestResponse.SendError(response,
                        $"State '{state.name}' already holds a blend tree. Use addChild to add to it, " +
                        "or DELETE it first.", 409);
                    return;
                }

                // AddObjectToAsset is the only route to a blend tree on an existing state:
                // CreateBlendTreeInController creates a state as well, and
                // CreateBlendTreeChild only nests under a tree that already exists. So the
                // hideFlags are ours to set, and HideInHierarchy is what the Animator
                // window produces -- measured against a hand-authored controller, whose
                // trees all carry it. CreateBlendTreeInController leaves None, which is
                // why it is not the model to copy.
                var tree = new BlendTree { name = ReadName(body, "New Blend Tree"), hideFlags = HideFlags.HideInHierarchy };

                if (!TryApplyTreeFields(controller, tree, body, response, out var ignored, validateOnly: true)) return;

                AssetDatabase.AddObjectToAsset(tree, controller);
                controller.SetStateEffectiveMotion(state, tree, layerIndex);
                TryApplyTreeFields(controller, tree, body, response, out ignored, validateOnly: false);

                Save(controller, undoGroup);
                SendCreated(response, layerIndex, state.name, new int[0], tree, ignored);
                return;
            }

            if (!TryResolveTree(controller, state, layerIndex, body, response, out var parent, out var path)) return;

            // Everything the request asks for is parsed and checked before the child is
            // created, so a rejected request leaves nothing behind. Creating first and
            // validating after would append a child -- and possibly a BlendTree sub-asset
            // -- that the caller was told did not happen.
            if (!TryReadChildMotion(body, response, out var clip, out var hasMotion)) return;
            if (!TryParseChildFields(body, response, out var childFields)) return;

            var childIsTree = !hasMotion;
            BlendTree childTree = null;
            if (childIsTree)
            {
                var probe = new BlendTree();
                var ok = TryApplyTreeFields(controller, probe, body, response, out _, validateOnly: true);
                Object.DestroyImmediate(probe);
                if (!ok) return;
            }

            var childIgnored = BlendTreeRules.CollectIgnoredChildFields(
                parent.blendType, parent.useAutomaticThresholds,
                positionSet: childFields.HasPosition,
                directBlendParameterSet: childFields.HasDirectBlendParameter,
                thresholdSet: childFields.HasThreshold);

            if (childIsTree)
            {
                // CreateBlendTreeChild owns the sub-asset and already sets HideInHierarchy.
                childTree = parent.CreateBlendTreeChild(childFields.Threshold);
                childTree.name = ReadName(body, "New Blend Tree");
                TryApplyTreeFields(controller, childTree, body, response, out _, validateOnly: false);
            }
            else
            {
                parent.AddChild(clip, childFields.Threshold);
            }

            var childIndex = parent.children.Length - 1;
            ApplyChildFields(parent, childIndex, childFields);

            Save(controller, undoGroup);

            var newPath = new List<int>(path) { childIndex };
            SendCreated(response, layerIndex, state.name, newPath.ToArray(), childTree, childIgnored);
        }

        // ── PATCH ────────────────────────────────────────────────────────────

        public void HandleUpdate(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response);
            if (controller == null) return;

            // Opened before the first mutation so that this request is one undo entry.
            var undoGroup = UndoGroups.Begin("UnionAir: Update Blend Tree");

            var body = RequestBodyReader.ReadString(request);
            if (!TryResolveState(controller, body, response, out var layerIndex, out var state)) return;
            if (!TryReadChildPath(body, response, out var path)) return;

            // The parent is resolved rather than the addressed motion, because a child
            // does not have to be a blend tree to be updated: threshold, position,
            // timeScale, cycleOffset, mirror and directBlendParameter all belong to the
            // entry, and most entries in a real tree hold a clip. Requiring the addressed
            // motion to be a tree made every clip child unreachable.
            if (!TryResolveParent(controller, state, layerIndex, path, response, out var parent)) return;

            BlendTree tree = null;
            if (path.Length == 0)
            {
                tree = parent;
            }
            else
            {
                var index = path[path.Length - 1];
                if (index < 0 || index >= parent.children.Length)
                {
                    RestResponse.SendNotFound(response,
                        $"childPath does not resolve: index {index} at depth {path.Length - 1}, " +
                        $"which has {parent.children.Length} child(ren).");
                    return;
                }
                tree = parent.children[index].motion as BlendTree;
            }

            var hasTreeFields = HasAnyTreeField(body);
            if (hasTreeFields && tree == null)
            {
                RestResponse.SendError(response,
                    "The addressed child holds a clip, so the blend tree fields do not apply to it. " +
                    "Address a blend tree, or send only the child fields.", 400);
                return;
            }

            // Everything is checked before a single field is written, tree fields and
            // child fields alike. Validating the child half after writing the tree half
            // would let a malformed position leave a partly updated tree behind a 400.
            if (tree != null && !TryApplyTreeFields(controller, tree, body, response, out _, validateOnly: true)) return;

            if (path.Length == 0 && RequestBodyReader.HasTopLevelField(body, "threshold"))
            {
                RestResponse.SendError(response,
                    "threshold addresses a child, and childPath names the root blend tree, which is not a child of anything.", 400);
                return;
            }

            if (!TryParseChildFields(body, response, out var childFields)) return;

            if (childFields.HasMotion && hasTreeFields)
            {
                // The tree fields would be written to a tree this same request replaces,
                // so they would take effect on an object nothing can reach afterwards.
                RestResponse.SendError(response,
                    "motion replaces what the child holds, so blend tree fields in the same request would " +
                    "be written to a tree this request discards. Send them separately.", 400);
                return;
            }

            var ignored = new List<string>();
            if (tree != null)
            {
                if (!TryApplyTreeFields(controller, tree, body, response, out var treeIgnored, validateOnly: false)) return;
                ignored.AddRange(treeIgnored);
            }

            if (path.Length > 0)
            {
                ignored.AddRange(BlendTreeRules.CollectIgnoredChildFields(
                    parent.blendType, parent.useAutomaticThresholds,
                    positionSet: childFields.HasPosition,
                    directBlendParameterSet: childFields.HasDirectBlendParameter,
                    thresholdSet: childFields.HasThreshold));

                var index = path[path.Length - 1];
                if (childFields.HasMotion)
                {
                    // Swapping a child's motion drops whatever was there. If that was a
                    // blend tree, Unity leaves it in the asset exactly as it does for a
                    // removed child, so the subtree is collected first and destroyed after.
                    var doomed = new List<BlendTree>();
                    CollectTrees(parent.children[index].motion as BlendTree, doomed);
                    ApplyChildFields(parent, index, childFields);
                    foreach (var t in doomed)
                        if (t != null) Object.DestroyImmediate(t, true);
                }
                else
                {
                    ApplyChildFields(parent, index, childFields);
                }
            }

            Save(controller, undoGroup);

            var sb = new StringBuilder();
            sb.Append("{\"layerIndex\":").Append(layerIndex);
            sb.Append(",\"state\":").Append(RestResponse.FormatNullableString(state.name));
            sb.Append(",\"childPath\":").Append(PathJson(path));
            AppendIgnored(sb, ignored);
            sb.Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        /// <summary>Whether the request carries any field that belongs to a blend tree rather than to a child entry.</summary>
        private static bool HasAnyTreeField(string body)
            => RequestBodyReader.HasTopLevelField(body, "name")
            || RequestBodyReader.HasTopLevelField(body, "blendType")
            || RequestBodyReader.HasTopLevelField(body, "blendParameter")
            || RequestBodyReader.HasTopLevelField(body, "blendParameterY")
            || RequestBodyReader.HasTopLevelField(body, "useAutomaticThresholds")
            || RequestBodyReader.HasTopLevelField(body, "minThreshold")
            || RequestBodyReader.HasTopLevelField(body, "maxThreshold");

        // ── DELETE ───────────────────────────────────────────────────────────

        public void HandleDelete(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            var controller = LoadController(guid, response);
            if (controller == null) return;

            // Opened before the first mutation so that this request is one undo entry.
            var undoGroup = UndoGroups.Begin("UnionAir: Delete Blend Tree");

            var body = RequestBodyReader.ReadString(request);
            if (!TryResolveState(controller, body, response, out var layerIndex, out var state)) return;
            if (!TryReadChildPath(body, response, out var path)) return;

            if (path.Length == 0)
            {
                var motion = controller.GetStateEffectiveMotion(state, layerIndex);
                if (!(motion is BlendTree))
                {
                    RestResponse.SendError(response, $"State '{state.name}' does not hold a blend tree.", 404);
                    return;
                }

                // Measured on 6000.0.80f1: clearing the motion destroys the tree and every
                // descendant, root included. Nothing to clean up by hand here.
                controller.SetStateEffectiveMotion(state, null, layerIndex);
                Save(controller, undoGroup);
                RestResponse.Send(response,
                    $"{{\"removed\":\"root\",\"layerIndex\":{layerIndex}," +
                    $"\"state\":{RestResponse.FormatNullableString(state.name)},\"childPath\":[]}}");
                return;
            }

            if (!TryResolveParent(controller, state, layerIndex, path, response, out var parent)) return;

            var index = path[path.Length - 1];
            if (index < 0 || index >= parent.children.Length)
            {
                RestResponse.SendError(response,
                    $"childPath does not resolve: index {index} at depth {path.Length - 1}, " +
                    $"which has {parent.children.Length} child(ren).", 404);
                return;
            }

            // RemoveChild detaches the entry and leaves the subtree in the asset with
            // nothing referring to it -- measured: removing a child holding two nested
            // trees left both in the .controller file. The subtree is collected before the
            // removal, while it is still reachable, and destroyed after.
            var doomed = new List<BlendTree>();
            CollectTrees(parent.children[index].motion as BlendTree, doomed);

            parent.RemoveChild(index);
            foreach (var t in doomed)
                if (t != null) Object.DestroyImmediate(t, true);

            Save(controller, undoGroup);
            RestResponse.Send(response,
                $"{{\"removed\":\"child\",\"layerIndex\":{layerIndex}," +
                $"\"state\":{RestResponse.FormatNullableString(state.name)},\"childPath\":{PathJson(path)}," +
                $"\"destroyedSubTrees\":{doomed.Count}}}");
        }

        /// <summary>
        /// Collects a blend tree and every blend tree beneath it, deepest last, so the
        /// caller can destroy them after detaching the subtree.
        /// </summary>
        internal static void CollectTrees(BlendTree tree, List<BlendTree> into)
        {
            if (tree == null) return;
            into.Add(tree);
            foreach (var child in tree.children)
                CollectTrees(child.motion as BlendTree, into);
        }

        // ── Address resolution ───────────────────────────────────────────────

        private static bool TryResolveState(
            AnimatorController controller, string body, UnionAirResponse response,
            out int layerIndex, out AnimatorState state)
        {
            state = null;
            layerIndex = RequestBodyReader.GetInt(body, "layerIndex") ?? 0;

            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                RestResponse.SendError(response,
                    $"layerIndex {layerIndex} is out of range; the controller has {controller.layers.Length} layer(s).", 400);
                return false;
            }

            var name = RequestBodyReader.GetString(body, "state");
            if (string.IsNullOrEmpty(name))
            {
                RestResponse.SendError(response, "Missing required field: state", 400);
                return false;
            }

            // Through the same address the state endpoints use, so that a state inside a
            // sub-state machine is reachable here too. Searching the layer root alone
            // answered "state not found" for a state the Animator window plainly shows, and
            // made a blend tree impossible to author anywhere but the top level.
            if (!AnimatorStateMachineAddress.TryResolve(controller, layerIndex, body, response, out var sm))
                return false;

            foreach (var child in sm.states)
            {
                if (child.state.name == name) { state = child.state; return true; }
            }

            RestResponse.SendNotFound(response,
                $"State not found in layer {layerIndex} at " +
                $"{AnimatorStateMachineRules.Describe(ReadPath(body))}: {name}");
            return false;
        }

        private static string[] ReadPath(string body)
        {
            RequestBodyReader.TryGetStringArray(body, "stateMachinePath", out var path);
            return path;
        }

        private static bool TryReadChildPath(string body, UnionAirResponse response, out int[] path)
        {
            path = new int[0];
            var raw = RequestBodyReader.GetRawArray(body, "childPath");
            if (string.IsNullOrEmpty(raw)) return true;

            var trimmed = raw.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[trimmed.Length - 1] != ']')
            {
                RestResponse.SendError(response, "childPath must be an array of child indices.", 400);
                return false;
            }

            var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
            if (inner.Length == 0) return true;

            var parts = inner.Split(',');
            var values = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                {
                    RestResponse.SendError(response, "childPath must be an array of child indices.", 400);
                    return false;
                }
            }
            path = values;
            return true;
        }

        private static bool TryResolveTree(
            AnimatorController controller, AnimatorState state, int layerIndex,
            string body, UnionAirResponse response, out BlendTree tree, out int[] path)
        {
            tree = null;
            if (!TryReadChildPath(body, response, out path)) return false;

            var motion = controller.GetStateEffectiveMotion(state, layerIndex);
            var current = motion as BlendTree;
            if (current == null)
            {
                RestResponse.SendNotFound(response, $"State '{state.name}' does not hold a blend tree.");
                return false;
            }

            for (int depth = 0; depth < path.Length; depth++)
            {
                var index = path[depth];
                if (index < 0 || index >= current.children.Length)
                {
                    RestResponse.SendNotFound(response,
                        $"childPath does not resolve: index {index} at depth {depth}, " +
                        $"which has {current.children.Length} child(ren).");
                    return false;
                }
                var next = current.children[index].motion as BlendTree;
                if (next == null)
                {
                    RestResponse.SendNotFound(response,
                        $"childPath does not resolve: the child at index {index}, depth {depth}, is not a blend tree.");
                    return false;
                }
                current = next;
            }

            tree = current;
            return true;
        }

        private static bool TryResolveParent(
            AnimatorController controller, AnimatorState state, int layerIndex,
            int[] path, UnionAirResponse response, out BlendTree parent)
        {
            parent = controller.GetStateEffectiveMotion(state, layerIndex) as BlendTree;
            if (parent == null)
            {
                RestResponse.SendNotFound(response, $"State '{state.name}' does not hold a blend tree.");
                return false;
            }

            for (int depth = 0; depth < path.Length - 1; depth++)
            {
                var index = path[depth];
                if (index < 0 || index >= parent.children.Length)
                {
                    RestResponse.SendNotFound(response,
                        $"childPath does not resolve: index {index} at depth {depth}, " +
                        $"which has {parent.children.Length} child(ren).");
                    return false;
                }
                var next = parent.children[index].motion as BlendTree;
                if (next == null)
                {
                    RestResponse.SendNotFound(response,
                        $"childPath does not resolve: the child at index {index}, depth {depth}, is not a blend tree.");
                    return false;
                }
                parent = next;
            }
            return true;
        }

        // ── Field application ────────────────────────────────────────────────

        /// <summary>
        /// Applies the tree-level fields, or with <paramref name="validateOnly"/> checks
        /// them and writes nothing. The two-pass shape is what makes a multi-field request
        /// atomic: every value is resolved against the controller before the first write.
        /// </summary>
        private static bool TryApplyTreeFields(
            AnimatorController controller, BlendTree tree, string body,
            UnionAirResponse response, out List<string> ignored, bool validateOnly)
        {
            ignored = new List<string>();

            var type = tree.blendType;
            if (RequestBodyReader.TryGetStringValue(body, "blendType", out var typeName, out var hasType) && hasType)
            {
                if (!BlendTreeRules.TryParseBlendType(typeName, out type))
                {
                    RestResponse.SendError(response,
                        $"Unknown blendType: {typeName}. Use one of {BlendTreeRules.BlendTypeNames}.", 400);
                    return false;
                }
                if (!validateOnly) tree.blendType = type;
            }

            if (!TryReadFloatParameter(controller, body, "blendParameter", response, out var blendParameter, out var hasP)) return false;
            if (hasP && !validateOnly) tree.blendParameter = blendParameter;

            if (!TryReadFloatParameter(controller, body, "blendParameterY", response, out var blendParameterY, out var hasPY)) return false;
            if (hasPY && !validateOnly) tree.blendParameterY = blendParameterY;

            if (RequestBodyReader.TryGetBoolValue(body, "useAutomaticThresholds", out var autoValue, out var hasAuto) && hasAuto && !validateOnly)
                tree.useAutomaticThresholds = autoValue;

            if (RequestBodyReader.TryGetFloatValue(body, "minThreshold", out var min, out var hasMin) && hasMin && !validateOnly)
                tree.minThreshold = min;
            if (RequestBodyReader.TryGetFloatValue(body, "maxThreshold", out var max, out var hasMax) && hasMax && !validateOnly)
                tree.maxThreshold = max;

            if (RequestBodyReader.TryGetStringValue(body, "name", out var name, out var hasName) && hasName && !validateOnly)
                tree.name = name;

            // auto is read here only so the caller can hand it to the child-field check;
            // whether a threshold survives is the parent's business, not this tree's.
            ignored = BlendTreeRules.CollectIgnoredTreeFields(type, hasPY);
            return true;
        }

        /// <summary>
        /// The child-entry fields a request carries, parsed and checked but not applied.
        ///
        /// Separating the two is what makes a request atomic: the child cannot be created
        /// or written until every value has resolved, so a malformed one is refused with
        /// the controller untouched.
        /// </summary>
        private struct ChildFields
        {
            public bool HasThreshold; public float Threshold;
            public bool HasPosition; public Vector2 Position;
            public bool HasTimeScale; public float TimeScale;
            public bool HasCycleOffset; public float CycleOffset;
            public bool HasMirror; public bool Mirror;
            public bool HasDirectBlendParameter; public string DirectBlendParameter;
            public bool HasMotion; public AnimationClip Motion;
        }

        private static bool TryParseChildFields(string body, UnionAirResponse response, out ChildFields fields)
        {
            fields = default(ChildFields);

            if (!RequestBodyReader.TryGetFloatValue(body, "threshold", out fields.Threshold, out fields.HasThreshold))
            {
                RestResponse.SendError(response, "threshold must be a number.", 400);
                return false;
            }

            var positionJson = RequestBodyReader.GetObject(body, "position");
            if (positionJson != null)
            {
                var x = RequestBodyReader.GetFloat(positionJson, "x");
                var y = RequestBodyReader.GetFloat(positionJson, "y");
                if (!x.HasValue || !y.HasValue)
                {
                    RestResponse.SendError(response, "position requires x and y.", 400);
                    return false;
                }
                fields.HasPosition = true;
                fields.Position = new Vector2(x.Value, y.Value);
            }
            else if (RequestBodyReader.HasTopLevelField(body, "position"))
            {
                RestResponse.SendError(response, "position must be an object such as {\"x\":0,\"y\":0}.", 400);
                return false;
            }

            if (!RequestBodyReader.TryGetFloatValue(body, "timeScale", out fields.TimeScale, out fields.HasTimeScale))
            {
                RestResponse.SendError(response, "timeScale must be a number.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetFloatValue(body, "cycleOffset", out fields.CycleOffset, out fields.HasCycleOffset))
            {
                RestResponse.SendError(response, "cycleOffset must be a number.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetBoolValue(body, "mirror", out fields.Mirror, out fields.HasMirror))
            {
                RestResponse.SendError(response, "mirror must be a boolean.", 400);
                return false;
            }
            if (!RequestBodyReader.TryGetStringValue(body, "directBlendParameter", out fields.DirectBlendParameter, out fields.HasDirectBlendParameter))
            {
                RestResponse.SendError(response, "directBlendParameter must be a string.", 400);
                return false;
            }

            if (!TryReadChildMotion(body, response, out fields.Motion, out fields.HasMotion)) return false;
            return true;
        }

        /// <summary>
        /// Writes parsed child fields. Cannot fail, which is the point: every check
        /// happened in <see cref="TryParseChildFields"/>, before anything was mutated.
        /// </summary>
        private static void ApplyChildFields(BlendTree parent, int childIndex, ChildFields fields)
        {
            var children = parent.children;
            if (childIndex < 0 || childIndex >= children.Length) return;

            var child = children[childIndex];
            if (fields.HasThreshold) child.threshold = fields.Threshold;
            if (fields.HasPosition) child.position = fields.Position;
            if (fields.HasTimeScale) child.timeScale = fields.TimeScale;
            if (fields.HasCycleOffset) child.cycleOffset = fields.CycleOffset;
            if (fields.HasMirror) child.mirror = fields.Mirror;
            if (fields.HasDirectBlendParameter) child.directBlendParameter = fields.DirectBlendParameter;
            if (fields.HasMotion) child.motion = fields.Motion;

            children[childIndex] = child;
            parent.children = children;
        }

        private static bool TryReadFloatParameter(
            AnimatorController controller, string body, string field,
            UnionAirResponse response, out string value, out bool present)
        {
            value = null;
            if (!RequestBodyReader.TryGetStringValue(body, field, out value, out present))
            {
                RestResponse.SendError(response, $"{field} must be a string.", 400);
                return false;
            }
            if (!present || string.IsNullOrEmpty(value)) return true;

            // A tree pointing at a parameter that does not exist is a broken controller,
            // and the read cannot tell it apart from a working one.
            foreach (var p in controller.parameters)
            {
                if (p.name != value) continue;
                if (p.type != AnimatorControllerParameterType.Float)
                {
                    RestResponse.SendError(response,
                        $"{field} '{value}' is a {p.type} parameter; a blend tree blends on a Float.", 400);
                    return false;
                }
                return true;
            }

            RestResponse.SendError(response,
                $"{field} '{value}' names no parameter on this controller. Add it first with " +
                "POST /api/assets/animator-controllers/{guid}/parameters.", 400);
            return false;
        }

        private static bool TryReadChildMotion(string body, UnionAirResponse response, out AnimationClip clip, out bool present)
        {
            clip = null;
            var motionJson = RequestBodyReader.GetObject(body, "motion");
            present = motionJson != null;
            if (!present) return true;

            var clipGuid = RequestBodyReader.GetString(motionJson, "guid");
            if (string.IsNullOrEmpty(clipGuid))
            {
                RestResponse.SendError(response, "motion requires a guid.", 400);
                return false;
            }
            var path = AssetDatabase.GUIDToAssetPath(clipGuid);
            clip = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                RestResponse.SendNotFound(response, $"No AnimationClip found for GUID: {clipGuid}");
                return false;
            }
            return true;
        }

        // ── Plumbing ─────────────────────────────────────────────────────────

        private static string ReadName(string body, string fallback)
        {
            var name = RequestBodyReader.GetString(body, "name");
            return string.IsNullOrEmpty(name) ? fallback : name;
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

        private static string PathJson(int[] path)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < path.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(path[i]);
            }
            return sb.Append("]").ToString();
        }

        private static void AppendIgnored(StringBuilder sb, List<string> ignored)
        {
            sb.Append(",\"ignored\":[");
            for (int i = 0; i < ignored.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(RestResponse.FormatNullableString(ignored[i]));
            }
            sb.Append("]");
        }

        private static void SendCreated(
            UnionAirResponse response, int layerIndex, string stateName, int[] path,
            BlendTree tree, List<string> ignored)
        {
            var sb = new StringBuilder();
            sb.Append("{\"created\":").Append(RestResponse.FormatNullableString(tree != null ? "BlendTree" : "AnimationClip"));
            sb.Append(",\"layerIndex\":").Append(layerIndex);
            sb.Append(",\"state\":").Append(RestResponse.FormatNullableString(stateName));
            sb.Append(",\"childPath\":").Append(PathJson(path));
            if (tree != null)
                sb.Append(",\"name\":").Append(RestResponse.FormatNullableString(tree.name));
            AppendIgnored(sb, ignored ?? new List<string>());
            sb.Append("}");
            RestResponse.Send(response, sb.ToString(), 201);
        }

        private static AnimatorController LoadController(string guid, UnionAirResponse response)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
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
    }
}
