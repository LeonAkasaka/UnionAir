# Custom Controllers
**English** | [日本語](custom-controllers.ja.md)

Custom controllers let application-side Editor assemblies add project-specific REST endpoints without modifying UnionAir itself. Controllers in UnionAir's own assembly are treated as built-in; controllers in other Editor assemblies are treated as custom and are exposed under `/api/custom/...`.

## Controller Setup

Declare a controller class with `[UnionAirController]` and one or more methods with `[UnionAirEndpoint]`. Endpoint methods must accept exactly one `UnionAirRequestContext` parameter and return `void`.

```csharp
using LeonAkasaka.UnionAir.Editor;

[UnionAirController("my-tool")]
[UnionAirCategory(
    "debug",
    DisplayName = "Debug Tools",
    Risk = UnionAirEndpointRisk.Custom,
    CanDisable = true,
    EnabledByDefault = false)]
public class MyToolController
{
    [UnionAirEndpoint(
        "GET",
        "status",
        Category = "debug",
        Summary = "Returns custom tool status")]
    public void Status(UnionAirRequestContext ctx)
    {
        RestResponse.Send(ctx.Response, "{\"status\":\"ok\"}");
    }
}
```

This example registers `GET /api/custom/my-tool/status`.

Custom handlers are disabled by default. Enable them in **Window > UnionAir > REST Bridge > Custom Handlers**. Custom categories can then be enabled or disabled independently; their checkboxes remain disabled while the Custom Handlers master switch is off.

`Category` is a string so custom extensions can define their own grouping labels in `/api/help` and the EditorWindow. Built-in endpoints use `UnionAirEndpointCategories.Read`, `SceneWrite`, `AssetWrite`, `PlayMode`, `EditorActions`, `Profiling`, and the optional `TestRunner`. Category metadata controls enablement and default risk reporting. `Risk` is descriptive metadata for tools and LLMs; category enablement controls whether requests are accepted. Endpoints can set `UseRiskOverride = true` and `Risk = ...` when a route has a narrower risk profile than its category.

## Requests and Responses

Use `UnionAirRequestContext.Request` to inspect the incoming request, `UnionAirRequestContext.RouteValues` for route template parameters, and `UnionAirRequestContext.Response` to write the response.

These are `UnionAirRequest` and `UnionAirResponse`, UnionAir's own types rather than `System.Net.HttpListenerRequest` and `HttpListenerResponse`. The transport stays an implementation detail, and every byte a handler writes passes through a type UnionAir owns. The request exposes `HttpMethod`, `Url`, `QueryString`, `Headers`, `HasEntityBody`, `ContentType`, and `ContentLength64`; the response exposes `StatusCode`, `ContentType`, `ContentLength64`, `OutputStream`, `AddHeader`, and `Close`. Read the request body through `RequestBodyReader` rather than a stream: it caches what it reads, so every reader of a given request sees the same body.

Custom endpoints inherit UnionAir's transport policy. Requests carrying an `Origin` header are rejected before the controller runs, so browser `fetch` and XMLHttpRequest clients are unsupported by default. A request with a non-empty body must use `Content-Type: application/json`; an empty request does not require a content type.

`RequestBodyReader` provides the same lightweight JSON helpers used by UnionAir's built-in handlers:

```csharp
var body = RequestBodyReader.ReadString(ctx.Request);
var name = RequestBodyReader.GetString(body, "name");
var targetJson = RequestBodyReader.GetObject(body, "target");
if (!RequestBodyReader.TryGetStringArray(body, "tags", out var tags))
    RestResponse.SendError(ctx.Response, "tags must be a string array", 400);
```

`TryGetStringArray` reads only a top-level key, returns an empty array when the key is absent, and returns `false` when a present value is not a valid array of strings.

`RestResponse` writes JSON responses with the same content type, UTF-8 encoding, and error shape as built-in endpoints. It does not add CORS headers:

```csharp
RestResponse.Send(ctx.Response, "{\"status\":\"ok\"}");
RestResponse.SendError(ctx.Response, "Missing required field: target", 400);
```

