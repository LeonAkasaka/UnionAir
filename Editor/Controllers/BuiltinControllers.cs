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
            Summary = "Returns GameObject details including components.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath" })]
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
            Summary = "Updates serialized component properties. Query: target={\\\"type\\\":\\\"componentPath\\\",\\\"value\\\":\\\"Path:ComponentType\\\"}. Alternatively, target a GameObject with hierarchyPath and add ?type=ComponentName. Use Unity serialized property names.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" },
            RequiredBody = new string[] { "properties" },
            OptionalBody = new string[] { "allowWhilePlaying" },
            RequestExample = "{\"properties\":{\"Rigidbody\":{\"m_Mass\":1.0,\"m_UseGravity\":true,\"m_IsKinematic\":false}}}",
            ResponseExample = "{\"updated\":[\"Rigidbody\"],\"target\":\"Ball\"}")]
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
            Summary = "Returns asset details by GUID.",
            PathParams = new string[] { "guid" })]
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
            Summary = "Updates material properties. 'properties' keys are shader property names (e.g. _Color, _Metallic). Color: {r,g,b,a}; Float/Range: number; Texture: {guid}; Vector: {x,y,z,w}.",
            RequiredQuery = new string[] { "guid" },
            RequiredBody = new string[] { "properties" },
            RequestExample = "{\"properties\":{\"_Color\":{\"r\":1.0,\"g\":0.2,\"b\":0.2,\"a\":1.0},\"_Metallic\":0.0,\"_Glossiness\":0.5,\"_MainTex\":{\"guid\":\"a1b2c3d4e5f67890a1b2c3d4e5f67890\"}}}",
            ResponseExample = "{\"updated\":[\"_Color\",\"_Metallic\",\"_Glossiness\",\"_MainTex\"]}")]
        private void Update(UnionAirRequestContext ctx)
            => new MaterialWriteHandler().Handle(ctx.Request, ctx.Response);
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
            Summary = "Returns a ScriptableObject asset with all readable serialized properties. Arrays are returned as null.",
            PathParams = new string[] { "guid" })]
        private void Detail(UnionAirRequestContext ctx)
            => new ScriptableObjectReadHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Creates a ScriptableObject asset from a type name.",
            RequiredBody = new string[] { "typeName", "assetPath" },
            OptionalBody = new string[] { "properties" })]
        private void Create(UnionAirRequestContext ctx)
            => new ScriptableObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates serialized properties on a ScriptableObject. Arrays and unsupported generic types are skipped.",
            RequiredQuery = new string[] { "guid" },
            RequiredBody = new string[] { "properties" })]
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

        [UnionAirEndpoint("POST", "{guid}/curves",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds or replaces float curves and/or object reference curves on an AnimationClip. At least one of 'curves' or 'objectReferenceCurves' must be provided. Each entry requires relativePath, type (C# type name, e.g. Transform), property, and keys array. Object reference keys use 'guid' to reference assets; for Sprite-type textures the Sprite sub-asset is loaded automatically.",
            PathParams = new string[] { "guid" },
            OptionalBody = new string[] { "curves", "objectReferenceCurves" },
            RequestExample = "{\"objectReferenceCurves\":[{\"relativePath\":\"\",\"type\":\"UnityEngine.UI.Image\",\"property\":\"m_Sprite\",\"keys\":[{\"time\":0.0,\"guid\":\"abc123...\"},{\"time\":0.1667,\"guid\":\"def456...\"}]}]}",
            ResponseExample = "{\"added\":[\"m_Sprite\"],\"addedFloat\":[],\"addedObjectReference\":[\"m_Sprite\"],\"errors\":[]}")]
        private void AddCurves(UnionAirRequestContext ctx)
            => new AnimationClipHandler().HandleAddCurves(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/curves",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes curves from an AnimationClip by binding. Each binding requires relativePath, type, and property.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "bindings" },
            RequestExample = "{\"bindings\":[{\"relativePath\":\"Hips\",\"type\":\"Transform\",\"property\":\"localPosition.y\"}]}",
            ResponseExample = "{\"removed\":[\"localPosition.y\"],\"errors\":[]}")]
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
            Summary = "Returns the full AnimatorController structure: layers, states, transitions, and parameters.",
            PathParams = new string[] { "guid" })]
        private void Detail(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleRead(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/parameters",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds or replaces a parameter on an AnimatorController. type must be Float, Int, Bool, or Trigger.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name", "type" },
            OptionalBody = new string[] { "defaultValue" },
            RequestExample = "{\"name\":\"Speed\",\"type\":\"Float\",\"defaultValue\":0.0}",
            ResponseExample = "{\"added\":\"Speed\",\"type\":\"Float\"}")]
        private void AddParameter(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleAddParameter(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/parameters",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes a parameter from an AnimatorController by name.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            RequestExample = "{\"name\":\"Speed\"}",
            ResponseExample = "{\"removed\":\"Speed\"}")]
        private void DeleteParameter(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleDeleteParameter(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/layers",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds a layer to an AnimatorController.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "weight" },
            RequestExample = "{\"name\":\"Arms\",\"weight\":1.0}",
            ResponseExample = "{\"added\":\"Arms\",\"layerIndex\":1}")]
        private void AddLayer(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleAddLayer(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/states",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds a state to a layer of an AnimatorController. 'motion' is an optional object with a 'guid' field referencing an AnimationClip asset.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "layerIndex", "motion", "speed", "setAsDefault" },
            RequestExample = "{\"name\":\"Walk\",\"layerIndex\":0,\"motion\":{\"guid\":\"abc123...\"},\"speed\":1.0,\"setAsDefault\":false}",
            ResponseExample = "{\"added\":\"Walk\",\"layerIndex\":0,\"isDefault\":false}")]
        private void AddState(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleAddState(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}/states",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates a state in an AnimatorController. Identify the state by 'name' and optionally 'layerIndex'.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "layerIndex", "newName", "motion", "speed", "setAsDefault" },
            RequestExample = "{\"name\":\"Walk\",\"layerIndex\":0,\"motion\":{\"guid\":\"abc123...\"},\"speed\":1.5}",
            ResponseExample = "{\"updated\":\"Walk\",\"layerIndex\":0}")]
        private void UpdateState(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleUpdateState(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/states",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes a state from an AnimatorController layer.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "layerIndex" },
            RequestExample = "{\"name\":\"Walk\",\"layerIndex\":0}",
            ResponseExample = "{\"removed\":\"Walk\",\"layerIndex\":0}")]
        private void DeleteState(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleDeleteState(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("POST", "{guid}/transitions",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Adds a transition between states in an AnimatorController. Use 'AnyState' as 'from' for any-state transitions, and 'Exit' as 'to' for exit transitions. Condition modes: If, IfNot, Greater, Less, Equals, NotEqual.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "from", "to" },
            OptionalBody = new string[] { "layerIndex", "hasExitTime", "exitTime", "duration", "offset", "conditions" },
            RequestExample = "{\"from\":\"Idle\",\"to\":\"Walk\",\"layerIndex\":0,\"hasExitTime\":false,\"duration\":0.25,\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Greater\",\"threshold\":0.1}]}",
            ResponseExample = "{\"added\":true,\"from\":\"Idle\",\"to\":\"Walk\",\"layerIndex\":0}")]
        private void AddTransition(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleAddTransition(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("PATCH", "{guid}/transitions",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates an existing transition. Identifies the transition by 'from' and 'to' state names.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "from", "to" },
            OptionalBody = new string[] { "layerIndex", "hasExitTime", "exitTime", "duration", "offset", "conditions" },
            RequestExample = "{\"from\":\"Idle\",\"to\":\"Walk\",\"duration\":0.1,\"conditions\":[{\"parameter\":\"Speed\",\"mode\":\"Greater\",\"threshold\":0.5}]}",
            ResponseExample = "{\"updated\":true,\"from\":\"Idle\",\"to\":\"Walk\",\"layerIndex\":0}")]
        private void UpdateTransition(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleUpdateTransition(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);

        [UnionAirEndpoint("DELETE", "{guid}/transitions",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Removes a transition from an AnimatorController. Identifies the transition by 'from' and 'to' state names.",
            PathParams = new string[] { "guid" },
            RequiredBody = new string[] { "from", "to" },
            OptionalBody = new string[] { "layerIndex" },
            RequestExample = "{\"from\":\"Idle\",\"to\":\"Walk\",\"layerIndex\":0}",
            ResponseExample = "{\"removed\":true,\"from\":\"Idle\",\"to\":\"Walk\",\"layerIndex\":0}")]
        private void DeleteTransition(UnionAirRequestContext ctx)
            => new AnimatorControllerHandler().HandleDeleteTransition(ctx.Request, ctx.Response, ctx.RouteValues["guid"]);
    }
}
