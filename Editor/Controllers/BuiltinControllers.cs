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
            RequiredQuery = new string[] { "path" },
            OptionalQuery = new string[] { "width", "height", "format", "quality" })]
        private void Capture(UnionAirRequestContext ctx)
            => new CameraHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "capture/image",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Renders a camera and returns binary image data.",
            RequiredQuery = new string[] { "path" },
            OptionalQuery = new string[] { "width", "height", "format", "quality" })]
        private void CaptureImage(UnionAirRequestContext ctx)
            => new CameraHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("scene")]
    internal sealed class SceneController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns metadata for the active scene.")]
        private void Info(UnionAirRequestContext ctx)
            => new SceneHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "hierarchy",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns the scene GameObject hierarchy.")]
        private void Hierarchy(UnionAirRequestContext ctx)
            => new SceneHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "stats",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns aggregate scene statistics.")]
        private void Stats(UnionAirRequestContext ctx)
            => new SceneStatsHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "save",
            Category = UnionAirEndpointCategories.AssetWrite,
            Summary = "Saves the active scene.")]
        private void Save(UnionAirRequestContext ctx)
            => new SceneSaveHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("gameobjects")]
    internal sealed class GameObjectsController
    {
        [UnionAirEndpoint("GET", "",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Returns GameObject details including components.",
            RequiredQuery = new string[] { "path" })]
        private void Detail(UnionAirRequestContext ctx)
            => new GameObjectHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Creates an empty GameObject.",
            RequiredBody = new string[] { "name" },
            OptionalBody = new string[] { "parentPath" })]
        private void Create(UnionAirRequestContext ctx)
            => new GameObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("DELETE", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Deletes a GameObject.",
            RequiredQuery = new string[] { "path" })]
        private void Delete(UnionAirRequestContext ctx)
            => new GameObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Updates GameObject properties.",
            RequiredQuery = new string[] { "path" },
            OptionalBody = new string[] { "name", "isActive", "tag", "layer", "transform" })]
        private void Update(UnionAirRequestContext ctx)
            => new GameObjectWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "primitive",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Creates a primitive GameObject.",
            RequiredBody = new string[] { "type" },
            OptionalBody = new string[] { "name", "parentPath" })]
        private void Primitive(UnionAirRequestContext ctx)
            => new GameObjectPrimitiveHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "instantiate",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Instantiates a prefab asset into the scene.",
            RequiredBody = new string[] { "guid or assetPath" },
            OptionalBody = new string[] { "name", "parentPath" })]
        private void Instantiate(UnionAirRequestContext ctx)
            => new GameObjectInstantiateHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "duplicate",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Duplicates a GameObject.",
            RequiredQuery = new string[] { "path" })]
        private void Duplicate(UnionAirRequestContext ctx)
            => new GameObjectDuplicateHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "reparent",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Moves a GameObject to a new parent.",
            RequiredBody = new string[] { "path" },
            OptionalBody = new string[] { "parentPath" })]
        private void Reparent(UnionAirRequestContext ctx)
            => new GameObjectReparentHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "batch",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Runs multiple GameObject operations in one Undo group.",
            RequiredBody = new string[] { "operations" })]
        private void Batch(UnionAirRequestContext ctx)
            => new GameObjectBatchHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("gameobjects/components")]
    internal sealed class ComponentsController
    {
        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Adds a component to a GameObject.",
            RequiredBody = new string[] { "path", "type" })]
        private void Add(UnionAirRequestContext ctx)
            => new ComponentWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("DELETE", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Removes a component from a GameObject.",
            RequiredQuery = new string[] { "path", "type" })]
        private void Remove(UnionAirRequestContext ctx)
            => new ComponentWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.SceneWrite,
            Summary = "Updates serialized component properties, including object references.",
            RequiredQuery = new string[] { "path", "type" },
            RequiredBody = new string[] { "properties" })]
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
            Summary = "Deletes an asset and its meta file.",
            PathParams = new string[] { "guid" })]
        private void Delete(UnionAirRequestContext ctx)
            => new AssetDeleteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "move",
            Category = UnionAirEndpointCategories.AssetWrite,
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
            Summary = "Creates a prefab from a scene GameObject.",
            RequiredBody = new string[] { "goPath", "assetPath", "mode" })]
        private void Create(UnionAirRequestContext ctx)
            => new PrefabCreateHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "apply",
            Category = UnionAirEndpointCategories.AssetWrite,
            Summary = "Applies prefab instance overrides.",
            RequiredBody = new string[] { "goPath" })]
        private void Apply(UnionAirRequestContext ctx)
            => new PrefabOverrideHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("POST", "revert",
            Category = UnionAirEndpointCategories.AssetWrite,
            Summary = "Reverts a prefab instance.",
            RequiredBody = new string[] { "goPath" })]
        private void Revert(UnionAirRequestContext ctx)
            => new PrefabOverrideHandler().Handle(ctx.Request, ctx.Response);
    }

    [UnionAirController("assets/materials")]
    internal sealed class MaterialsController
    {
        [UnionAirEndpoint("POST", "",
            Category = UnionAirEndpointCategories.AssetWrite,
            Summary = "Creates a material asset.",
            RequiredBody = new string[] { "assetPath", "shader" })]
        private void Create(UnionAirRequestContext ctx)
            => new MaterialWriteHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("PATCH", "",
            Category = UnionAirEndpointCategories.AssetWrite,
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
            OptionalQuery = new string[] { "name", "component", "tag", "layer", "active", "assetGuid", "includeComponents" })]
        private void GameObjects(UnionAirRequestContext ctx)
            => new SearchGameObjectsHandler().Handle(ctx.Request, ctx.Response);

        [UnionAirEndpoint("GET", "asset-refs",
            Category = UnionAirEndpointCategories.Read,
            Summary = "Finds scene references to an asset.",
            RequiredQuery = new string[] { "guid" })]
        private void AssetRefs(UnionAirRequestContext ctx)
            => new SearchAssetRefsHandler().Handle(ctx.Request, ctx.Response);
    }
}


