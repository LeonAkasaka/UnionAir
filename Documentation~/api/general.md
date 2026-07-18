# API Reference — General
**English** | [日本語](general.ja.md)

Base URL: `http://localhost:<port>/api/` (default port: **8765**). See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

---

## GET /api/help

Returns a compact API manifest for LLMs, MCP bridges, and other tools that cannot access this documentation directly. The endpoint list is generated from `[UnionAirController]` and `[UnionAirEndpoint]` route metadata.

### Response

```json
{
  "name": "com.leonakasaka.unionair",
  "displayName": "UnionAir - Unity REST Bridge",
  "version": "0.2.0",
  "baseUrl": "http://localhost:8765/api",
  "description": "UnionAir exposes Unity Editor state and selected Editor operations as a local REST API.",
  "categories": [
    {
      "id": "read",
      "displayName": "Read",
      "source": "builtin",
      "enabled": true,
      "canDisable": false,
      "enabledByDefault": true,
      "risk": ["readOnly"]
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
      "summary": "Checks whether the server is running.",
      "risk": ["readOnly"],
      "playModePolicy": "allowed",
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
| `categories[].risk` | string[] | `readOnly`, `sceneUpdate`, `assetUpdate`, `playMode`, `custom`, `requestDependent`, or `editorState` |
| `endpoints[].source` | string | `builtin` or `custom` |
| `endpoints[].enabled` | bool | Whether the endpoint is currently enabled |
| `endpoints[].routeTemplate` | string | Route template used by the attribute router |
| `endpoints[].category` | string | Category used for discovery/UI grouping. Built-in constants include `read`, `sceneWrite`, `assetWrite`, `playMode`, `editorActions`, `testRunner`, and `custom`; custom endpoints may use any stable category string. |
| `endpoints[].risk` | string[] | Risk inherited from the endpoint category, unless the endpoint declares a more specific risk override |
| `endpoints[].playModePolicy` | string | `allowed`, `blocked`, or `explicitOptIn`. `blocked` endpoints return `409` in Play mode. `explicitOptIn` endpoints require both the Editor setting and `allowWhilePlaying=true` in Play mode. |
| `endpoints[].testRunPolicy` | string | `allowed` or `blocked`. Endpoints are blocked during a test run unless explicitly allowed. |
| `endpoints[].requiredQuery` | string[] | Required query string parameters |
| `endpoints[].optionalQuery` | string[] | Optional query string parameters |
| `endpoints[].requiredBody` | string[] | Required JSON body fields |
| `endpoints[].optionalBody` | string[] | Optional JSON body fields |

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `detail` | (compact) | `full` adds per-endpoint detail fields such as request/response examples to each endpoint item. |
| `category` | (all) | Filters categories and endpoints to a single category ID (case-insensitive), e.g. `read`, `sceneWrite`, `assetWrite`, `playMode`, `editorActions`, `testRunner`. |
| `includeDisabled` | `false` | When `true`, includes disabled custom categories/endpoints and endpoints with route conflicts. Built-in categories/endpoints are always listed with their current `enabled` state. |
| `source` | `all` | `builtin`, `custom`, or `all` |

> This endpoint is intentionally a lightweight discovery manifest, not a full OpenAPI schema. Use this document for detailed request and response examples. When adding or changing an endpoint, update its `[UnionAirEndpoint]` metadata so `/api/help`, routing, and the EditorWindow endpoint list stay in sync.

---

## Custom Controllers

Application-side Editor assemblies can add custom controllers under `/api/custom/...`. See [Custom Controllers](../custom-controllers.md) for controller setup, category metadata, request parsing, reference resolution helpers, Play Mode policy, and security guidance.

---

## GET /api/health

Checks whether the server is running.

### Response

```json
{
  "status": "ok",
  "unityVersion": "6000.3.5f2"
}
```

---

## Object References

Scene GameObjects and Components expose Unity `GlobalObjectId` strings in read responses. Write and detail APIs use typed object references for targets, sources, and parents.

Reference shape:

```json
{ "type": "hierarchyPath", "value": "Canvas/Button" }
```

Object references must be JSON objects. A bare string such as `"Canvas/Button"` is not accepted and returns `400 Bad Request`.

| Type | Value |
|------|-------|
| `hierarchyPath` | GameObject hierarchy path, such as `Canvas/Button`. This is the default when `type` is omitted |
| `componentPath` | Component path in `GameObjectPath:ComponentType` form, such as `Canvas/Button:UnityEngine.UI.Text` |
| `globalObjectId` | Unity GlobalObjectId string for a scene GameObject or Component |

`scenePath` remains a separate loaded scene selector and is used only for `hierarchyPath` and `componentPath` resolution. Scene asset responses use asset `guid` values, not `globalObjectId`.

Custom controllers can parse and resolve this same reference shape with `UnionAirReferenceResolver`; see [Custom Controllers](../custom-controllers.md).

---