`RestResponse.AddCorsHeaders` is retained as an obsolete no-op for source compatibility. Custom controllers that require browser access must implement and secure their own transport rather than bypass UnionAir's request policy.

Handlers that complete later must call `ctx.Defer()` before returning and eventually close the response themselves. Deferred background I/O may use the .NET response stream, but it must not call Unity APIs from a worker thread.

`RestResponse.FormatNullableString(value)` returns an escaped JSON string literal, or the JSON literal `null` only when `value` is null. Empty strings remain `""`.

## Reference Resolution

Custom controllers can use `UnionAirReferenceResolver` to parse and resolve the same typed object references accepted by built-in endpoints.

```csharp
using LeonAkasaka.UnionAir.Editor;
using UnityEngine;

[UnionAirController("selection")]
[UnionAirCategory(
    "selectionTools",
    DisplayName = "Selection Tools",
    Risk = UnionAirEndpointRisk.SceneUpdate,
    EnabledByDefault = false)]
public class SelectionController
{
    [UnionAirEndpoint(
        "POST",
        "ping",
        Category = "selectionTools",
        PlayModePolicy = UnionAirPlayModePolicy.ExplicitOptIn,
        Summary = "Resolves a GameObject target and returns its path",
        RequiredBody = new string[] { "target" },
        OptionalBody = new string[] { "scenePath", "allowWhilePlaying" },
        OptionalQuery = new string[] { "allowWhilePlaying" })]
    public void Ping(UnionAirRequestContext ctx)
    {
        var body = RequestBodyReader.ReadString(ctx.Request);

        if (!UnionAirReferenceResolver.TryResolveSceneFromRequest(
                ctx.Request, body, out var scene, out var error, out var statusCode) ||
            !UnionAirReferenceResolver.TryReadBody(
                body, "target", out var target, out error, out statusCode) ||
            !UnionAirReferenceResolver.TryResolveGameObject(
                scene, target, "target", out var go, out error, out statusCode))
        {
            RestResponse.SendError(ctx.Response, error, statusCode);
            return;
        }

        var id = UnionAirReferenceResolver.GetGlobalObjectId(go);
        RestResponse.Send(ctx.Response,
            "{\"name\":\"" + RestResponse.EscapeJson(go.name) + "\"," +
            "\"globalObjectId\":\"" + RestResponse.EscapeJson(id) + "\"}");
    }
}
```

Supported typed object reference payloads:

```json
{ "type": "hierarchyPath", "value": "Canvas/Button" }
{ "type": "componentPath", "value": "Canvas/Button:UnityEngine.UI.Text" }
{ "type": "globalObjectId", "value": "GlobalObjectId_V1-..." }
```

`type` defaults to `hierarchyPath` when omitted. `scenePath` remains a separate loaded scene selector and is used only for `hierarchyPath` and `componentPath` resolution.

Useful resolver methods include:

| Method | Purpose |
|--------|---------|
| `TryReadQuery` | Parse a typed object reference from a query string field |
| `TryReadBody` | Parse a typed object reference from a JSON body field |
| `TryParse` | Parse a raw object-reference JSON value |
| `TryResolveSceneFromRequest` | Resolve `scenePath` from query or body, defaulting to the active scene |
| `TryResolveOptionalScene` | Resolve a scene path/name or default to the active scene |
| `TryResolveRequiredScene` | Resolve a required loaded scene path/name |
| `TryResolveGameObject` | Resolve a reference to a `GameObject` |
| `TryResolveComponent` | Resolve a reference to a `Component` |
| `TryResolveGameObjectOrComponent` | Resolve a reference to either object kind |
| `TryResolveObject` | Resolve a scene `GameObject` or `Component` as `UnityEngine.Object` |
| `TryResolveCamera` | Resolve a camera by GameObject or Camera component reference |
| `GetGlobalObjectId` | Serialize a Unity object to a `GlobalObjectId` string |
| `TryResolveGlobalObjectId` | Resolve a `GlobalObjectId` string to `UnityEngine.Object` |
| `TryResolveGlobalObjectIdAsGameObject` | Resolve a `GlobalObjectId` string to a `GameObject` |
| `TryResolveGlobalObjectIdAsComponent` | Resolve a `GlobalObjectId` string to a `Component` |
| `TryResolveGlobalObjectIdAsGameObjectOrComponent` | Resolve a `GlobalObjectId` string to a `GameObject` or `Component` |
| `TryResolveAssetReference` | Resolve an asset by `assetGuid` or `assetPath` |

