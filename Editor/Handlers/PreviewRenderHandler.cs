using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace LeonAkasaka.UnionAir.Editor
{
    internal sealed class PreviewRenderHandler
    {
        internal const int MaxConcurrentPreviews = 8;
        private static int _activePreviews;

        internal static int ActivePreviewCount => Volatile.Read(ref _activePreviews);

        internal void Handle(UnionAirRequest request, UnionAirResponse response, bool binaryImage)
        {
            var body = RequestBodyReader.ReadString(request);
            if (!PreviewRenderRequestParser.TryParse(body, binaryImage, out var model, out var error))
            {
                RestResponse.SendError(response, error, 400);
                return;
            }

            if (!EditorTargetUtils.TryResolveTarget(
                    model.Target,
                    model.ScenePath,
                    "target",
                    out var resolvedTarget,
                    out error,
                    out var statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            var source = resolvedTarget as GameObject;
            if (source == null && resolvedTarget is Component component)
                source = component.gameObject;
            if (source == null)
            {
                RestResponse.SendError(response, "Target must resolve to a GameObject, prefab, or model asset.", 422);
                return;
            }

            AnimationClip requestedClip = null;
            if (model.Animation.Mode == PreviewAnimationMode.Clip &&
                !TryResolveClip(model.Animation, out requestedClip, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return;
            }

            if (!TryEnter())
            {
                RestResponse.SendError(
                    response,
                    "The preview concurrency limit of eight active requests has been reached.",
                    429);
                return;
            }

            var previewScene = default(Scene);
            PlayableGraph graph = default(PlayableGraph);
            try
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                var clone = InstantiateTarget(source, previewScene);
                if (clone == null)
                {
                    RestResponse.SendError(response, "Unity could not instantiate the preview target.", 422);
                    return;
                }
                clone.name = source.name;
                if (!clone.activeSelf) clone.SetActive(true);

                if (!TryResolveAnimator(
                        clone,
                        model.Animation,
                        out var animator,
                        out var animatorPath,
                        out error,
                        out statusCode))
                {
                    RestResponse.SendError(response, error, statusCode);
                    return;
                }

                var resolvedParameters = new List<ResolvedAnimatorParameter>();
                if (!TryValidateAnimation(
                        model.Animation,
                        animator,
                        requestedClip,
                        resolvedParameters,
                        out error))
                {
                    RestResponse.SendError(response, error, 422);
                    return;
                }

                if (animator != null)
                {
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind();
                }

                AnimationClipPlayable clipPlayable = default(AnimationClipPlayable);
                if (requestedClip != null)
                {
                    graph = PlayableGraph.Create("UnionAir Preview");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    clipPlayable = AnimationClipPlayable.Create(graph, requestedClip);
                    clipPlayable.SetApplyFootIK(false);
                    clipPlayable.SetApplyPlayableIK(false);
                    var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
                    output.SetSourcePlayable(clipPlayable);
                    graph.Play();
                }

                var focus = clone.transform;
                if (!string.IsNullOrEmpty(model.FocusPath))
                {
                    focus = clone.transform.Find(model.FocusPath);
                    if (focus == null)
                    {
                        RestResponse.SendError(
                            response,
                            "Focus path not found on preview target: " + model.FocusPath,
                            404);
                        return;
                    }
                }

                var camera = CreateCamera(previewScene, model);
                CreateLights(previewScene, model.Lighting);
                var frames = new List<PreviewFrameResult>(model.Times.Length);
                for (var i = 0; i < model.Times.Length; i++)
                {
                    var time = model.Times[i];
                    EvaluateAnimation(
                        model.Animation,
                        animator,
                        requestedClip,
                        clipPlayable,
                        graph,
                        resolvedParameters,
                        time);

                    if (!TryCollectBounds(focus, out var bounds))
                    {
                        RestResponse.SendError(
                            response,
                            "The preview target has no active renderer bounds" +
                            (string.IsNullOrEmpty(model.FocusPath) ? "." : " under focusPath '" + model.FocusPath + "'."),
                            422);
                        return;
                    }

                    var frame = RenderFrame(
                        camera,
                        clone,
                        animator,
                        requestedClip,
                        model,
                        bounds,
                        time);
                    frames.Add(frame);
                }

                if (binaryImage)
                {
                    RestResponse.SendBinary(
                        response,
                        frames[0].Image,
                        model.Format == "png" ? "image/png" : "image/jpeg");
                }
                else
                {
                    RestResponse.Send(
                        response,
                        BuildJson(source, animator, animatorPath, model, frames));
                }
            }
            catch (Exception ex)
            {
                RestResponse.SendError(response, "Preview render failed: " + ex.Message, 500);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
                Exit();
            }
        }

        private static bool TryEnter()
        {
            var active = Interlocked.Increment(ref _activePreviews);
            if (active <= MaxConcurrentPreviews) return true;
            Interlocked.Decrement(ref _activePreviews);
            return false;
        }

        private static void Exit()
            => Interlocked.Decrement(ref _activePreviews);

        private static bool TryResolveClip(
            PreviewAnimationSettings animation,
            out AnimationClip clip,
            out string error,
            out int statusCode)
        {
            clip = null;
            var guid = RequestBodyReader.GetString(animation.Clip, "assetGuid");
            var assetPath = RequestBodyReader.GetString(animation.Clip, "assetPath");
            if (!EditorTargetUtils.TryResolveAssetPath(
                    guid,
                    assetPath,
                    "animation.clip",
                    out _,
                    out var resolvedPath,
                    out error,
                    out statusCode))
                return false;

            var candidates = new List<AnimationClip>();
            var assets = AssetDatabase.LoadAllAssetsAtPath(resolvedPath);
            for (var i = 0; i < assets.Length; i++)
            {
                var candidate = assets[i] as AnimationClip;
                if (candidate != null &&
                    !candidate.name.StartsWith("__preview__", StringComparison.Ordinal))
                    candidates.Add(candidate);
            }

            if (!string.IsNullOrEmpty(animation.ClipName))
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].name != animation.ClipName) continue;
                    clip = candidates[i];
                    animation.ClipName = clip.name;
                    error = null;
                    statusCode = 200;
                    return true;
                }

                error = "AnimationClip '" + animation.ClipName + "' was not found at " + resolvedPath +
                        ". Available clips: " + ClipNames(candidates) + ".";
                statusCode = 404;
                return false;
            }

            if (candidates.Count == 1)
            {
                clip = candidates[0];
                animation.ClipName = clip.name;
                error = null;
                statusCode = 200;
                return true;
            }
            if (candidates.Count == 0)
            {
                error = "Asset is not an AnimationClip: " + resolvedPath;
                statusCode = 422;
                return false;
            }

            error = "Animation asset contains several clips; specify animation.clipName. Available clips: " +
                    ClipNames(candidates) + ".";
            statusCode = 409;
            return false;
        }

        private static string ClipNames(List<AnimationClip> clips)
        {
            var names = new string[clips.Count];
            for (var i = 0; i < clips.Count; i++) names[i] = clips[i].name;
            return string.Join(", ", names);
        }

        private static GameObject InstantiateTarget(GameObject source, Scene previewScene)
        {
            if (EditorUtility.IsPersistent(source))
            {
                var prefab = PrefabUtility.InstantiatePrefab(source, previewScene) as GameObject;
                if (prefab != null) return prefab;
            }

            var clone = UnityEngine.Object.Instantiate(source);
            if (clone.transform.parent != null)
                clone.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(clone, previewScene);
            return clone;
        }

        private static bool TryResolveAnimator(
            GameObject clone,
            PreviewAnimationSettings settings,
            out Animator animator,
            out string path,
            out string error,
            out int statusCode)
        {
            animator = null;
            path = null;
            error = null;
            statusCode = 422;

            if (!string.IsNullOrEmpty(settings.AnimatorPath))
            {
                var transform = clone.transform.Find(settings.AnimatorPath);
                if (transform == null)
                {
                    error = "animation.animatorPath was not found: " + settings.AnimatorPath;
                    statusCode = 404;
                    return false;
                }
                animator = transform.GetComponent<Animator>();
                if (animator == null)
                {
                    error = "No Animator exists at animation.animatorPath: " + settings.AnimatorPath;
                    statusCode = 404;
                    return false;
                }
                path = settings.AnimatorPath;
                return true;
            }

            var animators = clone.GetComponentsInChildren<Animator>(true);
            if (animators.Length == 1)
            {
                animator = animators[0];
                path = RelativePath(clone.transform, animator.transform);
                return true;
            }

            if (settings.Mode == PreviewAnimationMode.None)
            {
                if (animators.Length > 0)
                {
                    animator = animators[0];
                    path = RelativePath(clone.transform, animator.transform);
                }
                return true;
            }

            if (animators.Length == 0)
            {
                error = "Animation preview requires an Animator on the target.";
                return false;
            }

            error = "The target has several Animators; specify animation.animatorPath.";
            statusCode = 409;
            return false;
        }

        private static bool TryValidateAnimation(
            PreviewAnimationSettings settings,
            Animator animator,
            AnimationClip clip,
            List<ResolvedAnimatorParameter> resolvedParameters,
            out string error)
        {
            error = null;
            if (settings.Mode == PreviewAnimationMode.None) return true;
            if (animator == null)
            {
                error = "Animation preview requires an Animator on the target.";
                return false;
            }

            if (settings.Mode == PreviewAnimationMode.Clip)
            {
                if (clip == null)
                {
                    error = "Animation clip could not be resolved.";
                    return false;
                }
                return true;
            }

            if (animator.runtimeAnimatorController == null)
            {
                error = "State and parameter previews require a RuntimeAnimatorController.";
                return false;
            }

            if (settings.Mode == PreviewAnimationMode.State)
            {
                if (settings.Layer >= animator.layerCount)
                {
                    error = "Animation layer " + settings.Layer + " is outside the Animator layer range.";
                    return false;
                }
                var stateHash = Animator.StringToHash(settings.State);
                if (!animator.HasState(settings.Layer, stateHash))
                {
                    error = "Animator state not found on layer " + settings.Layer + ": " + settings.State;
                    return false;
                }
                return true;
            }

            var parameters = animator.parameters;
            for (var i = 0; i < settings.Parameters.Count; i++)
            {
                var request = settings.Parameters[i];
                AnimatorControllerParameter parameter = null;
                for (var j = 0; j < parameters.Length; j++)
                    if (parameters[j].name == request.Name) { parameter = parameters[j]; break; }
                if (parameter == null)
                {
                    error = "Animator parameter not found: " + request.Name;
                    return false;
                }

                var resolved = new ResolvedAnimatorParameter
                {
                    Hash = parameter.nameHash,
                    Type = parameter.type
                };
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float:
                        if (!RequestBodyReader.TryGetFloatValue(request.RawValue, "value", out resolved.FloatValue, out _))
                        {
                            error = "Animator parameter '" + request.Name + "' requires a finite number.";
                            return false;
                        }
                        break;
                    case AnimatorControllerParameterType.Int:
                        if (!RequestBodyReader.TryGetIntValue(request.RawValue, "value", out resolved.IntValue, out _))
                        {
                            error = "Animator parameter '" + request.Name + "' requires an integer.";
                            return false;
                        }
                        break;
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger:
                        if (!RequestBodyReader.TryGetBoolValue(request.RawValue, "value", out resolved.BoolValue, out _))
                        {
                            error = "Animator parameter '" + request.Name + "' requires a boolean.";
                            return false;
                        }
                        break;
                }
                resolvedParameters.Add(resolved);
            }
            return true;
        }

        private static Camera CreateCamera(Scene scene, PreviewRenderRequestModel model)
        {
            var go = new GameObject("UnionAir Preview Camera");
            SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = model.Background;
            camera.orthographic = false;
            camera.fieldOfView = model.View.FieldOfView;
            camera.nearClipPlane = 0.01f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            return camera;
        }

        private static void CreateLights(Scene scene, PreviewLightingSettings settings)
        {
            CreateLight(scene, "UnionAir Preview Key", settings.KeyIntensity, settings.KeyColor,
                Quaternion.Euler(40f, -35f, 0f));
            CreateLight(scene, "UnionAir Preview Fill", settings.FillIntensity, settings.FillColor,
                Quaternion.Euler(20f, 145f, 0f));
        }

        private static void CreateLight(
            Scene scene, string name, float intensity, Color color, Quaternion rotation)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = rotation;
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
        }

        private static void EvaluateAnimation(
            PreviewAnimationSettings settings,
            Animator animator,
            AnimationClip clip,
            AnimationClipPlayable clipPlayable,
            PlayableGraph graph,
            List<ResolvedAnimatorParameter> parameters,
            float time)
        {
            switch (settings.Mode)
            {
                case PreviewAnimationMode.None:
                    return;
                case PreviewAnimationMode.Clip:
                    clipPlayable.SetTime(time);
                    clipPlayable.SetDone(false);
                    graph.Evaluate(0f);
                    return;
                case PreviewAnimationMode.State:
                    animator.Rebind();
                    animator.Play(settings.State, settings.Layer, time);
                    animator.Update(0f);
                    return;
                case PreviewAnimationMode.Parameters:
                    animator.Rebind();
                    for (var i = 0; i < parameters.Count; i++)
                    {
                        var parameter = parameters[i];
                        switch (parameter.Type)
                        {
                            case AnimatorControllerParameterType.Float:
                                animator.SetFloat(parameter.Hash, parameter.FloatValue);
                                break;
                            case AnimatorControllerParameterType.Int:
                                animator.SetInteger(parameter.Hash, parameter.IntValue);
                                break;
                            case AnimatorControllerParameterType.Bool:
                                animator.SetBool(parameter.Hash, parameter.BoolValue);
                                break;
                            case AnimatorControllerParameterType.Trigger:
                                if (parameter.BoolValue) animator.SetTrigger(parameter.Hash);
                                else animator.ResetTrigger(parameter.Hash);
                                break;
                        }
                    }
                    animator.Update(0f);
                    if (time > 0f) animator.Update(time);
                    return;
            }
        }

        private static bool TryCollectBounds(Transform focus, out Bounds bounds)
        {
            bounds = default(Bounds);
            var initialized = false;
            var renderers = focus.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var candidate = renderer.bounds;
                if (!PreviewFraming.IsFinite(candidate)) continue;
                if (!initialized)
                {
                    bounds = candidate;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(candidate);
                }
            }
            return initialized && PreviewFraming.IsFinite(bounds);
        }

        private static PreviewFrameResult RenderFrame(
            Camera camera,
            GameObject clone,
            Animator animator,
            AnimationClip requestedClip,
            PreviewRenderRequestModel model,
            Bounds bounds,
            float time)
        {
            var rotation = PreviewFraming.CameraRotation(model.View.Yaw, model.View.Pitch);
            var distance = model.View.Distance ?? PreviewFraming.CalculateDistance(
                bounds,
                rotation,
                model.View.FieldOfView,
                (float)model.Width / model.Height,
                model.View.Padding);
            var position = bounds.center - (rotation * Vector3.forward) * distance;
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.farClipPlane = Math.Max(100f, distance + bounds.size.magnitude * 4f);

            var frame = new PreviewFrameResult
            {
                Time = time,
                Bounds = bounds,
                CameraPosition = position,
                CameraRotation = rotation,
                Distance = distance,
                Image = RenderPreviewCamera(
                    camera, model.Width, model.Height, model.Format, model.Quality)
            };

            PopulateAnimationResult(frame, clone, animator, requestedClip, model.Animation.Mode);
            return frame;
        }

        private static byte[] RenderPreviewCamera(
            Camera camera, int width, int height, string format, int quality)
        {
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(
                width,
                height,
                format == "png" ? TextureFormat.RGBA32 : TextureFormat.RGB24,
                false);
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                return format == "png" ? texture.EncodeToPNG() : texture.EncodeToJPG(quality);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(texture);
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static void PopulateAnimationResult(
            PreviewFrameResult frame,
            GameObject clone,
            Animator animator,
            AnimationClip requestedClip,
            PreviewAnimationMode mode)
        {
            var clips = new List<AnimationClip>();
            if (mode == PreviewAnimationMode.Clip)
            {
                clips.Add(requestedClip);
            }
            else if (animator != null && mode != PreviewAnimationMode.None)
            {
                for (var layer = 0; layer < animator.layerCount; layer++)
                {
                    var info = animator.GetCurrentAnimatorStateInfo(layer);
                    var state = new PreviewStateResult
                    {
                        Layer = layer,
                        FullPathHash = info.fullPathHash,
                        ShortNameHash = info.shortNameHash,
                        NormalizedTime = info.normalizedTime,
                        Length = info.length,
                        Loop = info.loop
                    };
                    var clipInfo = animator.GetCurrentAnimatorClipInfo(layer);
                    for (var i = 0; i < clipInfo.Length; i++)
                    {
                        var activeClip = clipInfo[i].clip;
                        if (activeClip == null) continue;
                        state.Clips.Add(new PreviewClipResult
                        {
                            Name = activeClip.name,
                            Weight = clipInfo[i].weight
                        });
                        if (!clips.Contains(activeClip)) clips.Add(activeClip);
                    }
                    frame.States.Add(state);
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < clips.Count; i++)
                AppendBindings(clone, clips[i], frame, seen);
        }

        private static void AppendBindings(
            GameObject clone,
            AnimationClip clip,
            PreviewFrameResult frame,
            HashSet<string> seen)
        {
            if (clip == null) return;
            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            for (var i = 0; i < floatBindings.Length; i++)
                AppendBinding(clone, floatBindings[i], frame, seen);
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            for (var i = 0; i < objectBindings.Length; i++)
                AppendBinding(clone, objectBindings[i], frame, seen);
        }

        private static void AppendBinding(
            GameObject clone,
            EditorCurveBinding binding,
            PreviewFrameResult frame,
            HashSet<string> seen)
        {
            var typeName = binding.type != null ? binding.type.FullName : "";
            var key = binding.path + "\n" + typeName + "\n" + binding.propertyName;
            if (!seen.Add(key)) return;

            var result = new PreviewBindingResult
            {
                Path = binding.path,
                Type = typeName,
                Property = binding.propertyName
            };
            var target = string.IsNullOrEmpty(binding.path)
                ? clone.transform
                : clone.transform.Find(binding.path);
            var applied = target != null && binding.type != null &&
                          (binding.type == typeof(GameObject) || target.GetComponent(binding.type) != null);
            (applied ? frame.AppliedBindings : frame.SkippedBindings).Add(result);
        }

        private static string BuildJson(
            GameObject source,
            Animator animator,
            string animatorPath,
            PreviewRenderRequestModel model,
            List<PreviewFrameResult> frames)
        {
            var assetPath = AssetDatabase.GetAssetPath(source);
            var isAsset = !string.IsNullOrEmpty(assetPath) && EditorUtility.IsPersistent(source);
            var sb = new StringBuilder();
            sb.Append("{\"target\":{");
            AppendStringField(sb, "kind", isAsset ? "asset" : "sceneObject");
            sb.Append(",");
            AppendStringField(sb, "name", source.name);
            if (isAsset)
            {
                sb.Append(","); AppendStringField(sb, "assetGuid", AssetDatabase.AssetPathToGUID(assetPath));
                sb.Append(","); AppendStringField(sb, "assetPath", assetPath);
            }
            else
            {
                sb.Append(","); AppendStringField(sb, "globalObjectId", ObjectIdUtils.GetGlobalObjectId(source));
                sb.Append(","); AppendStringField(sb, "scenePath", source.scene.path);
            }
            sb.Append("},\"focusPath\":").Append(RestResponse.FormatNullableString(model.FocusPath));
            sb.Append(",\"width\":").Append(model.Width);
            sb.Append(",\"height\":").Append(model.Height);
            sb.Append(",\"format\":\"").Append(model.Format).Append("\"");
            sb.Append(",\"mimeType\":\"").Append(model.Format == "png" ? "image/png" : "image/jpeg").Append("\"");
            sb.Append(",\"rigType\":\"").Append(RigType(animator)).Append("\"");
            sb.Append(",\"animatorPath\":").Append(RestResponse.FormatNullableString(animatorPath));
            sb.Append(",\"animation\":{");
            AppendStringField(sb, "mode", AnimationModeName(model.Animation.Mode));
            if (model.Animation.State != null)
            {
                sb.Append(","); AppendStringField(sb, "state", model.Animation.State);
                sb.Append(",\"layer\":").Append(model.Animation.Layer);
            }
            if (model.Animation.ClipName != null)
            {
                sb.Append(","); AppendStringField(sb, "clipName", model.Animation.ClipName);
            }
            sb.Append("},\"view\":{");
            AppendStringField(sb, "preset", model.View.Preset);
            sb.Append(",\"yaw\":").Append(RestResponse.FormatFloat(model.View.Yaw));
            sb.Append(",\"pitch\":").Append(RestResponse.FormatFloat(model.View.Pitch));
            sb.Append(",\"requestedDistance\":");
            sb.Append(model.View.Distance.HasValue ? RestResponse.FormatFloat(model.View.Distance.Value) : "null");
            sb.Append(",\"fieldOfView\":").Append(RestResponse.FormatFloat(model.View.FieldOfView));
            sb.Append(",\"padding\":").Append(RestResponse.FormatFloat(model.View.Padding));
            sb.Append("},\"background\":"); AppendColor(sb, model.Background);
            sb.Append(",\"lighting\":{\"model\":\"twoDirectionalNoShadows\",\"keyIntensity\":")
                .Append(RestResponse.FormatFloat(model.Lighting.KeyIntensity));
            sb.Append(",\"keyColor\":"); AppendColor(sb, model.Lighting.KeyColor);
            sb.Append(",\"fillIntensity\":").Append(RestResponse.FormatFloat(model.Lighting.FillIntensity));
            sb.Append(",\"fillColor\":"); AppendColor(sb, model.Lighting.FillColor);
            sb.Append("},\"frames\":[");
            for (var i = 0; i < frames.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendFrame(sb, frames[i], model.Format);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendFrame(StringBuilder sb, PreviewFrameResult frame, string format)
        {
            sb.Append("{\"time\":").Append(RestResponse.FormatFloat(frame.Time));
            sb.Append(",\"framing\":{\"bounds\":{");
            sb.Append("\"center\":"); AppendVector(sb, frame.Bounds.center);
            sb.Append(",\"size\":"); AppendVector(sb, frame.Bounds.size);
            sb.Append("},\"cameraPosition\":"); AppendVector(sb, frame.CameraPosition);
            sb.Append(",\"cameraRotation\":{");
            sb.Append("\"x\":").Append(RestResponse.FormatFloat(frame.CameraRotation.x));
            sb.Append(",\"y\":").Append(RestResponse.FormatFloat(frame.CameraRotation.y));
            sb.Append(",\"z\":").Append(RestResponse.FormatFloat(frame.CameraRotation.z));
            sb.Append(",\"w\":").Append(RestResponse.FormatFloat(frame.CameraRotation.w));
            sb.Append("},\"distance\":").Append(RestResponse.FormatFloat(frame.Distance)).Append("}");
            sb.Append(",\"states\":[");
            for (var i = 0; i < frame.States.Count; i++)
            {
                if (i > 0) sb.Append(",");
                AppendState(sb, frame.States[i]);
            }
            sb.Append("],\"appliedBindings\":["); AppendBindingsJson(sb, frame.AppliedBindings);
            sb.Append("],\"skippedBindings\":["); AppendBindingsJson(sb, frame.SkippedBindings);
            sb.Append("],\"mimeType\":\"").Append(format == "png" ? "image/png" : "image/jpeg").Append("\"");
            sb.Append(",\"image\":\"").Append(Convert.ToBase64String(frame.Image)).Append("\"}");
        }

        private static void AppendState(StringBuilder sb, PreviewStateResult state)
        {
            sb.Append("{\"layer\":").Append(state.Layer);
            sb.Append(",\"fullPathHash\":").Append(state.FullPathHash);
            sb.Append(",\"shortNameHash\":").Append(state.ShortNameHash);
            sb.Append(",\"normalizedTime\":").Append(RestResponse.FormatFloat(state.NormalizedTime));
            sb.Append(",\"length\":").Append(RestResponse.FormatFloat(state.Length));
            sb.Append(",\"loop\":").Append(RestResponse.FormatBool(state.Loop));
            sb.Append(",\"clips\":[");
            for (var i = 0; i < state.Clips.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{"); AppendStringField(sb, "name", state.Clips[i].Name);
                sb.Append(",\"weight\":").Append(RestResponse.FormatFloat(state.Clips[i].Weight)).Append("}");
            }
            sb.Append("]}");
        }

        private static void AppendBindingsJson(StringBuilder sb, List<PreviewBindingResult> bindings)
        {
            for (var i = 0; i < bindings.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{"); AppendStringField(sb, "path", bindings[i].Path);
                sb.Append(","); AppendStringField(sb, "type", bindings[i].Type);
                sb.Append(","); AppendStringField(sb, "property", bindings[i].Property);
                sb.Append("}");
            }
        }

        private static void AppendColor(StringBuilder sb, Color color)
        {
            sb.Append("{\"r\":").Append(RestResponse.FormatFloat(color.r));
            sb.Append(",\"g\":").Append(RestResponse.FormatFloat(color.g));
            sb.Append(",\"b\":").Append(RestResponse.FormatFloat(color.b));
            sb.Append(",\"a\":").Append(RestResponse.FormatFloat(color.a)).Append("}");
        }

        private static void AppendVector(StringBuilder sb, Vector3 vector)
        {
            sb.Append("{\"x\":").Append(RestResponse.FormatFloat(vector.x));
            sb.Append(",\"y\":").Append(RestResponse.FormatFloat(vector.y));
            sb.Append(",\"z\":").Append(RestResponse.FormatFloat(vector.z)).Append("}");
        }

        private static void AppendStringField(StringBuilder sb, string key, string value)
            => sb.Append("\"").Append(key).Append("\":\"")
                .Append(RestResponse.EscapeJson(value)).Append("\"");

        private static string AnimationModeName(PreviewAnimationMode mode)
        {
            switch (mode)
            {
                case PreviewAnimationMode.Clip: return "clip";
                case PreviewAnimationMode.State: return "state";
                case PreviewAnimationMode.Parameters: return "parameters";
                default: return "none";
            }
        }

        private static string RigType(Animator animator)
        {
            if (animator == null) return "none";
            return animator.isHuman ? "humanoid" : "generic";
        }

        private static string RelativePath(Transform root, Transform child)
        {
            if (root == child) return "";
            var names = new List<string>();
            for (var current = child; current != null && current != root; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private sealed class ResolvedAnimatorParameter
        {
            internal int Hash;
            internal AnimatorControllerParameterType Type;
            internal float FloatValue;
            internal int IntValue;
            internal bool BoolValue;
        }
    }
}
