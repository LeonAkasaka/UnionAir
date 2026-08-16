# API Reference — General
**English** | [日本語](general.ja.md)

Base URL: `http://localhost:<port>/api/`, read from `<project>/.unionair/endpoint.txt` at connection time. See the [API Reference index](../api-reference.md) for endpoint discovery, response conventions, and category/security notes.

---

## GET /api/help

Returns a compact API manifest for LLMs, MCP bridges, and other tools that cannot access this documentation directly. The endpoint list is generated from `[UnionAirController]` and `[UnionAirEndpoint]` route metadata.

### Response

```json
{
  "name": "com.leonakasaka.unionair",
  "displayName": "UnionAir - Unity REST Bridge",
  "version": "0.6.0",
  "baseUrl": "http://localhost:51234/api",
  "description": "UnionAir exposes Unity Editor state and selected Editor operations as a local REST API.",
  "categories": [
    {
      "id": "read",
      "displayName": "Read",
      "source": "builtin",
      "enabled": true,
      "canDisable": false,
      "enabledByDefault": true,
      "risk": ["readOnly"],
      "blockedDuring": []
    }
  ],
  "endpoints": [
    {
      "method": "GET",
      "path": "/api/health",
      "routeTemplate": "/api/health",
      "source": "builtin",
      "enabled": true,
      "category": "read",
      "summary": "Checks whether the server is running and identifies its Unity project.",
      "risk": ["readOnly"],
      "playModePolicy": "allowed",
      "testRunPolicy": "allowed",
      "blockedDuring": [],
      "pathParams": [],
      "requiredQuery": [],
      "optionalQuery": [],
      "requiredBody": [],
      "optionalBody": []
    }
  ]
}
```

Each endpoint item includes the HTTP method, path, category, short summary, risk metadata, and compact parameter/body field lists. Category items describe API grouping, current enablement, and the risk profile for endpoints in that category.

| Field | Type | Description |
|-----------|-----|------|
| `categories[].id` | string | Stable category ID referenced by endpoints |
| `categories[].displayName` | string | Human-readable category label |
| `categories[].source` | string | `builtin` or `custom` |
| `categories[].enabled` | bool | Whether endpoints in the category are currently enabled |
| `categories[].canDisable` | bool | Whether the category can be disabled in the EditorWindow |
| `categories[].enabledByDefault` | bool | Whether the category starts enabled before user overrides |
| `categories[].risk` | string[] | `readOnly`, `sceneUpdate`, `assetUpdate`, `playMode`, `custom`, `requestDependent`, `editorState`, `profiling`, or `executableOutput` |
| `categories[].blockedDuring` | string[] | Editor activities during which endpoints in the category are rejected; see [Editor Activities](activities.md) |
| `endpoints[].source` | string | `builtin` or `custom` |
| `endpoints[].enabled` | bool | Whether the endpoint is currently enabled |
| `endpoints[].routeTemplate` | string | Route template used by the attribute router |
| `endpoints[].category` | string | Category used for discovery/UI grouping. Built-in constants include `read`, `sceneWrite`, `assetWrite`, `playMode`, `editorActions`, `testRunner`, `profiling`, `build`, and `custom`; custom endpoints may use any stable category string. |
| `endpoints[].risk` | string[] | Risk inherited from the endpoint category, unless the endpoint declares a more specific risk override |
| `endpoints[].playModePolicy` | string | `allowed`, `blocked`, or `explicitOptIn`. `blocked` endpoints return `409` in Play mode. `explicitOptIn` endpoints require both the Editor setting and `allowWhilePlaying=true` in Play mode. |
| `endpoints[].testRunPolicy` | string | `allowed` or `blocked`. Endpoints are blocked during a test run unless explicitly allowed. |
| `endpoints[].blockedDuring` | string[] | Every Editor activity during which the endpoint is rejected, in priority order, including the ones enforced by `playModePolicy` and `testRunPolicy`. Read this instead of reconstructing the answer from three fields; see [Editor Activities](activities.md) |
| `endpoints[].requiredQuery` | string[] | Required query string parameters |
| `endpoints[].optionalQuery` | string[] | Optional query string parameters |
| `endpoints[].requiredBody` | string[] | Required JSON body fields |
| `endpoints[].optionalBody` | string[] | Optional JSON body fields |

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `detail` | (compact) | `full` adds per-endpoint detail fields such as request/response examples to each endpoint item. |
| `category` | (all) | Filters categories and endpoints to a single category ID (case-insensitive), e.g. `read`, `sceneWrite`, `assetWrite`, `playMode`, `editorActions`, `testRunner`, `profiling`, `build`. |
| `includeDisabled` | `false` | When `true`, includes disabled custom categories/endpoints and endpoints with route conflicts. Built-in categories/endpoints are always listed with their current `enabled` state. |
| `source` | `all` | `builtin`, `custom`, or `all` |