Resolver methods return `false` with an error message and status code. They do not write the HTTP response, so controllers can decide whether to return the error directly or combine it with custom validation.

Typical status codes:

| Status | Cause |
|--------|-------|
| `400` | Missing or malformed input |
| `404` | Scene, object, component, or asset not found |
| `409` | Scene name is ambiguous |
| `422` | Input resolves to the wrong object kind or type |

## Asset References

Use asset references when a custom endpoint needs to resolve project assets instead of scene objects.

```csharp
var body = RequestBodyReader.ReadString(ctx.Request);
var assetJson = RequestBodyReader.GetObject(body, "asset");

if (!UnionAirReferenceResolver.TryResolveAssetReference(
        assetJson,
        typeof(Texture2D),
        "asset",
        out var asset,
        out var error,
        out var statusCode))
{
    RestResponse.SendError(ctx.Response, error, statusCode);
    return;
}
```

Asset reference payloads may use `assetGuid` or `assetPath`. `assetType` is optional and must resolve to a `UnityEngine.Object` type when present.

```json
{ "assetGuid": "a1b2c3...", "assetType": "UnityEngine.Texture2D" }
{ "assetPath": "Assets/Textures/Icon.png" }
```

## Play Mode and Security

Custom controllers run inside the Unity Editor process with the permissions of the Editor assembly that defines them. UnionAir does not sandbox custom controller code. Disabling Custom Handlers only removes their HTTP routes; it does not prevent their assembly from loading or their code from running through another Editor entry point. Category and handler toggles limit API exposure and accidental operations, not malicious project code.

Use category metadata and `PlayModePolicy` deliberately:

| Setting | Guidance |
|---------|----------|
| `UnionAirEndpointRisk.None` | Read-only endpoints |
| `UnionAirEndpointRisk.SceneUpdate` | Endpoints that modify scene objects or scene state |
| `UnionAirEndpointRisk.AssetUpdate` | Endpoints that modify assets, project files, prefabs, or saved scenes |
| `UnionAirEndpointRisk.PlayMode` | Endpoints that enter, exit, pause, or step Play mode |
| `UnionAirEndpointRisk.Custom` | Tool-specific or mixed behavior |
| `UnionAirEndpointRisk.RequestDependent` | Endpoints whose side effects depend on request parameters or payload |
| `UnionAirEndpointRisk.EditorState` | Endpoints that change Editor UI or selection state without directly modifying scene or asset data |
| `UnionAirEndpointRisk.Profiling` | Endpoints that enable profiling or capture diagnostic artifacts containing project data |
| `UnionAirPlayModePolicy.Blocked` | Persistent scene or asset writes that should never run in Play mode |
| `UnionAirPlayModePolicy.ExplicitOptIn` | Transient scene-object changes that require both Editor-side permission and `allowWhilePlaying=true` |
| `UnionAirTestRunPolicy.Blocked` | Default. Rejects the endpoint while any Unity Test Framework run is active |
| `UnionAirTestRunPolicy.Allowed` | Reserve for operational observation or control that is safe during tests |

UnionAir applies the test-run lock to custom endpoints as well as built-in endpoints. Leave the default `Blocked` policy unless the route is specifically designed to observe or control an active run.

Custom handlers are disabled by default, and custom categories can be disabled independently from the EditorWindow. Keep custom endpoints narrowly scoped, validate all request fields, and return explicit errors for rejected operations.
