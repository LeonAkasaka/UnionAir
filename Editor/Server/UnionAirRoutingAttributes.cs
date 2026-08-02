using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LeonAkasaka.UnionAir.Editor.InputSystem")]
[assembly: InternalsVisibleTo("LeonAkasaka.UnionAir.Editor.TestRunner")]
[assembly: InternalsVisibleTo("LeonAkasaka.UnionAir.Editor.Tests")]

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Marks an assembly as a first-party UnionAir assembly so that its controllers
    /// are registered as built-in endpoints (under <c>/api/</c>) rather than custom
    /// endpoints (under <c>/api/custom/</c>).
    /// </summary>
    /// <remarks>
    /// This attribute is <c>internal</c> and only accessible to assemblies explicitly
    /// listed in <c>InternalsVisibleTo</c>. Unofficial assemblies cannot apply it.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    internal sealed class UnionAirBuiltinAssemblyAttribute : Attribute { }

    /// <summary>
    /// Identifies whether a discovered route is provided by UnionAir or by an external Editor assembly.
    /// </summary>
    public enum UnionAirRouteSource
    {
        /// <summary>A route declared in UnionAir's own editor assembly.</summary>
        Builtin,

        /// <summary>A route declared by another Editor assembly and exposed under <c>/api/custom</c>.</summary>
        Custom
    }

    /// <summary>
    /// Describes the possible side effects of an API category for help output and operator review.
    /// </summary>
    /// <remarks>
    /// Values can be combined for custom categories that may affect multiple Unity Editor systems.
    /// <see cref="None"/> represents read-only endpoints and is serialized as <c>readOnly</c> in
    /// <c>GET /api/help</c>.
    /// </remarks>
    [Flags]
    public enum UnionAirEndpointRisk
    {
        /// <summary>The endpoint is expected to be read-only.</summary>
        None = 0,

        /// <summary>The endpoint may modify scene objects, components, transforms, or scene state.</summary>
        SceneUpdate = 1 << 0,

        /// <summary>The endpoint may modify assets, project files, prefabs, materials, or saved scenes.</summary>
        AssetUpdate = 1 << 1,

        /// <summary>The endpoint may enter, exit, pause, or step Play mode.</summary>
        PlayMode = 1 << 2,

        /// <summary>The endpoint has a custom or tool-specific risk profile.</summary>
        Custom = 1 << 3,

        /// <summary>The endpoint risk depends on request parameters or payload.</summary>
        RequestDependent = 1 << 4,

        /// <summary>The endpoint may change Unity Editor UI or selection state without directly modifying scene or asset data.</summary>
        EditorState = 1 << 5,

        /// <summary>The endpoint may enable profiling or capture diagnostic artifacts containing project data.</summary>
        Profiling = 1 << 6,

        /// <summary>
        /// The endpoint may produce executable output, such as a player build written outside the
        /// Unity project's asset folders.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="AssetUpdate"/>: the artifact is a runnable program rather than a
        /// project file, and it is not written through the AssetDatabase.
        /// </remarks>
        ExecutableOutput = 1 << 7
    }

    /// <summary>
    /// Declares whether an endpoint may be called while the Unity Editor is in Play mode.
    /// </summary>
    public enum UnionAirPlayModePolicy
    {
        /// <summary>The endpoint may be called in Edit mode or Play mode.</summary>
        Allowed,

        /// <summary>The endpoint is rejected while the Editor is in Play mode.</summary>
        Blocked,

        /// <summary>The endpoint is rejected in Play mode unless the request explicitly opts in.</summary>
        ExplicitOptIn
    }

    /// <summary>
    /// Declares whether an endpoint may be called while a Unity Test Framework run is active.
    /// </summary>
    public enum UnionAirTestRunPolicy
    {
        /// <summary>The endpoint is rejected while any test run is active.</summary>
        Blocked,

        /// <summary>The endpoint may be called while a test run is active.</summary>
        Allowed
    }

    /// <summary>
    /// Built-in category identifiers used by UnionAir endpoint metadata.
    /// </summary>
    public static class UnionAirEndpointCategories
    {
        /// <summary>Read-only built-in endpoints.</summary>
        public const string Read = "read";

        /// <summary>Built-in endpoints that may modify scene objects or scene state.</summary>
        public const string SceneWrite = "sceneWrite";

        /// <summary>Built-in endpoints that may modify assets or project files.</summary>
        public const string AssetWrite = "assetWrite";

        /// <summary>Built-in endpoints that may control Play mode.</summary>
        public const string PlayMode = "playMode";

        /// <summary>Built-in endpoints that execute request-dependent Unity Editor actions.</summary>
        public const string EditorActions = "editorActions";

        /// <summary>Built-in endpoints that discover and execute Unity Test Framework tests.</summary>
        public const string TestRunner = "testRunner";

        /// <summary>Built-in endpoints that capture performance and memory diagnostics.</summary>
        public const string Profiling = "profiling";

        /// <summary>Built-in endpoints that report build configuration and produce player builds.</summary>
        public const string Build = "build";

        /// <summary>Default category identifier for custom endpoints when no more specific category is supplied.</summary>
        public const string Custom = "custom";
    }

    /// <summary>
    /// Marks a class as a UnionAir API controller whose methods can declare HTTP routes.
    /// </summary>
    /// <remarks>
    /// Controllers in UnionAir's editor assembly are exposed under <c>/api/{route}</c>.
    /// Controllers in other Editor assemblies are treated as custom controllers and exposed under
    /// <c>/api/custom/{route}</c>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class UnionAirControllerAttribute : Attribute
    {
        /// <summary>
        /// Creates a controller route declaration.
        /// </summary>
        /// <param name="route">
        /// Controller route segment, without the <c>/api</c> prefix. For example, <c>assets</c>
        /// produces built-in routes under <c>/api/assets</c>.
        /// </param>
        public UnionAirControllerAttribute(string route)
        {
            Route = route;
        }

        /// <summary>
        /// Controller route segment declared by the attribute.
        /// </summary>
        public string Route { get; }
    }

    /// <summary>
    /// Defines metadata for an API category used by endpoint discovery, category enablement, and help output.
    /// </summary>
    /// <remarks>
    /// Custom controllers should declare each non-built-in category they use. Undefined custom categories
    /// are still discovered, but they are reported with default metadata and a help/UI warning.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class UnionAirCategoryAttribute : Attribute
    {
        /// <summary>
        /// Creates a category definition.
        /// </summary>
        /// <param name="id">
        /// Stable category identifier referenced by <see cref="UnionAirEndpointAttribute.Category"/>.
        /// </param>
        public UnionAirCategoryAttribute(string id)
        {
            Id = id;
        }

        /// <summary>
        /// Stable category identifier used by endpoint metadata and persisted category settings.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Human-readable category label shown in the EditorWindow and <c>GET /api/help</c>.
        /// When empty, the category ID is used as the display name.
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Risk metadata for all endpoints in this category.
        /// </summary>
        public UnionAirEndpointRisk Risk { get; set; } = UnionAirEndpointRisk.Custom;

        /// <summary>
        /// Whether users can enable or disable this category from the UnionAir EditorWindow.
        /// </summary>
        public bool CanDisable { get; set; } = true;

        /// <summary>
        /// Whether this category is enabled before users make an explicit override.
        /// </summary>
        public bool EnabledByDefault { get; set; }
    }

    /// <summary>
    /// Marks a controller method as an HTTP endpoint.
    /// </summary>
    /// <remarks>
    /// Endpoint methods must accept exactly one <see cref="UnionAirRequestContext"/> parameter and return
    /// <c>void</c>. The attribute supplies route and help metadata only; request body parsing remains explicit
    /// inside the endpoint implementation.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class UnionAirEndpointAttribute : Attribute
    {
        /// <summary>
        /// Creates an endpoint route declaration.
        /// </summary>
        /// <param name="method">HTTP method such as <c>GET</c>, <c>POST</c>, <c>PATCH</c>, or <c>DELETE</c>.</param>
        /// <param name="route">
        /// Method route segment relative to the controller route. Use an empty string for the controller root.
        /// Template parameters use braces, for example <c>{guid}</c>.
        /// </param>
        public UnionAirEndpointAttribute(string method, string route)
        {
            Method = method;
            Route = route;
        }

        /// <summary>
        /// HTTP method handled by this endpoint.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Method route segment relative to the controller route.
        /// </summary>
        public string Route { get; }

        /// <summary>
        /// Category identifier used for enablement, grouping, risk metadata, and help output.
        /// </summary>
        public string Category { get; set; } = UnionAirEndpointCategories.Read;

        /// <summary>
        /// Whether this endpoint can be called while the Unity Editor is in Play mode.
        /// </summary>
        public UnionAirPlayModePolicy PlayModePolicy { get; set; } = UnionAirPlayModePolicy.Allowed;

        /// <summary>
        /// Whether this endpoint can be called while a Unity Test Framework run is active.
        /// </summary>
        public UnionAirTestRunPolicy TestRunPolicy { get; set; } = UnionAirTestRunPolicy.Blocked;

        /// <summary>
        /// Optional endpoint-specific risk metadata used when it differs from the category risk.
        /// </summary>
        public UnionAirEndpointRisk Risk { get; set; } = UnionAirEndpointRisk.None;

        /// <summary>
        /// Whether <see cref="Risk"/> should override the endpoint category risk.
        /// </summary>
        public bool UseRiskOverride { get; set; }

        /// <summary>
        /// Short description included in <c>GET /api/help</c> and the generated endpoint list.
        /// </summary>
        public string Summary { get; set; } = "";

        /// <summary>
        /// Required route template parameters, such as <c>guid</c> for <c>{guid}</c>.
        /// </summary>
        public string[] PathParams { get; set; } = new string[0];

        /// <summary>
        /// Required query string parameter names.
        /// </summary>
        public string[] RequiredQuery { get; set; } = new string[0];

        /// <summary>
        /// Optional query string parameter names.
        /// </summary>
        public string[] OptionalQuery { get; set; } = new string[0];

        /// <summary>
        /// Required JSON body field names.
        /// </summary>
        public string[] RequiredBody { get; set; } = new string[0];

        /// <summary>
        /// Optional JSON body field names.
        /// </summary>
        public string[] OptionalBody { get; set; } = new string[0];

        /// <summary>
        /// Example JSON request body shown when <c>GET /api/help?detail=full</c> is requested.
        /// </summary>
        public string RequestExample { get; set; } = "";

        /// <summary>
        /// Example JSON response body shown when <c>GET /api/help?detail=full</c> is requested.
        /// </summary>
        public string ResponseExample { get; set; } = "";
    }
}
