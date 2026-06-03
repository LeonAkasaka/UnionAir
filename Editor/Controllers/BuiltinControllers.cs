namespace LeonAkasaka.UnionAir.Editor
{
    [UnionAirController("help")]
    internal sealed class HelpController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns the API manifest. Use ?detail=full for examples, ?category=<id> to filter by category (e.g. sceneWrite, read, assetWrite, playMode, editorActions).",
            OptionalQuery = new string[] { "detail", "category", "source", "includeDisabled" })]
        private void Help(UnionAirRequestContext ctx)
            => new HelpHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("health")]
    internal sealed class HealthController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Checks whether the server is running.")]
        private void Health(UnionAirRequestContext ctx)
            => new HealthHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("editor")]
    internal sealed class EditorController
    {
        [UnionAirEndpoint("GET", "status",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns the Unity Editor execution status. Poll this after POST /api/editor/refresh until isCompiling is false; connection-refused during domain reload is expected — retry with backoff.",
            ResponseExample = "{\"isPlaying\":false,\"isPaused\":false,\"isCompiling\":false,\"isUpdating\":false}")]
        private void Status(UnionAirRequestContext ctx)
            => new EditorStatusHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "logs",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns captured Unity Console logs.",
            OptionalQuery = new string[] { "type", "search", "limit" })]
        private void Logs(UnionAirRequestContext ctx)
            => new EditorLogsHandler().Handle(ctx.Request, ctx.Response);

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
            Summary = "Refreshes the Unity AssetDatabase and triggers script recompilation. If scripts changed, Unity performs a domain reload: the REST server restarts and will return connection-refused for a few seconds. Retry GET /api/editor/status until isCompiling is false before making further calls.",
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
            Summary = "Requests entering Play mode.")]
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
            OptionalQuery = new string[] { "scenePath", "width", "height", "format", "quality" })]
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
            Summary = "Saves the active scene.")]
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
            Summary = "Removes a component from a GameObject. Query: target={\\\"type\\\":\\\"hierarchyPath\\\",\\\"value\\\":\\\"Path\\\"}&type=Rigidbody",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" })]
        private void Remove(UnionAirRequestContext ctx)
            => new ComponentWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Updates serialized component properties. Query: target={\\\"type\\\":\\\"hierarchyPath\\\",\\\"value\\\":\\\"Path\\\"}. Use Unity serialized property names.",
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
            Summary = "Deletes an asset and its meta file.",
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
            Summary = "Reimports one project asset.",
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
}
