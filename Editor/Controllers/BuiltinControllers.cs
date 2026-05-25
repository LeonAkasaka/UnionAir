namespace LeonAkasaka.UnionAir.Editor
{
    [UnionAirController("help")]
    internal sealed class HelpController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns this compact API manifest.")]
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
            Summary = "Returns the Unity Editor execution status.")]
        private void Status(UnionAirRequestContext ctx)
            => new EditorStatusHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "logs",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns captured Unity Console logs.",
            OptionalQuery = new string[] { "type", "search", "limit" })]
        private void Logs(UnionAirRequestContext ctx)
            => new EditorLogsHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "refresh",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Refreshes the Unity AssetDatabase.")]
        private void Refresh(UnionAirRequestContext ctx)
            => new EditorRefreshHandler().Handle(ctx.Request, ctx.Response);

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
            OptionalBody = new string[] { "parent", "scenePath", "allowWhilePlaying" })]
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
            Summary = "Updates GameObject properties.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" },
            OptionalBody = new string[] { "name", "isActive", "tag", "layer", "transform", "allowWhilePlaying" })]
        private void Update(UnionAirRequestContext ctx)
            => new GameObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "primitive",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Creates a primitive GameObject.",
            RequiredBody = new string[] { "type" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "name", "parent", "scenePath", "allowWhilePlaying" })]
        private void Primitive(UnionAirRequestContext ctx)
            => new GameObjectPrimitiveHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "instantiate",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Instantiates a prefab asset into the scene.",
            RequiredBody = new string[] { "guid or assetPath" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "name", "parent", "scenePath", "allowWhilePlaying" })]
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
            Summary = "Moves a GameObject to a new parent.",
            RequiredBody = new string[] { "target" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "parent", "scenePath", "allowWhilePlaying" })]
        private void Reparent(UnionAirRequestContext ctx)
            => new GameObjectReparentHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "batch",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Runs multiple GameObject operations in one Undo group.",
            RequiredBody = new string[] { "operations" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "scenePath", "target", "parent", "allowWhilePlaying" })]
        private void Batch(UnionAirRequestContext ctx)
            => new GameObjectBatchHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("gameobjects/components")]
    internal sealed class ComponentsController
    {
        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Adds a component to a GameObject.",
            RequiredBody = new string[] { "target", "type" },
            OptionalQuery = new string[] { "allowWhilePlaying" },
            OptionalBody = new string[] { "scenePath", "allowWhilePlaying" })]
        private void Add(UnionAirRequestContext ctx)
            => new ComponentWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("DELETE", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Removes a component from a GameObject.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" })]
        private void Remove(UnionAirRequestContext ctx)
            => new ComponentWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
            Summary = "Updates serialized component properties, including object references.",
            RequiredQuery = new string[] { "target" },
            OptionalQuery = new string[] { "scenePath", "allowWhilePlaying" },
            RequiredBody = new string[] { "properties" },
            OptionalBody = new string[] { "allowWhilePlaying" })]
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
            Summary = "Creates a material asset.",
            RequiredBody = new string[] { "assetPath", "shader" })]
        private void Create(UnionAirRequestContext ctx)
            => new MaterialWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            PlayModePolicy = UnionAirPlayModePolicy.Blocked,
            Summary = "Updates material properties.",
            RequiredQuery = new string[] { "guid" },
            RequiredBody = new string[] { "properties" })]
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
}