> This endpoint is intentionally a lightweight discovery manifest, not a full OpenAPI schema. Use this document for detailed request and response examples. When adding or changing an endpoint, update its `[UnionAirEndpoint]` metadata so `/api/help`, routing, and the EditorWindow endpoint list stay in sync.

---

## Custom Controllers

Application-side Editor assemblies can add custom controllers under `/api/custom/...`. See [Custom Controllers](../custom-controllers.md) for controller setup, category metadata, request parsing, reference resolution helpers, Play Mode policy, and security guidance.

---

## GET /api/health

Checks whether the server is running and identifies the Unity project served by that Editor
process. A client that discovered the URL through `.unionair/endpoint.txt` must compare
`projectPath` with the project directory containing that file; a mismatch means the discovery file
is stale and now points at another project's Editor.

### Response

```json
{
  "status": "ok",
  "unityVersion": "6000.3.5f2",
  "projectPath": "C:\\Work\\MyProject"
}
```

`projectPath` is the absolute parent directory of the project's `Assets` directory. Compare paths
using the host platform's path semantics (case-insensitive on Windows).

---

## Object References

Scene GameObjects and Components expose Unity `GlobalObjectId` strings in read responses. Write and detail APIs use typed object references for targets, sources, and parents.

Reference shape:

```json
{ "type": "hierarchyPath", "value": "Canvas/Button" }
```

Object references must be JSON objects. A bare string such as `"Canvas/Button"` is not accepted and returns `400 Bad Request`.

### Naming one object inside a file

An asset reference carries `localIdentifier`, the object's local file id as a decimal string:

```json
{
  "assetGuid": "8f0565f2...",
  "assetPath": "Assets/Models/unitychan.fbx",
  "assetType": "UnityEngine.Mesh",
  "localIdentifier": "4300028"
}
```

`assetGuid` and `assetPath` identify a *file*. A model file holds many meshes, a sprite sheet many sprites, and `Library/unity default resources` holds every built-in object, so a file and a type together do not name one object. `localIdentifier` does, and reads report it for every asset reference — including references to single-object assets, so that the shape of a response never depends on what it points at.

It is a string because a local file id is 64 bits and a JSON number is not.

Writes take it as **optional**, and resolve in one of three ways:

| The request | What happens |
|---|---|
| Carries `localIdentifier` | That object is resolved. This is what a client echoing a read sends |
| No `localIdentifier`, and the path holds one object of the required type | Resolved. Most references are this — a material, a texture, a prefab |
| No `localIdentifier`, and the path holds more than one | `400`, naming how many candidates the path holds |

The third case previously answered `200` having bound whichever object Unity returned, with no way for the client to tell which one it got.

[`GET /api/assets/{guid}`](assets.md#get-apiassetsguid) lists a file's `subAssets` with their identifiers. Built-in resources are the exception: they are reachable and writable by `localIdentifier`, but they are not sub-assets of a project asset and are not listed, so the way to obtain one is to read an existing reference to it.

### Field types

Every field of a reference — `type`, `value`, `scenePath`, `assetGuid`, `assetPath`, `assetType`, `localIdentifier` — must be a JSON string, or `null`, which reads the same as omitting it. A field carrying a number, a boolean, an array, or an object answers `400` naming the field, rather than being coerced to text: `{"assetGuid": 5}` is a value of the wrong type, not the GUID `"5"`.

| Type | Value |
|------|-------|
| `hierarchyPath` | GameObject hierarchy path, such as `Canvas/Button`. This is the default when `type` is omitted |
| `componentPath` | Component path in `GameObjectPath:ComponentType` form, such as `Canvas/Button:UnityEngine.UI.Text` |
| `globalObjectId` | Unity GlobalObjectId string for a scene GameObject or Component |

`scenePath` remains a separate loaded scene selector and is used only for `hierarchyPath` and `componentPath` resolution. Scene asset responses use asset `guid` values, not `globalObjectId`.

Custom controllers can parse and resolve this same reference shape with `UnionAirReferenceResolver`; see [Custom Controllers](../custom-controllers.md).

---
