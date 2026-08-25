namespace LeonAkasaka.UnionAir.Editor
{
    [UnionAirController("help")]
    internal sealed class HelpController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Returns the API manifest. Use ?detail=full for examples, ?category=<id> to filter by category (e.g. sceneWrite, read, assetWrite, playMode, editorActions, testRunner, profiling).",
            OptionalQuery = new string[] { "detail", "category", "source", "includeDisabled" })]
        private void Help(UnionAirRequestContext ctx)
            => new HelpHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("health")]
    internal sealed class HealthController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Checks whether the server is running and identifies its Unity project.",
            ResponseExample = "{\"status\":\"ok\",\"unityVersion\":\"6000.0.80f1\",\"projectPath\":\"C:\\\\Work\\\\MyProject\"}")]
        private void Health(UnionAirRequestContext ctx)
            => new HealthHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("editor")]
    internal sealed class EditorController
    {
        [UnionAirEndpoint("GET", "status",
            Category = UnionAirEndpointCategories.Read,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Returns the Unity Editor execution status. After POST /api/editor/refresh, retry through any domain reload and wait until settled is true before making dependent calls. lifecycleGeneration increments on every domain reload, so a client whose connection dropped can confirm a reload happened rather than a crash; sessionId changes only when the Editor process restarts.",
            ResponseExample = "{\"isPlaying\":false,\"isPaused\":false,\"isCompiling\":false,\"isUpdating\":false,\"unityVersion\":\"6000.0.23f1\",\"isTestRunning\":false,\"testRunSource\":null,\"testRunId\":null,\"sessionId\":\"f40cbf3fc3224a97b5b7ac7aa3b1ea38\",\"lifecycleGeneration\":3,\"settled\":true,\"hasCompileErrors\":false,\"compileState\":null,\"compileId\":null,\"compileSource\":null}")]
        private void Status(UnionAirRequestContext ctx)
            => new EditorStatusHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "logs",
            Category = UnionAirEndpointCategories.Read,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Returns captured Unity Console logs, newest first, retained across domain reloads. The type filter is case-insensitive; unknown types return 400. Use the exclusive 'since' cursor with the previous 'latestSequence' to fetch only new entries, and discard the cursor whenever 'sessionId' changes.",
            OptionalQuery = new string[] { "type", "search", "limit", "since" },
            ResponseExample = "{\"sessionId\":\"3f2a9c81\",\"count\":1,\"oldestSequence\":0,\"latestSequence\":42,\"truncated\":false,\"hasMore\":false,\"logs\":[{\"sequence\":42,\"type\":\"error\",\"message\":\"NullReferenceException\",\"stackTrace\":\"\",\"timestamp\":\"2026-07-28T02:11:00.1234567Z\"}]}")]
        private void Logs(UnionAirRequestContext ctx)
            => new EditorLogsHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "logs.ndjson",
            Category = UnionAirEndpointCategories.Read,
            TestRunPolicy = UnionAirTestRunPolicy.Allowed,
            Summary = "Downloads retained NDJSON Console logs for the current Editor session, including entries already evicted from the in-memory buffer. Concatenates the same-session rotated predecessor and active file in oldest-first order; at most these two files are retained.")]
        private void LogsFile(UnionAirRequestContext ctx)
            => new EditorLogsHandler().HandleDownload(ctx);

        [UnionAirEndpoint("GET", "selection",
            Category = UnionAirEndpointCategories.EditorActions,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.EditorState,
            Summary = "Returns the current Unity Editor selection.")]
        private void GetSelection(UnionAirRequestContext ctx)
            => new EditorSelectionHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "selection",
            Category = UnionAirEndpointCategories.EditorActions,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.EditorState,
            Summary = "Sets or clears the current Unity Editor selection.",
            OptionalBody = new string[] { "target", "targets", "activeIndex", "scenePath", "clear" })]
        private void SetSelection(UnionAirRequestContext ctx)
            => new EditorSelectionHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "ping",
            Category = UnionAirEndpointCategories.EditorActions,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.EditorState,
            Summary = "Highlights a Unity Editor object without changing the selection.",
            RequiredBody = new string[] { "target" },
            OptionalBody = new string[] { "scenePath" })]
        private void Ping(UnionAirRequestContext ctx)
            => new EditorPingHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "refresh",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Refreshes the Unity AssetDatabase and triggers script recompilation. Returns 409 before refreshing when a loaded scene changed externally, preventing Unity's interactive Reload dialog; explicitly save or unload the reported scenes first. If scripts changed, retry GET /api/editor/status through the domain reload and wait until both isUpdating and isCompiling are false before making dependent calls.",
            ResponseExample = "{\"refreshed\":true,\"isCompiling\":true,\"isUpdating\":false,\"isPlaying\":false}")]
        private void Refresh(UnionAirRequestContext ctx)
            => new EditorRefreshHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "menu-item",
            Category = UnionAirEndpointCategories.EditorActions,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.RequestDependent,
            Summary = "Executes a Unity Editor menu item.",
            RequiredBody = new string[] { "path" })]
        private void MenuItem(UnionAirRequestContext ctx)
            => new EditorMenuItemHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "menu-items",
            Category = UnionAirEndpointCategories.EditorActions,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.EditorState,
            Summary = "Lists currently discoverable Unity Editor menu item paths.",
            OptionalQuery = new string[] { "root", "search", "includeFolders", "includeAttributeFallback", "limit" })]
        private void MenuItems(UnionAirRequestContext ctx)
            => new EditorMenuItemsHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "play",
            Category = UnionAirEndpointCategories.PlayMode,
            // Entering Play mode during a compilation loses the request: Unity reloads the domain
            // when the cycle finishes and the mode change is discarded with it. Exiting, pausing,
            // and stepping are deliberately not blocked, because stopping a running game is exactly
            // what a client needs to be able to do while something else is in flight.
            BlockedDuring = UnionAirActivity.Compile,
            Summary = "Requests entering Play mode. An optional 'inputs' list schedules frame-accurate input, replayed from the first Play mode frame; 'frame' is the frame the game observes the input on. The whole list is validated before Play mode is entered, so an invalid entry returns 400 and nothing happens. With 'inputs' the response is 202 and carries the replay id to poll through GET /api/playmode/input/result; without it the response is unchanged. Requires the com.unity.inputsystem package.",
            OptionalBody = new string[] { "inputs" },
            RequestExample = "{\"inputs\":[{\"frame\":5,\"type\":\"perform\",\"action\":\"Player/Jump\",\"mode\":\"press\"},{\"frame\":8,\"type\":\"perform\",\"action\":\"Player/Jump\",\"mode\":\"release\"}]}",
            ResponseExample = "{\"playing\":true,\"replay\":{\"id\":\"ir-20260729-083741-cb2984\",\"state\":\"queued\",\"eventCount\":2,\"statusUrl\":\"/api/playmode/input/result?id=ir-20260729-083741-cb2984\"},\"note\":\"Poll GET /api/playmode/input/result until state leaves queued and running.\"}")]
        private void Play(UnionAirRequestContext ctx)
            => new EditorPlayHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "stop",
            Category = UnionAirEndpointCategories.PlayMode,
            Summary = "Requests exiting Play mode.")]
        private void Stop(UnionAirRequestContext ctx)
            => new EditorPlayHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "pause",
            Category = UnionAirEndpointCategories.PlayMode,
            Summary = "Sets or toggles pause state.",
            OptionalBody = new string[] { "paused" })]
        private void Pause(UnionAirRequestContext ctx)
            => new EditorPlayHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "step",
            Category = UnionAirEndpointCategories.PlayMode,
            Summary = "Advances one frame while paused in Play mode.")]
        private void Step(UnionAirRequestContext ctx)
            => new EditorPlayHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "capture",
            Category = UnionAirEndpointCategories.Read,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Captures the current view as a base64 image. In Play mode captures the current GameView frame and resizes the output image; it does not re-render at width/height. In Edit mode renders the last active Scene View.",
            OptionalQuery = new string[] { "width", "height", "format", "quality" },
            ResponseExample = "{\"source\":\"screen\",\"cameraName\":\"Main Camera\",\"width\":1920,\"height\":1080,\"format\":\"jpeg\",\"mimeType\":\"image/jpeg\",\"image\":\"<base64>\"}")]
        private void CaptureView(UnionAirRequestContext ctx)
            => new EditorCaptureHandler().HandleCapture(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "capture/image",
            Category = UnionAirEndpointCategories.Read,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Captures the current view and returns binary image data. Follows the same sizing rules as GET /api/editor/capture.",
            OptionalQuery = new string[] { "width", "height", "format", "quality" })]
        private void CaptureViewImage(UnionAirRequestContext ctx)
            => new EditorCaptureHandler().HandleCaptureImage(ctx.Request, ctx.Response);
    }

    [UnionAirController("cameras")]
    internal sealed class CameraController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Lists Camera components in the scene.")]
        private void List(UnionAirRequestContext ctx)
            => new CameraHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "capture",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Renders a camera and returns a base64 image.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "width", "height", "format", "quality" },
            ResponseExample = "{\"cameraPath\":\"Main Camera\",\"width\":1280,\"height\":720,\"format\":\"png\",\"mimeType\":\"image/png\",\"image\":\"<base64>\"}")]
        private void Capture(UnionAirRequestContext ctx)
            => new CameraHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "capture/image",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Renders a camera and returns binary image data.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "width", "height", "format", "quality" })]
        private void CaptureImage(UnionAirRequestContext ctx)
            => new CameraHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("previews")]
    internal sealed class PreviewController
    {
        private const UnionAirActivity PreviewBlockers =
            UnionAirActivity.Compile |
            UnionAirActivity.AssetUpdate |
            UnionAirActivity.Build |
            UnionAirActivity.BuildTargetSwitch;

        [UnionAirEndpoint("POST", "render",
            Category = UnionAirEndpointCategories.Read,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            BlockedDuring = PreviewBlockers,
            Summary = "Renders a scene GameObject, prefab, or model in an isolated preview scene. Optional clip, Animator state, or parameter evaluation and all requested times are sampled and rendered atomically. The response reports projected-bounds framing, lighting, resolved Animator states, applied/skipped bindings, and base64 images without changing the user's scene, selection, or assets.",
            RequiredBody = new string[] { "target" },
            OptionalBody = new string[] { "scenePath", "focusPath", "width", "height", "format", "quality", "times", "view", "background", "lighting", "animation" },
            RequestExample = "{\"target\":{\"assetGuid\":\"abc123...\"},\"focusPath\":\"Head\",\"times\":[0,0.5,1],\"view\":{\"preset\":\"front\"},\"animation\":{\"mode\":\"state\",\"state\":\"Base Layer.Idle\",\"layer\":0}}",
            ResponseExample = "{\"target\":{\"kind\":\"asset\",\"name\":\"Character\"},\"rigType\":\"humanoid\",\"lighting\":{\"model\":\"twoDirectionalNoShadows\"},\"frames\":[{\"time\":0,\"framing\":{\"distance\":3.1},\"states\":[{\"layer\":0,\"fullPathHash\":123}],\"appliedBindings\":[],\"skippedBindings\":[],\"mimeType\":\"image/png\",\"image\":\"<base64>\"}]}")]
        private void Render(UnionAirRequestContext ctx)
            => new PreviewRenderHandler().Handle(ctx.Request, ctx.Response, false);

        [UnionAirEndpoint("POST", "render/image",
            Category = UnionAirEndpointCategories.Read,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            BlockedDuring = PreviewBlockers,
            Summary = "Renders exactly one isolated preview frame and returns binary PNG or JPEG data. Uses the same request as POST /api/previews/render but requires exactly one time.",
            RequiredBody = new string[] { "target" },
            OptionalBody = new string[] { "scenePath", "focusPath", "width", "height", "format", "quality", "times", "view", "background", "lighting", "animation" },
            RequestExample = "{\"target\":{\"type\":\"hierarchyPath\",\"value\":\"Character\"},\"times\":[0],\"format\":\"png\"}")]
        private void RenderImage(UnionAirRequestContext ctx)
            => new PreviewRenderHandler().Handle(ctx.Request, ctx.Response, true);
    }

    [UnionAirController("playmode/ui")]
    internal sealed class PlayModeUiController
    {
        [UnionAirEndpoint("GET", "elements",
            Category = UnionAirEndpointCategories.PlayMode,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Lists interactable Unity UI and TextMeshPro UI elements in the running scene. Only available in Play mode.",
            OptionalQuery = new string[] { "scenePath" })]
        private void Elements(UnionAirRequestContext ctx)
            => new PlayModeUiHandler().HandleElements(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "click",
            Category = UnionAirEndpointCategories.PlayMode,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Clicks a Unity UI Button or IPointerClickHandler target. target must be an ObjectRef object such as {type: hierarchyPath, value: Canvas/Button}. Only available in Play mode.",
            RequiredBody = new string[] { "target" },
            OptionalBody = new string[] { "scenePath", "backend", "normalizedPosition" },
            RequestExample = "{\"target\":{\"type\":\"hierarchyPath\",\"value\":\"Canvas/Button\"}}",
            ResponseExample = "{\"success\":true,\"backend\":\"unityUi\",\"action\":\"click\",\"path\":\"Canvas/Button\",\"clicked\":true}")]
        private void Click(UnionAirRequestContext ctx)
            => new PlayModeUiHandler().HandleClick(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "text",
            Category = UnionAirEndpointCategories.PlayMode,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Sets text on a Unity UI InputField or TMP_InputField and optionally submits it. Only available in Play mode.",
            RequiredBody = new string[] { "target", "text" },
            OptionalBody = new string[] { "scenePath", "backend", "submit" },
            RequestExample = "{\"target\":{\"type\":\"hierarchyPath\",\"value\":\"Canvas/Input\"},\"text\":\"hello\",\"submit\":true}",
            ResponseExample = "{\"success\":true,\"backend\":\"unityUi\",\"action\":\"text\",\"path\":\"Canvas/Input\",\"text\":\"hello\"}")]
        private void Text(UnionAirRequestContext ctx)
            => new PlayModeUiHandler().HandleText(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "scroll",
            Category = UnionAirEndpointCategories.PlayMode,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Scrolls a Unity UI ScrollRect by delta or normalized position. Only available in Play mode.",
            RequiredBody = new string[] { "target" },
            OptionalBody = new string[] { "scenePath", "backend", "delta", "normalizedPosition" },
            RequestExample = "{\"target\":{\"type\":\"hierarchyPath\",\"value\":\"Canvas/List\"},\"delta\":{\"x\":0,\"y\":-1}}",
            ResponseExample = "{\"success\":true,\"backend\":\"unityUi\",\"action\":\"scroll\",\"path\":\"Canvas/List\",\"normalizedPosition\":{\"x\":0,\"y\":1}}")]
        private void Scroll(UnionAirRequestContext ctx)
            => new PlayModeUiHandler().HandleScroll(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "value",
            Category = UnionAirEndpointCategories.PlayMode,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Sets a Unity UI Toggle, Slider, Dropdown, or TMP_Dropdown value. Only available in Play mode.",
            RequiredBody = new string[] { "target", "value" },
            OptionalBody = new string[] { "scenePath", "backend" },
            RequestExample = "{\"target\":{\"type\":\"hierarchyPath\",\"value\":\"Canvas/Toggle\"},\"value\":true}",
            ResponseExample = "{\"success\":true,\"backend\":\"unityUi\",\"action\":\"value\",\"path\":\"Canvas/Toggle\",\"value\":true}")]
        private void Value(UnionAirRequestContext ctx)
            => new PlayModeUiHandler().HandleValue(ctx.Request, ctx.Response);
    }

    [UnionAirController("playmode/screen")]
    internal sealed class PlayModeScreenController
    {
        [UnionAirEndpoint("POST", "hittest",
            Category = UnionAirEndpointCategories.PlayMode,
            PlayModePolicy = UnionAirPlayModePolicy.Allowed,
            Summary = "Read-only: raycasts a screen point (EventSystem + Physics) and reports what a pointer click there would hit. Only available in Play mode.",
            OptionalBody = new string[] { "position", "normalizedPosition", "origin" },
            RequestExample = "{\"normalizedPosition\":{\"x\":0.5,\"y\":0.5},\"origin\":\"topLeft\"}",
            ResponseExample = "{\"success\":true,\"position\":{\"x\":640,\"y\":360},\"screenSize\":{\"width\":1280,\"height\":720},\"eventSystemHits\":[{\"path\":\"Cube\",\"globalObjectId\":\"<id>\",\"module\":\"UnityEngine.EventSystems.PhysicsRaycaster\",\"distance\":9.4}],\"physicsCamera\":\"Main Camera\",\"physicsHit\":{\"path\":\"Cube\",\"globalObjectId\":\"<id>\",\"distance\":9.4,\"point\":[0.1,0.5,-2]}}")]
        private void HitTest(UnionAirRequestContext ctx)
            => PlayModeScreenHandler.HandleHitTest(ctx.Request, ctx.Response);
    }

    [UnionAirController("scene")]
    internal sealed class SceneController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns metadata for a loaded scene.",
            OptionalQuery = new string[] { "scenePath" })]
        private void Info(UnionAirRequestContext ctx)
            => new SceneHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "hierarchy",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns the scene GameObject hierarchy.",
            OptionalQuery = new string[] { "scenePath", "depth", "compact", "limit", "path" })]
        private void Hierarchy(UnionAirRequestContext ctx)
            => new SceneHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "stats",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns aggregate scene statistics.",
            OptionalQuery = new string[] { "scenePath" })]
        private void Stats(UnionAirRequestContext ctx)
            => new SceneStatsHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "save",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Saves the active scene. Provide assetPath to save a new scene to a specific location (e.g. Assets/Scenes/MyScene.unity).",
            OptionalBody = new string[] { "assetPath" })]
        private void Save(UnionAirRequestContext ctx)
            => new SceneSaveHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("scenes")]
    internal sealed class ScenesController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Lists loaded scenes.")]
        private void List(UnionAirRequestContext ctx)
            => new ScenesHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "new",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Creates a new scene.",
            OptionalBody = new string[] { "mode", "setup", "discardUnsaved" })]
        private void New(UnionAirRequestContext ctx)
            => new ScenesHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "open",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Opens a scene asset.",
            RequiredBody = new string[] { "path" },
            OptionalBody = new string[] { "mode", "discardUnsaved" })]
        private void Open(UnionAirRequestContext ctx)
            => new ScenesHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "unload",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Unloads a loaded scene.",
            RequiredBody = new string[] { "path or name" },
            OptionalBody = new string[] { "discardUnsaved" })]
        private void Unload(UnionAirRequestContext ctx)
            => new ScenesHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "active",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Sets the active scene.",
            RequiredBody = new string[] { "path or name" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "allowWhilePlaying" })]
        private void Active(UnionAirRequestContext ctx)
            => new ScenesHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("gameobjects")]
    internal sealed class GameObjectsController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns GameObject details including components. An object reference in components[].properties is spelled the way PATCH /api/gameobjects/components reads it, so a value can be sent straight back: an asset as {assetGuid, assetPath, assetType, localIdentifier}, an object in a loaded scene as {type: globalObjectId, value: ...}. 'type' means the kind of reference and not the object's class, which is 'assetType'. There is no display name, because no field of the write carries one. A scene that has never been saved has no GlobalObjectId for its objects, and a built-in Unity resource is addressed by GUID and file id together, which localIdentifier carries, so reading one and sending it back resolves. A SkinnedMeshRenderer also carries 'blendShapeNames' beside 'properties': the mesh's shape names in mesh order, indexed by the same integer that indexes m_BlendShapeWeights. 'worldTransform' reports the object's world position, rotation, lossyScale and its right/up/forward axes as unit vectors, beside the parent-relative 'transform' the write accepts. A Renderer carries 'bounds', the world-space AABB as center and extents; the serialized m_AABB is the local one.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath" },
            ResponseExample = "{\"name\":\"Cube\",\"path\":\"Cube\",\"globalObjectId\":\"GlobalObjectId_V1-...\",\"isActive\":true,\"tag\":\"Untagged\",\"layer\":0,\"components\":[{\"type\":\"UnityEngine.MeshRenderer\",\"globalObjectId\":\"GlobalObjectId_V1-...\",\"enabled\":true,\"properties\":{\"m_Materials\":[{\"assetGuid\":\"a1b2...\",\"assetPath\":\"Assets/Materials/Rock.mat\",\"assetType\":\"UnityEngine.Material\",\"localIdentifier\":\"2100000\"}],\"m_ProbeAnchor\":{\"type\":\"globalObjectId\",\"value\":\"GlobalObjectId_V1-...\"}}}]}")]
        private void Detail(UnionAirRequestContext ctx)
            => new GameObjectHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Creates an empty GameObject.",
            RequiredBody = new string[] { "name" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "parent", "scenePath", "allowWhilePlaying" },
            RequestExample = "{\"name\":\"Brick\",\"parent\":{\"type\":\"hierarchyPath\",\"value\":\"BrickGroup\"}}",
            ResponseExample = "{\"name\":\"Brick\",\"path\":\"BrickGroup/Brick\",\"globalObjectId\":\"...\"}")]
        private void Create(UnionAirRequestContext ctx)
            => new GameObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("DELETE", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Deletes a GameObject.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" })]
        private void Delete(UnionAirRequestContext ctx)
            => new GameObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Updates GameObject properties. Query: target={\\\"type\\\":\\\"hierarchyPath\\\",\\\"value\\\":\\\"Path/To/Object\\\"}",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" },
            OptionalBody = new string[] { "name", "isActive", "tag", "layer", "transform", "allowWhilePlaying" },
            RequestExample = "{\"transform\":{\"position\":{\"x\":0,\"y\":1,\"z\":5},\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1}}}",
            ResponseExample = "{\"name\":\"Ball\",\"path\":\"Ball\",\"globalObjectId\":\"...\",\"isActive\":true,\"tag\":\"Untagged\",\"layer\":0,\"transform\":{\"position\":{\"x\":0,\"y\":1,\"z\":5},\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1}}}")]
        private void Update(UnionAirRequestContext ctx)
            => new GameObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "primitive",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Creates a primitive GameObject with optional transform in a single call.",
            RequiredBody = new string[] { "type" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "name", "parent", "transform", "scenePath", "allowWhilePlaying" },
            RequestExample = "{\"type\":\"Cube\",\"name\":\"Wall\",\"parent\":{\"type\":\"hierarchyPath\",\"value\":\"Level\"},\"transform\":{\"position\":{\"x\":0,\"y\":0.5,\"z\":0},\"scale\":{\"x\":10,\"y\":1,\"z\":0.2}}}",
            ResponseExample = "{\"name\":\"Wall\",\"path\":\"Level/Wall\",\"globalObjectId\":\"...\",\"primitiveType\":\"Cube\",\"transform\":{\"position\":{\"x\":0,\"y\":0.5,\"z\":0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":10,\"y\":1,\"z\":0.2}},\"components\":[\"Transform\",\"MeshFilter\",\"MeshRenderer\",\"BoxCollider\"]}")]
        private void Primitive(UnionAirRequestContext ctx)
            => new GameObjectPrimitiveHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "instantiate",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Instantiates a prefab asset into the scene. Supply either 'guid' or 'assetPath'; guid takes precedence when both are present.",
            RequiredBody = new string[] { "guid or assetPath" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "name", "parent", "scenePath", "allowWhilePlaying" },
            RequestExample = "{\"guid\":\"a1b2c3d4e5f67890a1b2c3d4e5f67890\",\"name\":\"Enemy_01\",\"parent\":{\"type\":\"hierarchyPath\",\"value\":\"Enemies\"}}",
            ResponseExample = "{\"name\":\"Enemy_01\",\"path\":\"Enemies/Enemy_01\",\"globalObjectId\":\"...\",\"prefabAssetPath\":\"Assets/Prefabs/Enemy.prefab\",\"components\":[\"Transform\",\"Animator\",\"Rigidbody\"]}")]
        private void Instantiate(UnionAirRequestContext ctx)
            => new GameObjectInstantiateHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "duplicate",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Duplicates a GameObject.",
            RequiredQuery = new string[] { "source" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" })]
        private void Duplicate(UnionAirRequestContext ctx)
            => new GameObjectDuplicateHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "reparent",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Moves a GameObject to a new parent. Both 'target' and 'parent' are ObjectRefs. Omit 'parent' to move to scene root.",
            RequiredBody = new string[] { "target" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "parent", "scenePath", "allowWhilePlaying" },
            RequestExample = "{\"target\":{\"type\":\"hierarchyPath\",\"value\":\"HUD/Score\"},\"parent\":{\"type\":\"hierarchyPath\",\"value\":\"Canvas/UI\"}}",
            ResponseExample = "{\"reparented\":\"Canvas/UI/Score\",\"globalObjectId\":\"...\"}")]
        private void Reparent(UnionAirRequestContext ctx)
            => new GameObjectReparentHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "batch",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Runs multiple GameObject operations in one Undo group. Each operation object requires 'op': create | create_primitive | update | delete. Returns 207 Multi-Status.",
            RequiredBody = new string[] { "operations" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "scenePath", "target", "parent", "allowWhilePlaying" },
            RequestExample = "{\"operations\":[{\"op\":\"create_primitive\",\"type\":\"Cube\",\"name\":\"Wall\",\"parent\":{\"type\":\"hierarchyPath\",\"value\":\"Level\"},\"transform\":{\"position\":{\"x\":0,\"y\":0.5,\"z\":0},\"scale\":{\"x\":10,\"y\":1,\"z\":0.2}}},{\"op\":\"update\",\"target\":{\"type\":\"hierarchyPath\",\"value\":\"Ball\"},\"isActive\":false},{\"op\":\"delete\",\"target\":{\"type\":\"hierarchyPath\",\"value\":\"OldObj\"}}]}",
            ResponseExample = "{\"processed\":3,\"failed\":0,\"results\":[{\"index\":0,\"success\":true,\"path\":\"Level/Wall\",\"globalObjectId\":\"...\"},{\"index\":1,\"success\":true,\"path\":\"Ball\",\"globalObjectId\":\"...\"},{\"index\":2,\"success\":true,\"path\":\"OldObj\",\"globalObjectId\":\"...\"}]}")]
        private void Batch(UnionAirRequestContext ctx)
            => new GameObjectBatchHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("gameobjects/components")]
    internal sealed class ComponentsController
    {
        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Adds a component to a GameObject. Use the C# type name for 'type'.",
            RequiredBody = new string[] { "target", "type" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "scenePath", "allowWhilePlaying" },
            RequestExample = "{\"target\":{\"type\":\"hierarchyPath\",\"value\":\"Ball\"},\"type\":\"Rigidbody\"}",
            ResponseExample = "{\"added\":\"Rigidbody\",\"target\":\"Ball\"}")]
        private void Add(UnionAirRequestContext ctx)
            => new ComponentWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("DELETE", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Removes a component from a GameObject. Query: target={\\\"type\\\":\\\"componentPath\\\",\\\"value\\\":\\\"Path:Rigidbody\\\"}. Alternatively, target a GameObject with hierarchyPath and add ?type=Rigidbody.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" })]
        private void Remove(UnionAirRequestContext ctx)
            => new ComponentWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Updates serialized component properties and the Inspector header checkbox. Query: target={\\\"type\\\":\\\"componentPath\\\",\\\"value\\\":\\\"Path:ComponentType\\\"}. Alternatively, target a GameObject with hierarchyPath and add ?type=ComponentName. Every key in 'properties' must be unique and name a writable Unity serialized property with a compatible JSON value; composite value objects reject unknown or duplicate members. An array is written whole as a JSON array, one element at a time as 'name.Array.data[i]', or resized as 'name.Array.size'. 'enabled' is a field of its own because the checkbox is not a property this endpoint can address.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" },
            RequiredBody = new string[] { "properties or enabled" },
            OptionalBody = new string[] { "allowWhilePlaying" },
            RequestExample = "{\"properties\":{\"m_Mass\":1.0,\"m_UseGravity\":true,\"m_IsKinematic\":false},\"enabled\":false}",
            ResponseExample = "{\"path\":\"Ball\",\"globalObjectId\":\"...\",\"component\":\"UnityEngine.Rigidbody\",\"componentGlobalObjectId\":\"...\",\"enabled\":false,\"updated\":[\"m_Mass\",\"m_UseGravity\",\"m_IsKinematic\"]}")]
        private void Update(UnionAirRequestContext ctx)
            => new ComponentWriteHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("assets")]
    internal sealed class AssetsController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Lists project assets.",
            OptionalQuery = new string[] { "path", "type", "search" })]
        private void List(UnionAirRequestContext ctx)
            => new AssetHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "{guid}",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns asset details by GUID: path, type, direct dependencies, labels, and the objects the file holds besides its main asset. 'subAssets' carries the localIdentifier an object reference sends to name one of them, and is omitted for a path holding only its main asset.",
            PathParams = new string[] { "guid" },
            ResponseExample = "{\"guid\":\"8f0565f2...\",\"path\":\"Assets/Models/unitychan.fbx\",\"type\":\"UnityEngine.GameObject\",\"dependencies\":[\"Assets/Models/Materials/body.mat\"],\"labels\":[],\"subAssets\":[{\"localIdentifier\":\"4300014\",\"name\":\"BLW_DEF\",\"type\":\"UnityEngine.Mesh\"}]}")]
        private void Detail(UnionAirRequestContext ctx)
            => new AssetHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "dependents",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Finds assets that depend on an asset.",
            RequiredQuery = new string[] { "guid" })]
        private void Dependents(UnionAirRequestContext ctx)
            => new AssetDependentsHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("DELETE", "{guid}",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Deletes an asset and its meta file. Returns 409 when the asset is a loaded scene or a folder containing loaded scenes; unload every reported scene before retrying.",
            PathParams = new string[] { "guid" })]
        private void Delete(UnionAirRequestContext ctx)
            => new AssetDeleteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "move",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Moves or renames an asset while preserving GUID references.",
            RequiredBody = new string[] { "guid", "newPath" })]
        private void Move(UnionAirRequestContext ctx)
            => new AssetMoveHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "open",
            Category = UnionAirEndpointCategories.EditorActions,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            UseRiskOverride = true,
            Risk = UnionAirEndpointRisk.EditorState,
            Summary = "Opens an asset in the Unity Editor.",
            RequiredBody = new string[] { "guid or assetPath" })]
        private void Open(UnionAirRequestContext ctx)
            => new AssetOpenHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "reimport",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Reimports one project asset by GUID or project-relative assetPath. Loaded scenes return 409 and must be unloaded before reimporting. Existing files under Assets/ or Packages/ may be imported before they have a GUID.",
            RequiredBody = new string[] { "guid or assetPath" },
            OptionalBody = new string[] { "recursive", "forceUpdate" })]
        private void Reimport(UnionAirRequestContext ctx)
            => new AssetReimportHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("assets/prefabs")]
    internal sealed class PrefabsController
    {
        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Creates a prefab from a scene GameObject.",
            RequiredBody = new string[] { "source", "assetPath", "mode" },
            OptionalBody = new string[] { "scenePath" })]
        private void Create(UnionAirRequestContext ctx)
            => new PrefabCreateHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "apply",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Applies prefab instance overrides.",
            RequiredBody = new string[] { "source" },
            OptionalBody = new string[] { "scenePath" })]
        private void Apply(UnionAirRequestContext ctx)
            => new PrefabOverrideHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "revert",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Reverts a prefab instance.",
            RequiredBody = new string[] { "source" },
            OptionalBody = new string[] { "scenePath" })]
        private void Revert(UnionAirRequestContext ctx)
            => new PrefabOverrideHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("assets/materials")]
    internal sealed class MaterialsController
    {
        [UnionAirEndpoint("GET", "{guid}",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns a material's shader, render queue, enabled keywords, and every declared shader property with its current value. Each value is spelled the way PATCH /api/assets/materials reads it, so a value can be sent straight back. 'flags' carries Unity's shader property flag names and 'range' the slider bounds of a Range property. renderQueue and keywords are reported and not writable.",
            PathParams = new string[] { "guid" },
            ResponseExample = "{\"guid\":\"5ebb6c...\",\"assetPath\":\"Assets/Materials/Hair.mat\",\"shader\":\"Toon/Toon\",\"renderQueue\":2000,\"keywords\":[\"_EMISSIVE_SIMPLE\"],\"properties\":[{\"name\":\"_BaseColor\",\"type\":\"Color\",\"value\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"flags\":[]},{\"name\":\"_MainTex\",\"type\":\"Texture\",\"value\":{\"assetGuid\":\"cbb65e...\",\"assetPath\":\"Assets/Textures/hair.tga\",\"assetType\":\"UnityEngine.Texture2D\",\"localIdentifier\":\"2800000\"},\"flags\":[]}]}")]
        private void Get(UnionAirRequestContext ctx)
            => new MaterialReadHandler().Handle(ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Creates a material asset. 'shader' is the Unity shader name string (e.g. Standard, Universal Render Pipeline/Lit).",
            RequiredBody = new string[] { "assetPath", "shader" },
            RequestExample = "{\"assetPath\":\"Assets/Materials/BrickMat.mat\",\"shader\":\"Standard\"}",
            ResponseExample = "{\"assetPath\":\"Assets/Materials/BrickMat.mat\",\"guid\":\"...\",\"shader\":\"Standard\"}")]
        private void Create(UnionAirRequestContext ctx)
            => new MaterialWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates material properties. 'properties' keys are shader property names (e.g. _Color, _Metallic). Color: {r,g,b,a}; Float/Range: number; Int: integer; Vector: {x,y,z,w}; Texture: an object reference with assetGuid or assetPath, or null. A key naming no shader property answers 400.",
            RequiredQuery = new string[] { "guid" },
            RequiredBody = new string[] { "properties" },
            RequestExample = "{\"properties\":{\"_Color\":{\"r\":1.0,\"g\":0.2,\"b\":0.2,\"a\":1.0},\"_Metallic\":0.0,\"_Glossiness\":0.5,\"_MainTex\":{\"assetGuid\":\"a1b2c3d4e5f67890a1b2c3d4e5f67890\"}}}",
            ResponseExample = "{\"updated\":[\"_Color\",\"_Metallic\",\"_Glossiness\",\"_MainTex\"]}")]
        private void Update(UnionAirRequestContext ctx)
            => new MaterialWriteHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("assets/shaders")]
    internal sealed class ShadersController
    {
        [UnionAirEndpoint("GET", "{guid}",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns a shader's import state, cached compiler messages, its effective local keyword space, declared properties with their defaults, and the subshaders Unity compiled. 'hasError' and 'messages' answer whether Unity accepted the last import, which reading the .shader file cannot. Messages are the ones cached at import time, so reimport the asset before reading again. 'isSupported' is Unity's capability signal — whether the shader runs on the current GPU, fallbacks considered — and says nothing about whether the import succeeded. 'keywords' is the effective space and includes keywords from Fallback/UsePass dependencies and keywords Unity adds, not only the shader's own declarations. 'subshaders' is what Unity compiled, which is the Fallback's when the shader's own subshaders are unusable. Each subshader reports its 'renderPipeline' tag, which says which pipeline that subshader is for; it is null when the subshader declares no such tag, as a built-in-pipeline subshader does. Every structural field is null only when the ShaderLab parse failed before the shader's name was read, because nothing then came from the file.",
            PathParams = new string[] { "guid" },
            ResponseExample = "{\"guid\":\"5ebb6c...\",\"assetPath\":\"Assets/Shaders/Toon.shader\",\"name\":\"Toon/Toon\",\"isSupported\":true,\"hasError\":false,\"hasWarnings\":false,\"messages\":[],\"renderQueue\":2000,\"maximumLOD\":-1,\"subshaderCount\":1,\"passCount\":2,\"keywords\":[{\"name\":\"_ALPHATEST_ON\",\"isOverridable\":false,\"isDynamic\":false}],\"properties\":[{\"name\":\"_BaseColor\",\"type\":\"Color\",\"description\":\"Base Color\",\"defaultValue\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"flags\":[\"MainColor\"],\"attributes\":[]},{\"name\":\"_AlphaClip\",\"type\":\"Float\",\"description\":\"Alpha Clipping\",\"defaultValue\":0,\"flags\":[],\"attributes\":[\"Toggle(_ALPHATEST_ON)\"]},{\"name\":\"_MainTex\",\"type\":\"Texture\",\"description\":\"Base Map\",\"defaultValue\":\"white\",\"textureDimension\":\"Tex2D\",\"flags\":[\"MainTexture\"],\"attributes\":[]}],\"activeSubshaderIndex\":0,\"subshaders\":[{\"levelOfDetail\":300,\"renderPipeline\":\"UniversalPipeline\",\"passes\":[{\"name\":\"ForwardLit\",\"lightMode\":\"UniversalForward\",\"isGrabPass\":false}]}]}")]
        private void Get(UnionAirRequestContext ctx)
            => new ShaderReadHandler().HandleByGuid(ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns the same report for the shader with a given name — the string GET /api/assets/materials/{guid} reports and POST /api/assets/materials takes — so a shader can be inspected before a material is created from it. Answers 404 when no shader carries the name, which is also when creating a material from it would fail.",
            RequiredQuery = new string[] { "name" })]
        private void GetByName(UnionAirRequestContext ctx)
            => new ShaderReadHandler().HandleByName(ctx.Request, ctx.Response);
    }

    [UnionAirController("search")]
    internal sealed class SearchController
    {
        [UnionAirEndpoint("GET", "gameobjects",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Searches scene GameObjects with AND filters.",
            OptionalQuery = new string[] { "scenePath", "name", "component", "tag", "layer", "active", "assetGuid", "includeComponents" })]
        private void GameObjects(UnionAirRequestContext ctx)
            => new SearchGameObjectsHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "asset-refs",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Finds scene references to an asset.",
            RequiredQuery = new string[] { "guid" },
            OptionalQuery = new string[] { "scenePath" })]
        private void AssetRefs(UnionAirRequestContext ctx)
            => new SearchAssetRefsHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("assets/scriptableobjects")]
    internal sealed class ScriptableObjectsController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Lists ScriptableObject assets.",
            OptionalQuery = new string[] { "type", "path", "search" })]
        private void List(UnionAirRequestContext ctx)
            => new ScriptableObjectReadHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "{guid}",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns a ScriptableObject asset with all readable serialized properties. An array is returned as a JSON array whose elements follow the same type rules.",
            PathParams = new string[] { "guid" })]
        private void Detail(UnionAirRequestContext ctx)
            => new ScriptableObjectReadHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Creates a ScriptableObject asset from a type name. Optional initial properties follow the PATCH contract: every key must be unique, writable, and carry a compatible JSON value; composite value objects reject unknown or duplicate members.",
            RequiredBody = new string[] { "typeName", "assetPath" },
            OptionalBody = new string[] { "properties" },
            RequestExample = "{\"typeName\":\"SkillDefinition\",\"assetPath\":\"Assets/Data/Fireball.asset\",\"properties\":{\"displayName\":\"Fireball\",\"cooldown\":2.5}}",
            ResponseExample = "{\"guid\":\"...\",\"assetPath\":\"Assets/Data/Fireball.asset\",\"type\":\"SkillDefinition\",\"updated\":[\"displayName\",\"cooldown\"]}")]
        private void Create(UnionAirRequestContext ctx)
            => new ScriptableObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates serialized properties on a ScriptableObject. Every key in 'properties' must be unique and name a writable serialized property with a compatible JSON value; composite value objects reject unknown or duplicate members, and unsupported generic types are rejected. An array is written whole as a JSON array, one element at a time as 'name.Array.data[i]', or resized as 'name.Array.size'.",
            RequiredQuery = new string[] { "guid" },
            RequiredBody = new string[] { "properties" },
            RequestExample = "{\"properties\":{\"displayName\":\"Fireball\",\"cooldown\":2.5}}",
            ResponseExample = "{\"guid\":\"...\",\"assetPath\":\"Assets/Data/Skill.asset\",\"type\":\"SkillDefinition\",\"updated\":[\"displayName\",\"cooldown\"]}")]
        private void Update(UnionAirRequestContext ctx)
            => new ScriptableObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("DELETE", "{guid}",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Deletes a ScriptableObject asset.",
            PathParams = new string[] { "guid" })]
        private void Delete(UnionAirRequestContext ctx)
            => new ScriptableObjectWriteHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("assets/texture-importer")]
    internal sealed class TextureImporterController
    {
        [UnionAirEndpoint("PATCH", "{guid}",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates texture import settings and reimports the asset. Supported textureType values: Sprite, Default, NormalMap, GUI, Cursor, Cookie, Lightmap, SingleChannel. spriteMode: Single, Multiple, Polygon (Sprite type only).",
            PathParams = new string[] { "guid" },
            OptionalBody = new string[] { "textureType", "spriteMode", "pixelsPerUnit" },
            RequestExample = "{\"textureType\":\"Sprite\",\"spriteMode\":\"Single\",\"pixelsPerUnit\":100}",
            ResponseExample = "{\"guid\":\"...\",\"assetPath\":\"Assets/Actors/portrait.png\",\"textureType\":\"Sprite\",\"spriteMode\":\"Single\",\"pixelsPerUnit\":100.0}")]
        private void Update(UnionAirRequestContext ctx)
            => new TextureImporterHandler().HandleUpdate(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);
    }

    [UnionAirController("assets/audio-importer")]
    internal sealed class AudioImporterController
    {
        [UnionAirEndpoint("GET", "{guid}",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns typed AudioImporter settings, platform override state and effective settings, the per-platform compression format catalog, and AudioClip metadata.",
            PathParams = new string[] { "guid" },
            ResponseExample = "{\"guid\":\"...\",\"assetPath\":\"Assets/Audio/theme.ogg\",\"forceToMono\":false,\"normalize\":true,\"ambisonic\":false,\"loadInBackground\":false,\"defaultSampleSettings\":{\"loadType\":\"CompressedInMemory\",\"compressionFormat\":\"Vorbis\",\"quality\":0.7,\"preloadAudioData\":true,\"sampleRateSetting\":\"PreserveSampleRate\",\"sampleRateOverride\":0,\"conversionMode\":0},\"defaultCompressionFormats\":[\"PCM\",\"Vorbis\",\"ADPCM\"],\"supportedConversionModes\":[0],\"platforms\":[{\"platform\":\"Android\",\"installed\":true,\"compressionFormats\":[\"PCM\",\"Vorbis\",\"ADPCM\",\"MP3\"],\"override\":false,\"inherited\":{\"loadType\":\"CompressedInMemory\",\"compressionFormat\":\"Vorbis\",\"quality\":0.7,\"preloadAudioData\":true,\"sampleRateSetting\":\"PreserveSampleRate\",\"sampleRateOverride\":0,\"conversionMode\":0},\"effective\":{\"loadType\":\"CompressedInMemory\",\"compressionFormat\":\"Vorbis\",\"quality\":0.7,\"preloadAudioData\":true,\"sampleRateSetting\":\"PreserveSampleRate\",\"sampleRateOverride\":0,\"conversionMode\":0}}],\"audioClip\":{\"name\":\"theme\",\"length\":12.5,\"channels\":2,\"frequency\":44100,\"samples\":551250,\"loadType\":\"CompressedInMemory\",\"preloadAudioData\":true,\"ambisonic\":false,\"loadInBackground\":false,\"loadState\":\"Loaded\"}}")]
        private void Get(UnionAirRequestContext ctx)
            => new AudioImporterHandler().HandleGet(ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Validates and updates AudioImporter global/default settings and platform overrides, then calls SaveAndReimport once. Set a platform entry's override to false to restore inheritance. The response reports final importer state, import diagnostics, and AudioClip metadata.",
            PathParams = new string[] { "guid" },
            OptionalBody = new string[] { "forceToMono", "normalize", "ambisonic", "loadInBackground", "defaultSampleSettings", "platformOverrides" },
            RequestExample = "{\"forceToMono\":true,\"defaultSampleSettings\":{\"loadType\":\"CompressedInMemory\",\"compressionFormat\":\"Vorbis\",\"quality\":0.7},\"platformOverrides\":[{\"platform\":\"Android\",\"override\":true,\"sampleSettings\":{\"compressionFormat\":\"Vorbis\",\"quality\":0.5}}]}",
            ResponseExample = "{\"guid\":\"...\",\"assetPath\":\"Assets/Audio/theme.ogg\",\"reimported\":true,\"diagnostics\":[]}")]
        private void Update(UnionAirRequestContext ctx)
            => new AudioImporterHandler().HandleUpdate(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);
    }

    [UnionAirController("assets/model-importer")]
    internal sealed class ModelImporterController
    {
        [UnionAirEndpoint("GET", "{guid}",
            Category = UnionAirEndpointCategories.Read,
            BlockedDuring = UnionAirActivity.AssetUpdate,
            Summary = "Returns normalized ModelImporter core, material/remap, rig/Avatar, and imported clip settings with explicit compatibility metadata and stable imported sub-asset identities. Transient preview objects are excluded.",
            PathParams = new string[] { "guid" },
            ResponseExample = "{\"schemaVersion\":1,\"guid\":\"...\",\"assetPath\":\"Assets/Models/robot.fbx\",\"settings\":{\"model\":{\"globalScale\":1.0,\"isReadable\":false}},\"subAssets\":[{\"guid\":\"...\",\"localIdentifier\":\"4300000\",\"name\":\"Body\",\"type\":\"UnityEngine.Mesh\"}]}")]
        private void Get(UnionAirRequestContext ctx)
            => new ModelImporterHandler().HandleGet(ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/preflight",
            Category = UnionAirEndpointCategories.Read,
            BlockedDuring = UnionAirActivity.Compile | UnionAirActivity.AssetUpdate,
            Summary = "Validates a versioned ModelImporter core, material/remap, rig/Avatar, and full imported-clip replacement without changing the importer or reimporting the asset.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "schemaVersion" },
            OptionalBody = new string[] { "model", "mesh", "geometry", "normals", "tangents", "materials", "materialRemaps", "rig", "clips" },
            RequestExample = "{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"Idle\",\"firstFrame\":0,\"lastFrame\":60,\"loopTime\":true,\"loopPose\":true}]}",
            ResponseExample = "{\"schemaVersion\":1,\"guid\":\"...\",\"assetPath\":\"Assets/Models/robot.fbx\",\"valid\":true,\"reimportRequired\":true,\"changedFields\":[\"model.isReadable\"]}")]
        private void Preflight(UnionAirRequestContext ctx)
            => new ModelImporterHandler().HandlePreflight(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            BlockedDuring = UnionAirActivity.Compile | UnionAirActivity.AssetUpdate,
            Summary = "Validates and updates versioned ModelImporter core, material/remap, rig/Avatar, and imported clip settings, performs at most one SaveAndReimport, and reports the before/after state, generated sub-asset delta, diagnostics, and rollback status.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "schemaVersion" },
            OptionalBody = new string[] { "model", "mesh", "geometry", "normals", "tangents", "materials", "materialRemaps", "rig", "clips" },
            RequestExample = "{\"schemaVersion\":1,\"clips\":[{\"takeName\":\"Take 001\",\"name\":\"Idle\",\"firstFrame\":0,\"lastFrame\":60,\"loopTime\":true,\"events\":[{\"time\":0.5,\"functionName\":\"Footstep\"}]}]}",
            ResponseExample = "{\"schemaVersion\":1,\"guid\":\"...\",\"assetPath\":\"Assets/Models/robot.fbx\",\"reimported\":true,\"changedFields\":[\"model.isReadable\"],\"diagnostics\":[],\"rollback\":null}")]
        private void Update(UnionAirRequestContext ctx)
            => new ModelImporterHandler().HandleUpdate(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);
    }

    [UnionAirController("assets/animation-clips")]
    internal sealed class AnimationClipsController
    {
        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Creates an AnimationClip asset.",
            RequiredBody = new string[] { "assetPath" },
            OptionalBody = new string[] { "frameRate", "wrapMode" },
            RequestExample = "{\"assetPath\":\"Assets/Animations/Walk.anim\",\"frameRate\":60,\"wrapMode\":\"Loop\"}",
            ResponseExample = "{\"assetPath\":\"Assets/Animations/Walk.anim\",\"guid\":\"...\",\"frameRate\":60.0,\"length\":0.0}")]
        private void Create(UnionAirRequestContext ctx)
            => new AnimationClipHandler().HandleCreate(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "{guid}",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns AnimationClip metadata and all property curves.",
            PathParams = new string[] { "guid" })]
        private void Detail(UnionAirRequestContext ctx)
            => new AnimationClipHandler().HandleRead(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates an AnimationClip's 'frameRate', 'wrapMode', and any subset of 'settings' -- the fields the Animation Inspector shows above the curve list, Loop Time among them. An omitted field is left unchanged, and every value is checked before the first is written. Note that 'wrapMode' is a WrapMode on the clip object and is not Loop Time; that is 'settings.loopTime'. A clip generated by a ModelImporter answers 409: its settings belong to the importer, and a write here would change an object the next reimport discards.",
            PathParams = new string[] { "guid" },
            OptionalBody = new string[] { "frameRate", "wrapMode", "settings" },
            RequestExample = "{\"frameRate\":30.0,\"settings\":{\"loopTime\":true,\"cycleOffset\":0.0}}",
            ResponseExample = "{\"assetPath\":\"Assets/Animations/Walk.anim\",\"name\":\"Walk\",\"applied\":[\"frameRate\",\"settings.loopTime\"],\"settings\":{\"loopTime\":true,\"loopBlend\":false,\"cycleOffset\":0.0}}")]
        private void UpdateClip(UnionAirRequestContext ctx)
            => new AnimationClipHandler().HandleUpdate(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/events",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Replaces every AnimationEvent on a clip. Unity stores events as an ordered array with no identity per entry, so the array is replaced wholesale rather than edited entry by entry. Each event needs 'time' and 'functionName'; 'objectReferenceParameter' takes {guid}, and 'messageOptions' defaults to RequireReceiver, which is Unity's default rather than this endpoint's choice. Every element is parsed before any is written, so a list whose fourth entry names a missing asset replaces nothing. A clip generated by a ModelImporter answers 409.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "events" },
            RequestExample = "{\"events\":[{\"time\":0.5,\"functionName\":\"Footstep\",\"stringParameter\":\"left\",\"messageOptions\":\"DontRequireReceiver\"}]}",
            ResponseExample = "{\"assetPath\":\"Assets/Animations/Walk.anim\",\"eventCount\":1,\"events\":[{\"time\":0.5,\"functionName\":\"Footstep\",\"stringParameter\":\"left\",\"floatParameter\":0.0,\"intParameter\":0,\"objectReferenceParameter\":null,\"messageOptions\":\"DontRequireReceiver\"}]}")]
        private void SetClipEvents(UnionAirRequestContext ctx)
            => new AnimationClipHandler().HandleSetEvents(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/events",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes every AnimationEvent from a clip and reports how many there were. A clip generated by a ModelImporter answers 409.",
            PathParams = new string[] { "guid" },
            ResponseExample = "{\"assetPath\":\"Assets/Animations/Walk.anim\",\"removed\":2}")]
        private void DeleteClipEvents(UnionAirRequestContext ctx)
            => new AnimationClipHandler().HandleDeleteEvents(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/curves",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds or replaces float curves and/or object reference curves on an AnimationClip. At least one of 'curves' or 'objectReferenceCurves' must be provided. Each entry requires relativePath, type (C# type name, e.g. Transform), property, and keys array. Object reference keys use 'guid' to reference assets; for Sprite-type textures the Sprite sub-asset is loaded automatically. The response names the serialized bindings the clip holds afterwards, which DELETE .../curves accepts and which are not always the names the request sent: AnimationClip.SetCurve expands a Transform vector property into all of its components, so 'localPosition.y' becomes 'm_LocalPosition.x/.y/.z' with the two unasked-for axes pinned to their default value. Each entry reports what it was asked for under 'requested' and what it produced under 'bindings'. The property name itself is not checked against what the type can animate, so a misspelling becomes a binding that animates nothing. What is checked is the result of each entry. An entry whose component suffix names nothing in the group stores none of its keys -- it creates the group empty, or leaves an existing one untouched -- and is reported in 'errors'; the status is 400 when no entry in the request stored what it was asked to. A rotation group left non-unit at its key times is reported in 'warnings', which is where the single 'localRotation' entry lands: SetCurve fills the quaternion's w with 0 and the clip plays back a half turn. Write rotation as 'localEulerAngles.*', or send all four quaternion components. Both arrays are always present.",
            PathParams = new string[] { "guid" },
            OptionalBody = new string[] { "curves", "objectReferenceCurves" },
            RequestExample = "{\"curves\":[{\"relativePath\":\"Hips\",\"type\":\"Transform\",\"property\":\"localPosition.y\",\"keys\":[{\"time\":0.0,\"value\":0.0},{\"time\":1.0,\"value\":1.0}]}]}",
            ResponseExample = "{\"added\":[\"m_LocalPosition.x\",\"m_LocalPosition.y\",\"m_LocalPosition.z\"],\"addedFloat\":[\"m_LocalPosition.x\",\"m_LocalPosition.y\",\"m_LocalPosition.z\"],\"addedObjectReference\":[],\"curves\":[{\"relativePath\":\"Hips\",\"type\":\"Transform\",\"requested\":\"localPosition.y\",\"bindings\":[\"m_LocalPosition.x\",\"m_LocalPosition.y\",\"m_LocalPosition.z\"]}],\"objectReferenceCurves\":[],\"errors\":[],\"warnings\":[]}")]
        private void AddCurves(UnionAirRequestContext ctx)
            => new AnimationClipHandler().HandleAddCurves(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/curves",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes curves from an AnimationClip by binding. Each binding requires relativePath, type, and property. 'property' is the serialized name GET returns. Adding expands a Transform vector property into all components -- writing 'localPosition.y' stores 'm_LocalPosition.x/.y/.z' -- while removal is exact, so each component is removed separately. A binding that matches nothing is reported in 'errors' with the names that are bound there; 'removed' lists only bindings that were present before and absent after.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "bindings" },
            RequestExample = "{\"bindings\":[{\"relativePath\":\"Hips\",\"type\":\"Transform\",\"property\":\"m_LocalPosition.y\"}]}",
            ResponseExample = "{\"removed\":[\"m_LocalPosition.y\"],\"errors\":[]}")]
        private void DeleteCurves(UnionAirRequestContext ctx)
            => new AnimationClipHandler().HandleDeleteCurves(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);
    }

    [UnionAirController("assets/animator-controllers")]
    internal sealed class AnimatorControllersController
    {
        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Creates an AnimatorController asset with a default Base Layer.",
            RequiredBody = new string[] { "assetPath" },
            RequestExample = "{\"assetPath\":\"Assets/Animations/Character.controller\"}",
            ResponseExample = "{\"assetPath\":\"Assets/Animations/Character.controller\",\"guid\":\"...\",\"layerCount\":1}")]
        private void Create(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleCreate(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "{guid}",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns the full AnimatorController structure: layers, states, transitions, and parameters. Every 'motion' carries a 'type' of AnimationClip, BlendTree, or Unknown; a blend tree has a null 'guid' and its structure inline, nested trees included. A clip's 'guid' identifies the asset holding it, so 'clipsAtPath' above 1 means it does not identify one clip. Every transition carries a 'transitionId' that PATCH and DELETE take as an address, and reports 'duration' beside the 'fixedDuration' that gives it its unit. Every state reports the settings that decide how it plays -- 'writeDefaultValues', 'tag', 'iKOnFeet', 'mirror', 'cycleOffset', each '*Parameter' beside its '*Active' flag -- plus its graph 'position' and the names of its 'behaviours', which are read-only. Every transition reports its 'destination' as a discriminated object -- a 'type' of State, StateMachine, Exit, or None -- rather than a bare name, because a name alone cannot say which it is. Each layer and each state machine nested in it reports the same shape recursively in 'stateMachines', with 'path', 'defaultState', 'entryTransitions', 'stateMachineTransitions', and its own 'anyStateTransitions', so a state inside a sub-state machine is reached by walking one structure; a node at the depth cap of 10 carries 'truncated' instead of its contents.",
            PathParams = new string[] { "guid" },
            ResponseExample = "{\"assetPath\":\"Assets/Animations/Character.controller\",\"guid\":\"...\",\"parameters\":[{\"name\":\"Speed\",\"type\":\"Float\",\"defaultFloat\":0.0}],\"layers\":[{\"name\":\"Base Layer\",\"index\":0,\"defaultWeight\":0.0,\"isBaseLayer\":true,\"blendingMode\":\"Override\",\"avatarMask\":null,\"iKPass\":false,\"syncedLayerIndex\":-1,\"syncedLayerAffectsTiming\":false,\"defaultState\":\"Locomotion\",\"states\":[{\"name\":\"Locomotion\",\"isDefault\":true,\"tag\":\"\",\"writeDefaultValues\":true,\"iKOnFeet\":false,\"mirror\":false,\"cycleOffset\":0.0,\"speed\":1.0,\"speedParameter\":\"\",\"speedParameterActive\":false,\"cycleOffsetParameter\":\"\",\"cycleOffsetParameterActive\":false,\"mirrorParameter\":\"\",\"mirrorParameterActive\":false,\"timeParameter\":\"\",\"timeParameterActive\":false,\"position\":{\"x\":300.0,\"y\":120.0},\"behaviours\":[],\"motion\":{\"type\":\"BlendTree\",\"guid\":null,\"name\":\"Locomotion\",\"blendType\":\"Simple1D\",\"blendParameter\":\"Speed\",\"blendParameterY\":\"Blend\",\"useAutomaticThresholds\":true,\"minThreshold\":0.0,\"maxThreshold\":0.8,\"children\":[{\"threshold\":0.0,\"position\":{\"x\":0.0,\"y\":0.0},\"timeScale\":1.0,\"cycleOffset\":0.0,\"mirror\":false,\"directBlendParameter\":\"Blend\",\"motion\":{\"type\":\"AnimationClip\",\"guid\":\"...\",\"name\":\"WAIT00\",\"assetPath\":\"Assets/Animations/wait.fbx\",\"clipsAtPath\":1}}]},\"transitions\":[{\"transitionId\":\"GlobalObjectId_V1-3-...\",\"destination\":{\"type\":\"State\",\"name\":\"Jump\"},\"hasExitTime\":true,\"exitTime\":0.9,\"duration\":0.25,\"fixedDuration\":true,\"offset\":0.0,\"interruptionSource\":\"None\",\"orderedInterruption\":true,\"canTransitionToSelf\":true,\"mute\":false,\"solo\":false,\"conditions\":[]}]}],\"anyStateTransitions\":[],\"entryTransitions\":[],\"stateMachineTransitions\":[],\"behaviours\":[],\"stateMachines\":[{\"name\":\"Combat\",\"path\":[\"Combat\"],\"position\":{\"x\":700.0,\"y\":0.0},\"defaultState\":null,\"states\":[],\"anyStateTransitions\":[],\"entryTransitions\":[{\"transitionId\":\"GlobalObjectId_V1-3-...\",\"from\":{\"type\":\"Entry\"},\"destination\":{\"type\":\"StateMachine\",\"name\":\"Melee\"},\"solo\":false,\"mute\":false,\"conditions\":[]}],\"stateMachineTransitions\":[],\"behaviours\":[],\"stateMachines\":[]}]}]}")]
        private void Detail(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleRead(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/parameters",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds or replaces a parameter on an AnimatorController. type must be Float, Int, Bool, or Trigger. A parameter that already exists with the same type is updated in place and keeps its position in the array; only a type change destroys and recreates it, which orphans every condition, blend parameter, and state override naming it -- those are listed in 'orphanedReferences' rather than left silent. A 'defaultValue' sent for a Trigger is reported in 'unsupported': Unity stores no default for one. Use PATCH to rename.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name", "type" },
            OptionalBody = new string[] { "defaultValue" },
            RequestExample = "{\"name\":\"Speed\",\"type\":\"Float\",\"defaultValue\":0.0}",
            ResponseExample = "{\"added\":\"Speed\",\"type\":\"Float\",\"unsupported\":[]}")]
        private void AddParameter(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleAddParameter(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}/parameters",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Renames a parameter, or sets its default value, in place. A rename rewrites every site that names it -- conditions on state, AnyState, entry, and state machine transitions at every nesting level of every layer, a blend tree's blendParameter and blendParameterY including nested trees, and a state's speedParameter, cycleOffsetParameter, mirrorParameter, and timeParameter -- and reports each one in 'references'. None of those is a reference Unity maintains, so a delete-then-add rename leaves them naming what no longer exists. 'newName' colliding with an existing parameter is a 409. 'type' cannot be changed here and is rejected with the reason.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "newName", "defaultValue" },
            RequestExample = "{\"name\":\"Speed\",\"newName\":\"MoveSpeed\",\"defaultValue\":0.5}",
            ResponseExample = "{\"name\":\"MoveSpeed\",\"type\":\"Float\",\"renamed\":{\"from\":\"Speed\",\"to\":\"MoveSpeed\"},\"referencesUpdated\":3,\"references\":[{\"kind\":\"condition\",\"layerIndex\":0,\"stateMachinePath\":[],\"transitionId\":\"GlobalObjectId_V1-3-...\",\"conditionIndex\":0}],\"unsupported\":[]}")]
        private void UpdateParameter(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleUpdateParameter(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/parameters",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes a parameter from an AnimatorController by name, and reports the references it orphans. Unity's RemoveParameter does not touch them: a condition naming a deleted parameter still serializes and simply never evaluates again. The delete still happens -- what a condition should become without its parameter is not a decision this API can make -- but 'references' names every site so it is at least visible.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            RequestExample = "{\"name\":\"Speed\"}",
            ResponseExample = "{\"removed\":\"Speed\",\"orphanedReferences\":2,\"references\":[{\"kind\":\"blendParameter\",\"layerIndex\":0,\"stateMachinePath\":[],\"state\":\"Locomotion\",\"childPath\":[]}]}")]
        private void DeleteParameter(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleDeleteParameter(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/layers",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds a layer to an AnimatorController. Every setting PATCH accepts may be supplied here, so a masked layer takes one request. 'weight' is accepted as a synonym for 'defaultWeight'. 'avatarMask' is {guid} referencing an AvatarMask asset. A rejected setting answers 400 and the layer is not created.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "defaultWeight", "weight", "blendingMode", "avatarMask", "iKPass", "syncedLayerIndex", "syncedLayerAffectsTiming" },
            RequestExample = "{\"name\":\"Arms\",\"defaultWeight\":1.0,\"avatarMask\":{\"guid\":\"...\"}}",
            ResponseExample = "{\"added\":\"Arms\",\"layerIndex\":1,\"applied\":[\"defaultWeight\",\"avatarMask\"]}")]
        private void AddLayer(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleAddLayer(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}/layers",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates a layer of an AnimatorController, addressed by 'layerIndex'. Every other field is optional and an omitted field is left unchanged. 'avatarMask' takes {guid} to set and an explicit null to clear. 'syncedLayerIndex' is -1 for no sync, or another layer's index; a value out of range, or the layer's own index, is rejected rather than passed to Unity. 'applied' lists the fields that were set.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "layerIndex" },
            OptionalBody = new string[] { "name", "defaultWeight", "weight", "blendingMode", "avatarMask", "iKPass", "syncedLayerIndex", "syncedLayerAffectsTiming" },
            RequestExample = "{\"layerIndex\":1,\"defaultWeight\":0.5,\"avatarMask\":null}",
            ResponseExample = "{\"layerIndex\":1,\"applied\":[\"defaultWeight\",\"avatarMask\"]}")]
        private void UpdateLayer(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleUpdateLayer(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/layers",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes a layer from an AnimatorController, addressed by 'layerIndex'. The layer's state machine is a sub-asset of the controller and is destroyed with it. Layer 0 is the base layer and is rejected with 400: Unity does not refuse the removal, and a controller without a base layer cannot be repaired through any other endpoint.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "layerIndex" },
            RequestExample = "{\"layerIndex\":1}",
            ResponseExample = "{\"removed\":\"Arms\",\"layerIndex\":1,\"layerCount\":1}")]
        private void DeleteLayer(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleDeleteLayer(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/blend-trees",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Creates a blend tree as the motion of an existing state, or adds a child to one. A blend tree has no GUID, so it is addressed by 'layerIndex' plus 'state', then 'childPath' -- an array of child indices from that state's root tree, where [] is the root itself. Without 'addChild' the request creates the state's root tree. With 'addChild' it appends to the addressed tree: a nested tree by default, or a clip when 'motion' carries a guid. childPath is positional, so removing or reordering children invalidates a path a client is holding.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "state" },
            OptionalBody = new string[] { "layerIndex", "stateMachinePath", "childPath", "addChild", "name", "blendType", "blendParameter", "blendParameterY", "useAutomaticThresholds", "minThreshold", "maxThreshold", "threshold", "position", "timeScale", "cycleOffset", "mirror", "directBlendParameter", "motion" },
            RequestExample = "{\"layerIndex\":0,\"state\":\"Locomotion\",\"name\":\"Locomotion\",\"blendType\":\"Simple1D\",\"blendParameter\":\"Speed\"}",
            ResponseExample = "{\"created\":\"BlendTree\",\"layerIndex\":0,\"state\":\"Locomotion\",\"childPath\":[],\"name\":\"Locomotion\",\"ignored\":[]}")]
        private void AddBlendTree(UnionAirRequestContext ctx)
            => new BlendTreeHandler().HandleCreate(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}/blend-trees",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates the blend tree addressed by 'layerIndex', 'state', and 'childPath'. Tree fields are 'name', 'blendType', 'blendParameter', 'blendParameterY', 'useAutomaticThresholds', 'minThreshold', 'maxThreshold'; child fields, which need a non-empty childPath, are 'threshold', 'position', 'timeScale', 'cycleOffset', 'mirror', 'directBlendParameter', and 'motion' to swap in a clip -- a blend tree the swap displaces is destroyed with its descendants. Every value is validated before anything is written, so a request that fails applies nothing. A field the addressed blend type does not consult is stored and reported in 'ignored' rather than dropped silently.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "state" },
            OptionalBody = new string[] { "layerIndex", "stateMachinePath", "childPath", "name", "blendType", "blendParameter", "blendParameterY", "useAutomaticThresholds", "minThreshold", "maxThreshold", "threshold", "position", "timeScale", "cycleOffset", "mirror", "directBlendParameter", "motion" },
            RequestExample = "{\"layerIndex\":0,\"state\":\"Locomotion\",\"childPath\":[1],\"threshold\":0.8}",
            ResponseExample = "{\"layerIndex\":0,\"state\":\"Locomotion\",\"childPath\":[1],\"ignored\":[]}")]
        private void UpdateBlendTree(UnionAirRequestContext ctx)
            => new BlendTreeHandler().HandleUpdate(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/blend-trees",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes the blend tree or child addressed by 'layerIndex', 'state', and 'childPath'. An empty or omitted childPath clears the state's motion, which Unity destroys along with every descendant. A non-empty childPath removes that child; Unity leaves the detached subtree in the asset, so it is destroyed here and 'destroyedSubTrees' reports how many blend trees went with it.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "state" },
            OptionalBody = new string[] { "layerIndex", "stateMachinePath", "childPath" },
            RequestExample = "{\"layerIndex\":0,\"state\":\"Locomotion\",\"childPath\":[1]}",
            ResponseExample = "{\"removed\":\"child\",\"layerIndex\":0,\"state\":\"Locomotion\",\"childPath\":[1],\"destroyedSubTrees\":2}")]
        private void DeleteBlendTree(UnionAirRequestContext ctx)
            => new BlendTreeHandler().HandleDelete(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);


        [UnionAirEndpoint("POST", "{guid}/state-machines",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Creates a sub-state machine inside the machine addressed by 'layerIndex' and 'stateMachinePath'. The path is an array of state machine names from the layer root, and an omitted or empty one means that root. 'position' is {x,y} graph layout. A name a sibling already carries is a 409, because the path addresses by name and a second one could not be addressed at all.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "layerIndex", "stateMachinePath", "position" },
            RequestExample = "{\"layerIndex\":0,\"stateMachinePath\":[\"Combat\"],\"name\":\"Melee\",\"position\":{\"x\":300,\"y\":120}}",
            ResponseExample = "{\"added\":\"Melee\",\"layerIndex\":0,\"stateMachinePath\":[\"Combat\",\"Melee\"]}")]
        private void AddStateMachine(UnionAirRequestContext ctx)
            => new AnimatorStateMachineHandler().HandleCreate(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/state-machines",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes the sub-state machine addressed by 'stateMachinePath', with the states, transitions, nested machines, and blend trees it holds -- all sub-assets of the controller. A machine that holds anything answers 409 listing its contents unless 'recursive' is true, because this is not the same size of operation as deleting one state. An empty path names the layer's root and is refused; delete the layer instead.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "stateMachinePath" },
            OptionalBody = new string[] { "layerIndex", "recursive" },
            RequestExample = "{\"layerIndex\":0,\"stateMachinePath\":[\"Combat\",\"Melee\"],\"recursive\":true}",
            ResponseExample = "{\"removed\":\"Melee\",\"layerIndex\":0,\"stateMachinePath\":[\"Combat\",\"Melee\"],\"removedStates\":3,\"removedStateMachines\":0,\"destroyedBlendTrees\":1}")]
        private void DeleteStateMachine(UnionAirRequestContext ctx)
            => new AnimatorStateMachineHandler().HandleDelete(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/state-machine-transitions",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds an AnimatorTransition to the machine addressed by 'stateMachinePath' -- the type that connects state machines, which carries a destination and conditions and no timing at all. 'from' is 'Entry' for an entry transition, or the name of a nested state machine. The destination is exactly one of 'to' (a state name), 'toStateMachine' (a path), or 'toExit'. Without one of these a created sub-state machine can never be entered.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "from" },
            OptionalBody = new string[] { "layerIndex", "stateMachinePath", "to", "toStateMachine", "toExit", "solo", "mute", "conditions" },
            RequestExample = "{\"layerIndex\":0,\"stateMachinePath\":[\"Combat\"],\"from\":\"Entry\",\"to\":\"Idle\"}",
            ResponseExample = "{\"added\":true,\"transitionId\":\"GlobalObjectId_V1-3-...\",\"layerIndex\":0,\"from\":\"Entry\"}")]
        private void AddStateMachineTransition(UnionAirRequestContext ctx)
            => new AnimatorStateMachineHandler().HandleAddTransition(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/state-machine-transitions",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes an AnimatorTransition by 'transitionId', which the read reports on every entry and state machine transition. There is no name-pair form: these transitions have no source state to name. The transition is a sub-asset of the controller and is destroyed with the removal.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "transitionId" },
            OptionalBody = new string[] { "layerIndex" },
            RequestExample = "{\"transitionId\":\"GlobalObjectId_V1-3-...\"}",
            ResponseExample = "{\"removed\":true,\"transitionId\":\"GlobalObjectId_V1-3-...\",\"kind\":\"entry\",\"layerIndex\":0}")]
        private void DeleteStateMachineTransition(UnionAirRequestContext ctx)
            => new AnimatorStateMachineHandler().HandleDeleteTransition(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/states",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds a state to a layer of an AnimatorController, fully formed: every setting PATCH accepts may be supplied here. 'motion' is an object with a 'guid' referencing an AnimationClip. 'position' is {x,y} graph layout, so a controller authored through the API does not stack every state at the origin. A '*Parameter' naming a parameter the controller does not have is a 400, and so is a '*Active' flag that would be left true with an empty name, which is an override that drives nothing. Every value is checked before the state is created, so a rejected request adds nothing. 'behaviours' is read-only and reported in 'unsupported'. An unknown field is a 400 listing the accepted ones.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "layerIndex", "stateMachinePath", "setAsDefault", "motion", "speed", "tag", "writeDefaultValues", "iKOnFeet", "mirror", "cycleOffset", "speedParameter", "speedParameterActive", "cycleOffsetParameter", "cycleOffsetParameterActive", "mirrorParameter", "mirrorParameterActive", "timeParameter", "timeParameterActive", "position", "behaviours" },
            RequestExample = "{\"name\":\"Walk\",\"layerIndex\":0,\"motion\":{\"guid\":\"abc123...\"},\"speed\":1.0,\"writeDefaultValues\":false,\"tag\":\"Locomotion\",\"position\":{\"x\":300,\"y\":120}}",
            ResponseExample = "{\"added\":\"Walk\",\"layerIndex\":0,\"isDefault\":false,\"unsupported\":[]}")]
        private void AddState(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleAddState(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}/states",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates a state in an AnimatorController, addressed by 'name' and optionally 'layerIndex'. Every other field is optional and an omitted field is left unchanged. A '*Parameter' and its '*Active' flag are one decision, judged on the pair the request would leave behind rather than the halves it carries: a name the controller does not have is a 400 and neither half is written, and so is leaving the flag true with an empty name. Every value is checked before the first is written, so a rejected field leaves the state as it was. 'behaviours' is read-only and reported in 'unsupported'. An unknown field is a 400 listing the accepted ones.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "layerIndex", "stateMachinePath", "newName", "setAsDefault", "motion", "speed", "tag", "writeDefaultValues", "iKOnFeet", "mirror", "cycleOffset", "speedParameter", "speedParameterActive", "cycleOffsetParameter", "cycleOffsetParameterActive", "mirrorParameter", "mirrorParameterActive", "timeParameter", "timeParameterActive", "position", "behaviours" },
            RequestExample = "{\"name\":\"Walk\",\"layerIndex\":0,\"writeDefaultValues\":false,\"cycleOffset\":0.25,\"speedParameter\":\"Speed\",\"speedParameterActive\":true}",
            ResponseExample = "{\"updated\":\"Walk\",\"layerIndex\":0,\"unsupported\":[]}")]
        private void UpdateState(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleUpdateState(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/states",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes a state from an AnimatorController layer.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "layerIndex", "stateMachinePath" },
            RequestExample = "{\"name\":\"Walk\",\"layerIndex\":0}",
            ResponseExample = "{\"removed\":\"Walk\",\"layerIndex\":0}")]
        private void DeleteState(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleDeleteState(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/transitions",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds a transition between states in an AnimatorController. Send 'from' plus exactly one destination: 'to' (a state name, or 'Exit'), or 'toStateMachine' (a path), which is how a state enters a sub-state machine. Use 'AnyState' as 'from' for any-state transitions. Condition modes: If, IfNot, Greater, Less, Equals, NotEqual. 'duration' is seconds when 'fixedDuration' is true and a fraction of the source state when it is false. A state pair may carry any number of transitions, so the response returns the new transition's 'transitionId' to address it by later. Every setting is validated before the transition is created, so a rejected request adds nothing. 'canTransitionToSelf' applies to AnyState transitions and is reported in 'unsupported' elsewhere.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "from" },
            OptionalBody = new string[] { "to", "toStateMachine", "layerIndex", "stateMachinePath", "hasExitTime", "exitTime", "duration", "fixedDuration", "offset", "interruptionSource", "orderedInterruption", "canTransitionToSelf", "mute", "solo", "conditions" },
            RequestExample = "{\"from\":\"Idle\",\"to\":\"Walk\",\"layerIndex\":0,\"hasExitTime\":false,\"duration\":0.25,\"fixedDuration\":true,\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Greater\",\"threshold\":0.1}]}",
            ResponseExample = "{\"added\":true,\"transitionId\":\"GlobalObjectId_V1-3-...\",\"from\":\"Idle\",\"to\":\"Walk\",\"layerIndex\":0,\"unsupported\":[]}")]
        private void AddTransition(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleAddTransition(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}/transitions",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates one transition, addressed by 'transitionId' from the read response, or by 'from' plus 'to' while that pair carries exactly one transition. A pair carrying several answers 409 listing every candidate's transitionId and conditions; it no longer updates the first silently. Every value is validated before the first is written, so a rejected field leaves the transition as it was. 'conditions' replaces the whole array, and an empty array clears it.",
            PathParams = new string[] { "guid" },
            OptionalBody = new string[] { "transitionId", "from", "to", "layerIndex", "stateMachinePath", "hasExitTime", "exitTime", "duration", "fixedDuration", "offset", "interruptionSource", "orderedInterruption", "canTransitionToSelf", "mute", "solo", "conditions" },
            RequestExample = "{\"transitionId\":\"GlobalObjectId_V1-3-...\",\"duration\":0.1,\"fixedDuration\":false,\"interruptionSource\":\"Destination\"}",
            ResponseExample = "{\"updated\":true,\"transitionId\":\"GlobalObjectId_V1-3-...\",\"from\":\"Idle\",\"to\":\"Walk\",\"layerIndex\":0,\"unsupported\":[]}")]
        private void UpdateTransition(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleUpdateTransition(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/transitions",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes one transition, addressed by 'transitionId' from the read response, or by 'from' plus 'to' while that pair carries exactly one transition. A pair carrying several answers 409 listing every candidate; it no longer removes them all. The transition is a sub-asset of the controller and is destroyed with the removal.",
            PathParams = new string[] { "guid" },
            OptionalBody = new string[] { "transitionId", "from", "to", "layerIndex", "stateMachinePath" },
            RequestExample = "{\"transitionId\":\"GlobalObjectId_V1-3-...\"}",
            ResponseExample = "{\"removed\":true,\"transitionId\":\"GlobalObjectId_V1-3-...\",\"from\":\"Idle\",\"to\":\"Walk\",\"layerIndex\":0}")]
        private void DeleteTransition(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleDeleteTransition(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);
    }
}
