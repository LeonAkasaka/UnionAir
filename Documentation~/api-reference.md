# API Reference

Base URL: `http://localhost:<port>/api/` (default port: **8765**)

All responses are returned with `Content-Type: application/json; charset=utf-8` and include the CORS header (`Access-Control-Allow-Origin: *`).
String fields in JSON responses are escaped consistently, including control characters.
Non-finite floating-point values (`NaN`, `Infinity`, `-Infinity`) are emitted as `null` in JSON numeric fields.

---

## GET /api/help

Returns a compact API manifest for LLMs, MCP bridges, and other tools that cannot access this documentation directly. The endpoint list is generated from `[UnionAirController]` and `[UnionAirEndpoint]` route metadata.

### Response

```json
{
  "name": "com.leonakasaka.unionair",
  "displayName": "UnionAir - Unity REST Bridge",
  "version": "0.1.0",
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
| `endpoints[].category` | string | Category used for discovery/UI grouping. Built-in constants include `read`, `sceneWrite`, `assetWrite`, `playMode`, `editorActions`, and `custom`; custom endpoints may use any stable category string. |
| `endpoints[].risk` | string[] | Risk inherited from the endpoint category, unless the endpoint declares a more specific risk override |
| `endpoints[].playModePolicy` | string | `allowed`, `blocked`, or `explicitOptIn`. `blocked` endpoints return `409` in Play mode. `explicitOptIn` endpoints require both the Editor setting and `allowWhilePlaying=true` in Play mode. |
| `endpoints[].requiredQuery` | string[] | Required query string parameters |
| `endpoints[].optionalQuery` | string[] | Optional query string parameters |
| `endpoints[].requiredBody` | string[] | Required JSON body fields |
| `endpoints[].optionalBody` | string[] | Optional JSON body fields |

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `includeDisabled` | `false` | When `true`, includes disabled custom categories/endpoints and endpoints with route conflicts. Built-in categories/endpoints are always listed with their current `enabled` state. |
| `source` | `all` | `builtin`, `custom`, or `all` |

> This endpoint is intentionally a lightweight discovery manifest, not a full OpenAPI schema. Use this document for detailed request and response examples. When adding or changing an endpoint, update its `[UnionAirEndpoint]` metadata so `/api/help`, routing, and the EditorWindow endpoint list stay in sync.

---

## Custom Controllers

Application-side Editor assemblies can add custom controllers under `/api/custom/...`. See [Custom Controllers](custom-controllers.md) for controller setup, category metadata, request parsing, reference resolution helpers, Play Mode policy, and security guidance.

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

## GET /api/editor/status

Returns the execution status of the Unity Editor.

### Response

```json
{
  "isPlaying":   false,
  "isPaused":    false,
  "isCompiling": false,
  "isUpdating":  false,
  "unityVersion": "6000.3.5f2"
}
```

| Field | Type | Description |
|-----------|-----|------|
| `isPlaying` | bool | Whether Play mode is enabled (`EditorApplication.isPlaying`) |
| `isPaused` | bool | Whether playback is paused in Play mode (`EditorApplication.isPaused`) |
| `isCompiling` | bool | Whether scripts are being compiled (`EditorApplication.isCompiling`) |
| `isUpdating` | bool | Whether asset update processing is in progress (`EditorApplication.isUpdating`) |
| `unityVersion` | string | Unity version string |

---

## GET /api/editor/logs

Returns Unity Console logs. Includes logs recorded since the editor started (or since the last domain reload). Up to 1000 entries are kept in a ring buffer.

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `type` | `all` | `log` / `warning` / `error` / `exception` / `assert` / `all` |
| `search` | ―  | Case-insensitive partial-match filter on messages |
| `limit` | `100` | Maximum number of results to return (max: 1000) |

### Response

```json
{
  "count": 2,
  "logs": [
    {
      "type": "error",
      "message": "NullReferenceException: Object reference not set...",
      "stackTrace": "MyScript.Update () (at Assets/MyScript.cs:42)",
      "timestamp": "2026-05-16T04:12:00"
    },
    {
      "type": "warning",
      "message": "Shader 'Custom/Foo' has no shadows pass",
      "stackTrace": "",
      "timestamp": "2026-05-16T04:11:58"
    }
  ]
}
```

> Logs are returned in newest-first order (`timestamp` descending).
> Because `StopCapturing()` is called before a domain reload, logs are not retained across reloads.

### Examples

```bash
# Latest 20 errors and exceptions
curl "http://localhost:8765/api/editor/logs?type=error&limit=20"

# Logs containing "NullReference"
curl "http://localhost:8765/api/editor/logs?search=NullReference"
```

---

## GET /api/editor/selection

Returns the current Unity Editor selection.

> Can be called only when the Editor Actions category is enabled.
> The endpoint risk is `editorState`.

### Response

```json
{
  "count": 1,
  "activeIndex": 0,
  "active": {
    "kind": "sceneObject",
    "name": "Main Camera",
    "type": "UnityEngine.GameObject",
    "globalObjectId": "GlobalObjectId_V1-...",
    "scenePath": "Assets/Scenes/SampleScene.unity"
  },
  "objects": [
    {
      "kind": "sceneObject",
      "name": "Main Camera",
      "type": "UnityEngine.GameObject",
      "globalObjectId": "GlobalObjectId_V1-...",
      "scenePath": "Assets/Scenes/SampleScene.unity"
    }
  ],
  "assetGuids": []
}
```

| Field | Description |
|-------|-------------|
| `kind` | `sceneObject`, `asset`, or `unknown` |
| `globalObjectId` | Present for scene GameObjects and Components |
| `scenePath` | Loaded scene path for scene objects |
| `assetGuid` / `assetPath` | Present for project assets |
| `entityId` | Fallback for unsupported Editor object kinds when Unity exposes an Editor object entity ID |

---

## POST /api/editor/selection

Sets or clears the current Unity Editor selection.

> Can be called only when the Editor Actions category is enabled.
> The endpoint risk is `editorState`.

### Request Body (JSON)

Set one target:

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/Button" }
}
```

Set multiple targets:

```json
{
  "targets": [
    { "type": "hierarchyPath", "value": "Canvas/Button", "scenePath": "Assets/Scenes/Main.unity" },
    { "assetPath": "Assets/Textures/Icon.png" }
  ],
  "activeIndex": 0
}
```

Clear selection:

```json
{ "clear": true }
```

Targets accept either scene object reference fields (`type`, `value`, optional `scenePath`) or asset reference fields (`assetGuid`, `assetPath`, optional `assetType`). Do not mix scene and asset reference fields in one target object.

### Response

Same shape as `GET /api/editor/selection`.

### Errors

| Status | Cause |
|--------|-------|
| 400 | Missing target fields, malformed target, mixed scene and asset target fields, or invalid `activeIndex` |
| 403 | Editor Actions category is disabled |
| 404 | Scene object or asset was not found |
| 409 | Scene name is ambiguous |
| 422 | Target resolves to an unsupported object kind |

---

## POST /api/editor/ping

Highlights a Unity Editor object with `EditorGUIUtility.PingObject()` without changing the current selection.

> Can be called only when the Editor Actions category is enabled.
> The endpoint risk is `editorState`.

### Request Body (JSON)

```json
{
  "target": { "assetGuid": "a1b2c3..." }
}
```

`target` accepts the same single-target shape as `POST /api/editor/selection`.

### Response

```json
{
  "pinged": true,
  "target": {
    "kind": "asset",
    "name": "Icon",
    "type": "UnityEngine.Texture2D",
    "assetGuid": "a1b2c3...",
    "assetPath": "Assets/Textures/Icon.png"
  }
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `target` is missing or malformed |
| 403 | Editor Actions category is disabled |
| 404 | Target object or asset was not found |
| 409 | Scene name is ambiguous |
| 422 | Target resolves to an unsupported object kind |

---

## GET /api/cameras

Returns a list of all Camera components in the scene.

### Response

```json
{
  "count": 1,
  "cameras": [
    {
      "path": "Main Camera",
      "globalObjectId": "GlobalObjectId_V1-...",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
      "name": "Main Camera",
      "enabled": true,
      "depth": -1,
      "fieldOfView": 60.0,
      "isOrthographic": false,
      "tag": "MainCamera"
    }
  ]
}
```

| Field | Type | Description |
|-----------|-----|------|
| `path` | string | Hierarchical GameObject path (used for the `path` parameter of `/api/cameras/capture`) |
| `globalObjectId` | string | GlobalObjectId of the camera GameObject |
| `componentGlobalObjectId` | string | GlobalObjectId of the Camera component |
| `depth` | float | Render order (higher values are rendered later) |
| `fieldOfView` | float | Vertical field of view (has no meaning when `isOrthographic: true`) |

---

## GET /api/cameras/capture

Runs `camera.Render()` with the specified camera and returns the result as a base64-encoded image.
Works in both Edit mode and Play mode.

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `target` | **Required** | Object reference resolving to a camera GameObject or Camera component |
| `scenePath` | active scene | Loaded scene asset path or unambiguous scene name for path-based target resolution |
| `width` | `640` | Output width (px), max 1920 |
| `height` | `360` | Output height (px), max 1080 |
| `format` | `jpeg` | `png` or `jpeg` |
| `quality` | `85` | JPEG quality (1–100, valid when `format=jpeg`) |

### Response

```json
{
  "cameraPath": "Main Camera",
  "globalObjectId": "GlobalObjectId_V1-...",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "width": 640,
  "height": 360,
  "format": "jpeg",
  "mimeType": "image/jpeg",
  "image": "<base64-encoded image data>"
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` is missing or malformed |
| 404 | No Camera component exists for `target` |
| 422 | `target` resolves to an unsupported object type |

### Examples

```bash
# List cameras to find the path
curl "http://localhost:8765/api/cameras"

# Capture Main Camera at default resolution in JPEG
curl --get "http://localhost:8765/api/cameras/capture" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'

# Capture in PNG at HD resolution
curl --get "http://localhost:8765/api/cameras/capture" \
  --data-urlencode 'target={"type":"componentPath","value":"Main Camera:UnityEngine.Camera"}' \
  --data-urlencode "width=1280" \
  --data-urlencode "height=720" \
  --data-urlencode "format=png"
```

### Use with LLM / MCP Bridges

The response `mimeType` and `image` fields can be converted directly into an MCP image content block.

---

## GET /api/cameras/capture/image

With the same parameters as `/api/cameras/capture`, returns the binary image directly.
If opened in a browser, it displays as-is, and you can save it to a file with `curl -o`.

### Query Parameters

Same as `/api/cameras/capture` (`target` required; `scenePath` / `width` / `height` / `format` / `quality` optional).

### Response

`Content-Type: image/jpeg` (or `image/png`) binary stream. No JSON wrapper.

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` is missing or malformed |
| 404 | No Camera component exists for `target` |
| 422 | `target` resolves to an unsupported object type |

### Examples

```bash
# Open in browser to view directly
open "http://localhost:8765/api/cameras/capture/image?path=Main+Camera"

# Save to file with curl
curl -o screenshot.png \
  "http://localhost:8765/api/cameras/capture/image?path=Main+Camera&format=png"

# Save HD JPEG
curl -o hd.jpg \
  "http://localhost:8765/api/cameras/capture/image?path=Main+Camera&width=1280&height=720&quality=90"
```

---

## GET /api/scene

Returns metadata for a loaded scene. If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `scenePath` | active scene | Loaded scene asset path, such as `Assets/Scenes/Level_A.unity`. A scene name is accepted only when unambiguous. |

### Response

```json
{
  "name": "SampleScene",
  "path": "Assets/Scenes/SampleScene.unity",
  "guid": "a1b2c3...",
  "isDirty": false,
  "isLoaded": true,
  "rootCount": 4
}
```

| Field | Type | Description |
|-----------|-----|------|
| `name` | string | Scene name |
| `path` | string | Path under Assets/ |
| `guid` | string | Scene asset GUID |
| `isDirty` | bool | Whether there are unsaved changes |
| `isLoaded` | bool | Whether the scene is loaded |
| `rootCount` | int | Number of root GameObjects |

---

## Object References

Scene GameObjects and Components expose Unity `GlobalObjectId` strings in read responses. Write and detail APIs use typed object references for targets, sources, and parents.

Reference shape:

```json
{ "type": "hierarchyPath", "value": "Canvas/Button" }
```

| Type | Value |
|------|-------|
| `hierarchyPath` | GameObject hierarchy path, such as `Canvas/Button`. This is the default when `type` is omitted |
| `componentPath` | Component path in `GameObjectPath:ComponentType` form, such as `Canvas/Button:UnityEngine.UI.Text` |
| `globalObjectId` | Unity GlobalObjectId string for a scene GameObject or Component |

`scenePath` remains a separate loaded scene selector and is used only for `hierarchyPath` and `componentPath` resolution. Scene asset responses use asset `guid` values, not `globalObjectId`.

Custom controllers can parse and resolve this same reference shape with `UnionAirReferenceResolver`; see [Custom Controllers](custom-controllers.md).

---

## GET /api/scene/hierarchy

Returns the GameObject tree for the entire scene. If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `scenePath` | active scene | Loaded scene asset path, such as `Assets/Scenes/Level_A.unity`. A scene name is accepted only when unambiguous. |
| `depth` | unlimited | Maximum recursion depth |
| `compact` | `false` | When `true`, omits transform/tag/layer details and includes child counts |
| `limit` | `500` | Maximum number of GameObjects returned |
| `path` | scene roots | Optional subtree root path |

### Response

```json
{
  "scene": "SampleScene",
  "objects": [ <GameObjectNode>, ... ],
  "totalReturned": 42,
  "truncated": false
}
```

#### GameObjectNode

```json
{
  "name": "Canvas",
  "path": "Canvas",
  "isActive": true,
  "tag": "Untagged",
  "layer": 5,
  "transform": {
    "position": { "x": 0, "y": 0, "z": 0 },
    "rotation": { "x": 0, "y": 0, "z": 0 },
    "scale":    { "x": 1, "y": 1, "z": 1 }
  },
  "children": [
    {
      "name": "Panel",
      "path": "Canvas/Panel",
      ...
    }
  ]
}
```

#### Top-level response fields

| Field | Type | Description |
|-------------|------|------|
| `scene` | string | Name of the resolved scene |
| `totalReturned` | int | Number of GameObjects actually included in `objects` |
| `truncated` | bool | `true` when the result was cut off by the `limit` parameter |

#### GameObjectNode fields

| Field | Type | Description |
|-----------|-----|------|
| `name` | string | GameObject name |
| `path` | string | `/`-separated path from the root |
| `globalObjectId` | string | Stable Unity GlobalObjectId for the GameObject |
| `isActive` | bool | `activeInHierarchy` (including parents) |
| `tag` | string | Tag |
| `layer` | int | Layer number |
| `transform` | object | position / rotation (EulerAngles) / scale in local coordinate system |
| `children` | array | Array of child GameObjectNodes (recursive) |

---

## GET /api/scenes

Lists all loaded scenes and identifies the active scene.

### Response

```json
{
  "activeScene": "Assets/Scenes/Main.unity",
  "scenes": [
    {
      "name": "Main",
      "path": "Assets/Scenes/Main.unity",
      "guid": "a1b2c3...",
      "buildIndex": 0,
      "isDirty": false,
      "isLoaded": true,
      "isActive": true,
      "rootCount": 4
    }
  ],
  "count": 1
}
```

---

## POST /api/scenes/new

Creates a new scene.

> Can be called only when the Scene Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "mode": "single",
  "setup": "default",
  "discardUnsaved": false
}
```

| Field | Required | Description |
|-----------|------|------|
| `mode` | ❌ | `single` or `additive`. Defaults to `single` |
| `setup` | ❌ | `default` or `empty`. Defaults to `default` |
| `discardUnsaved` | ❌ | Required as `true` for `single` mode when any loaded scene is dirty |

### Response

```json
{
  "created": {
    "name": "Untitled",
    "path": "",
    "buildIndex": -1,
    "isDirty": false,
    "isLoaded": true,
    "isActive": true,
    "rootCount": 2
  }
}
```

---

## POST /api/scenes/open

Opens an existing scene asset.

> Can be called only when the Scene Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "path": "Assets/Scenes/Level_A.unity",
  "mode": "additive",
  "discardUnsaved": false
}
```

| Field | Required | Description |
|-----------|------|------|
| `path` | ✅ | Scene asset path |
| `mode` | ❌ | `single` or `additive`. Defaults to `single` |
| `discardUnsaved` | ❌ | Required as `true` for `single` mode when any loaded scene is dirty |

---

## POST /api/scenes/unload

Unloads a loaded scene.

> Can be called only when the Scene Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "path": "Assets/Scenes/Level_A.unity",
  "discardUnsaved": false
}
```

| Field | Required | Description |
|-----------|------|------|
| `path` | Conditional | Loaded scene asset path. Required when `name` is omitted |
| `name` | Conditional | Loaded scene name. Accepted only when unambiguous |
| `discardUnsaved` | ❌ | Required as `true` when the target scene is dirty |

---

## POST /api/scenes/active

Sets the active scene.

> Can be called only when the Scene Write category is enabled.
> In Play mode, this endpoint requires the Editor-side **Allow Play Mode Scene Changes** setting and `allowWhilePlaying=true` in the body or query string.

### Request Body (JSON)

```json
{
  "path": "Assets/Scenes/Level_A.unity"
}
```

| Field | Required | Description |
|-----------|------|------|
| `path` | Conditional | Loaded scene asset path. Required when `name` is omitted |
| `name` | Conditional | Loaded scene name. Accepted only when unambiguous |
| `allowWhilePlaying` | ❌ | Required as `true` when calling this endpoint in Play mode, after the Editor-side setting is enabled |

---

## GET /api/gameobjects

Returns detailed information for the specified GameObject (including components).
If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `target` | ✅ | Object reference. Must resolve to a GameObject |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "name": "Button",
  "path": "Canvas/Panel/Button",
  "globalObjectId": "GlobalObjectId_V1-...",
  "isActive": true,
  "tag": "Untagged",
  "layer": 5,
  "transform": { ... },
  "components": [
    {
      "type": "UnityEngine.RectTransform",
      "globalObjectId": "GlobalObjectId_V1-...",
      "properties": {
        "m_LocalPosition": { "x": 0, "y": 0, "z": 0 },
        "m_LocalScale":    { "x": 1, "y": 1, "z": 1 }
      }
    },
    {
      "type": "UnityEngine.UI.Button",
      "properties": {
        "m_Interactable": true,
        "m_Transition": 1
      }
    }
  ]
}
```

`components[].properties` are properties obtained via `SerializedObject`.
Supported `SerializedPropertyType` values: `bool`, `int`, `float`, `string`, `Color`, `Vector2/3/4`, `Rect`, `ObjectReference`. Arrays are serialized as JSON arrays whose elements follow the same type rules. Other types are `null`.

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` is missing or malformed |
| 404 | No GameObject exists for `target` |
| 422 | `target` does not resolve to a GameObject |

---

## GET /api/assets

Returns a list of assets in the project.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `path` | ❌ | Search target folder (example: `Assets/UI`). If omitted, the entire `Assets/` tree |
| `type` | ❌ | Asset type name (example: `Texture2D`, `Material`, `Scene`) |
| `search` | ❌ | Additional filter string passed to `AssetDatabase.FindAssets` |

### Response

```json
{
  "assets": [
    {
      "guid": "a1b2c3d4e5f6...",
      "path": "Assets/UI/logo.png",
      "type": "Texture2D"
    }
  ],
  "total": 42,
  "returned": 42
}
```

> Returns up to **500 items**. If `total` exceeds 500, narrow the filters.

---

## GET /api/assets/{guid}

Returns detailed information about an asset specified by GUID.

### Path Parameters

| Parameter | Description |
|-------------|------|
| `guid` | GUID string for `AssetDatabase` |

### Response

```json
{
  "guid": "a1b2c3d4e5f6...",
  "path": "Assets/UI/logo.png",
  "type": "UnityEngine.Texture2D",
  "dependencies": [
    "Assets/UI/logo.png"
  ],
  "labels": ["UI", "Icon"]
}
```

| Field | Type | Description |
|-----------|-----|------|
| `guid` | string | Asset GUID |
| `path` | string | Path under Assets/ |
| `type` | string | Fully qualified type name |
| `dependencies` | string[] | Paths of directly dependent assets (`GetDependencies(recursive: false)`) |
| `labels` | string[] | Asset labels |

### Errors

| Status | Cause |
|-----------|------|
| 400 | GUID is empty |
| 404 | No matching asset exists |

---

## GET /api/search/gameobjects

Searches GameObjects in the scene using multiple AND conditions. All parameters are optional. If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Type | Description |
|-------------|-----|------|
| `scenePath` | string | Loaded scene asset path or unambiguous scene name |
| `name` | string | Case-insensitive partial match on the name |
| `component` | string | Partial match on the component type name (example: `Camera`, `MeshRenderer`) |
| `tag` | string | Exact match on the tag |
| `layer` | int | Layer number |
| `active` | bool | `true`/`false` (omitted = both) |
| `assetGuid` | string | References the asset with the specified GUID from any component |
| `includeComponents` | bool | When `true`, includes the list of component type names for each GameObject (default: `false`) |

### Response

```json
{
  "count": 2,
  "gameObjects": [
    {
      "name": "Main Camera",
      "path": "Main Camera",
      "globalObjectId": "GlobalObjectId_V1-...",
      "isActive": true,
      "tag": "MainCamera",
      "layer": 0,
      "transform": { "position": {...}, "rotation": {...}, "scale": {...} },
      "components": [
        { "type": "UnityEngine.Camera" },
        { "type": "UnityEngine.AudioListener" }
      ]
    }
  ]
}
```

> The `components` field is included only when `includeComponents=true` is specified.

### Examples

```bash
# GameObjects whose name contains "Enemy"
curl "http://localhost:8765/api/search/gameobjects?name=Enemy"

# GameObjects with Camera component (include component list)
curl "http://localhost:8765/api/search/gameobjects?component=Camera&includeComponents=true"

# References a specific asset + inactive only
curl "http://localhost:8765/api/search/gameobjects?assetGuid=abc123&active=false"
```

---

## GET /api/search/asset-refs

Lists places where components in the scene reference a specific asset. If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `guid` | ✅ | GUID of the asset to search for |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "asset": {
    "guid": "a1b2c3...",
    "path": "Assets/Materials/PlayerMat.mat",
    "type": "Material"
  },
  "references": [
    {
      "gameObjectPath": "Player/Body",
      "gameObjectGlobalObjectId": "GlobalObjectId_V1-...",
      "componentType": "UnityEngine.MeshRenderer",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
      "propertyName": "m_Materials"
    }
  ],
  "count": 1
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `guid` is missing |
| 404 | No matching asset exists |

> **Note**: Scans all components on all GameObjects in the scene using `SerializedObject`. Processing may take time in large scenes.

---

## GET /api/assets/dependents

Returns assets that depend on the specified asset (reverse dependencies).

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `guid` | ✅ | GUID of the depended-on asset |

### Response

```json
{
  "asset": {
    "guid": "a1b2c3...",
    "path": "Assets/Materials/PlayerMat.mat",
    "type": "Material"
  },
  "dependents": [
    {
      "guid": "d4e5f6...",
      "path": "Assets/Prefabs/Player.prefab",
      "type": "GameObject"
    }
  ],
  "count": 1
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `guid` is missing |
| 404 | No matching asset exists |

> **Note**: Calls `GetDependencies()` for all assets under `Assets/`. If there are many assets, processing may take time.

---

## GET /api/scene/stats

Returns aggregate statistics for a loaded scene. If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `scenePath` | active scene | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "scene": "SampleScene",
  "totalGameObjects": 42,
  "activeGameObjects": 38,
  "inactiveGameObjects": 4,
  "componentCounts": {
    "Camera": 1,
    "MeshRenderer": 15,
    "Light": 3,
    "Rigidbody": 8
  },
  "tagCounts": {
    "Untagged": 30,
    "Player": 1,
    "Enemy": 8
  },
  "layerCounts": {
    "Default": 35,
    "UI": 7
  }
}
```

> `Transform` / `RectTransform` are excluded from `componentCounts` because they would be noise.
> The keys in `layerCounts` are layer names (or numeric IDs for unnamed layers).

---

## Scene Write category — Common Notes

> **Security:** Write endpoints are **disabled** by default.
> Enable them using the toggles under **Window > UnionAir > REST Bridge**.
> Edit mode write operations can be undone with Unity Editor Undo (Ctrl+Z).
> GameObject and Component write endpoints return `409 Conflict` in Play mode unless **Allow Play Mode Scene Changes** is enabled in the EditorWindow and the request includes `allowWhilePlaying=true` in the JSON body or query string. For `POST` and `PATCH`, a body value takes precedence over the query string.
> Play mode scene-object writes are transient runtime changes and do not mark scenes dirty or register Undo operations.

---

## POST /api/gameobjects

Creates a new empty GameObject in the scene.
If `scenePath` is omitted, the active scene is used.

### Request Body (JSON)

```json
{
  "name": "MyObject",
  "parent": { "type": "hierarchyPath", "value": "Canvas" },
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| Field | Required | Description |
|-----------|------|------|
| `name` | ✅ | Name of the GameObject to create |
| `parent` | ❌ | Object reference resolving to a parent GameObject. If omitted, the object is placed at the scene root |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "path": "Canvas/MyObject",
  "name": "MyObject",
  "globalObjectId": "GlobalObjectId_V1-..."
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `name` is missing |
| 404 | `parent` does not exist |
| 422 | `parent` does not resolve to a GameObject |
| 403 | Scene Write category is disabled |

---

## POST /api/gameobjects/primitive

Creates a primitive-type GameObject.
If `scenePath` is omitted, the active scene is used.

### Request Body (JSON)

```json
{
  "type": "Cube",
  "name": "MyCube",
  "parent": { "type": "hierarchyPath", "value": "Stage" },
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| Field | Required | Description |
|-----------|------|------|
| `type` | ✅ | `Cube` \| `Sphere` \| `Capsule` \| `Cylinder` \| `Plane` \| `Quad` |
| `name` | ❌ | If omitted, the type name is used as-is |
| `parent` | ❌ | Object reference resolving to a parent GameObject. If omitted, the object is placed at the scene root |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "path": "Stage/MyCube",
  "name": "MyCube",
  "globalObjectId": "GlobalObjectId_V1-..."
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `type` is missing or invalid |
| 404 | `parent` does not exist |
| 422 | `parent` does not resolve to a GameObject |
| 403 | Scene Write category is disabled |

---

## POST /api/gameobjects/instantiate

Instantiates a prefab asset into the active scene while preserving the prefab connection.
If `scenePath` is omitted, the active scene is used.

### Request Body (JSON)

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Prefabs/Player.prefab",
  "name": "PlayerInstance",
  "parent": { "type": "hierarchyPath", "value": "Stage" },
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| Field | Required | Description |
|-----------|------|------|
| `guid` | Conditional | GUID of the prefab asset. Takes precedence over `assetPath` when both are provided |
| `assetPath` | Conditional | Prefab asset path. Required when `guid` is omitted |
| `name` | ❌ | Optional name for the created instance |
| `parent` | ❌ | Object reference resolving to a parent GameObject. If omitted, the scene root |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "name": "PlayerInstance",
  "path": "Stage/PlayerInstance",
  "globalObjectId": "GlobalObjectId_V1-...",
  "prefabAssetPath": "Assets/Prefabs/Player.prefab",
  "components": ["Transform", "MeshRenderer"]
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `guid` and `assetPath` are both missing, or the asset is not a prefab/GameObject |
| 404 | `guid` or `parent` does not exist |
| 422 | `parent` does not resolve to a GameObject |
| 403 | Scene Write category is disabled |

---

## DELETE /api/gameobjects

Deletes the specified GameObject from the scene.
If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `target` | ✅ | Object reference resolving to a GameObject |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{ "deleted": "Canvas/MyObject", "globalObjectId": "GlobalObjectId_V1-..." }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` is missing or malformed |
| 404 | `target` does not exist |
| 422 | `target` does not resolve to a GameObject |
| 403 | Scene Write category is disabled |

---

## PATCH /api/gameobjects

Updates the properties of the specified GameObject.
If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `target` | ✅ | Object reference resolving to a GameObject |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Request Body (JSON)

```json
{
  "name": "RenamedObject",
  "isActive": false,
  "tag": "Player",
  "layer": 3,
  "transform": {
    "position": { "x": 1, "y": 0, "z": 0 },
    "rotation": { "x": 0, "y": 45, "z": 0 },
    "scale":    { "x": 1, "y": 1,  "z": 1 }
  }
}
```

All fields are optional. Omitted fields are not changed. Each subfield of `transform` is likewise optional.

### Response

```json
{ "path": "Canvas/RenamedObject", "name": "RenamedObject", "globalObjectId": "GlobalObjectId_V1-..." }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` is missing or malformed |
| 404 | `target` does not exist |
| 422 | `target` does not resolve to a GameObject |
| 403 | Scene Write category is disabled |

---

## POST /api/gameobjects/duplicate

Duplicates the specified GameObject.
If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `source` | ✅ | Object reference resolving to a source GameObject |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{ "path": "Canvas/MyObject (1)", "name": "MyObject (1)", "globalObjectId": "GlobalObjectId_V1-..." }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `source` is missing or malformed |
| 404 | `source` does not exist |
| 422 | `source` does not resolve to a GameObject |
| 403 | Scene Write category is disabled |

---

## POST /api/gameobjects/reparent

Moves a GameObject to a different parent.
If `scenePath` is omitted, the active scene is used.

### Request Body (JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/Panel/Button" },
  "parent": { "type": "hierarchyPath", "value": "Canvas/NewPanel" },
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| Field | Required | Description |
|-----------|------|------|
| `target` | ✅ | Object reference resolving to the GameObject to move |
| `parent` | ❌ | Object reference resolving to the new parent. If omitted, moves to the scene root |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{ "path": "Canvas/NewPanel/Button", "globalObjectId": "GlobalObjectId_V1-..." }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` or `parent` is malformed |
| 404 | `target` or `parent` does not exist |
| 422 | `target` or `parent` does not resolve to a GameObject |
| 403 | Scene Write category is disabled |

---

## POST /api/gameobjects/batch

Executes multiple create / update / delete operations together as **a single Undo group**.
If `scenePath` is omitted, the active scene is used. A top-level `scenePath` applies to all operations, and each operation may override it with its own `scenePath`.

### Request Body (JSON)

```json
{
  "operations": [
    { "op": "create", "name": "EmptyGO", "parent": { "value": "Stage" } },
    { "op": "create_primitive", "type": "Cube", "name": "Cube_0", "transform": { "position": { "x": 0, "y": 0, "z": 0 } } },
    { "op": "update", "target": { "value": "Stage/OldObject" }, "isActive": false },
    { "op": "delete", "target": { "value": "Stage/Trash" } }
  ]
}
```

#### `op` Types

| `op` | Required Fields | Optional Fields |
|------|--------------|------------------|
| `create` | `name` | `parent`, `transform` |
| `create_primitive` | `type` (`Cube`\|`Sphere`\|`Capsule`\|`Cylinder`\|`Plane`\|`Quad`) | `name`, `parent`, `transform` |
| `update` | `target` | `name`, `isActive`, `tag`, `layer`, `transform` |
| `delete` | `target` | — |

All operation types also accept optional `scenePath`.

`transform` shape: `{"position":{"x":0,"y":0,"z":0},"rotation":{...},"scale":{...}}`

### Response (HTTP 207)

```json
{
  "processed": 4,
  "failed": 1,
  "results": [
    { "index": 0, "success": true,  "path": "Stage/EmptyGO", "globalObjectId": "GlobalObjectId_V1-..." },
    { "index": 1, "success": true,  "path": "Cube_0", "globalObjectId": "GlobalObjectId_V1-..." },
    { "index": 2, "success": true,  "path": "Stage/OldObject", "globalObjectId": "GlobalObjectId_V1-..." },
    { "index": 3, "success": false, "error": "GameObject not found: Stage/Trash" }
  ]
}
```

Even if one operation fails, the remaining operations continue. All successful operations are grouped into a single Undo group.

### Errors

| Status | Cause |
|-----------|------|
| 400 | `operations` is missing or empty |
| 403 | Scene Write category is disabled |

---

## POST /api/gameobjects/components

Adds a component to the specified GameObject.
If `scenePath` is omitted, the active scene is used.

### Request Body (JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/MyObject" },
  "type": "UnityEngine.BoxCollider",
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| Field | Required | Description |
|-----------|------|------|
| `target` | ✅ | Object reference resolving to a GameObject |
| `type` | ✅ | Fully qualified type name of the component to add |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "path": "Canvas/MyObject",
  "globalObjectId": "GlobalObjectId_V1-...",
  "type": "UnityEngine.BoxCollider",
  "componentGlobalObjectId": "GlobalObjectId_V1-..."
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` is missing or malformed, or `type` is missing |
| 404 | `target` does not exist |
| 422 | `target` does not resolve to a GameObject, the component type cannot be resolved, or adding the component failed |
| 403 | Scene Write category is disabled |

---

## DELETE /api/gameobjects/components

Removes the specified component.
If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `target` | ✅ | Object reference resolving to a Component. Use `componentPath` (e.g. `{"type":"componentPath","value":"Canvas/Button:Rigidbody"}`) or a Component `globalObjectId`. Alternatively, target a GameObject with `hierarchyPath` and add `?type=ComponentName` |
| `type` | ❌ | C# type name of the component to remove. Required when `target` is a GameObject reference (e.g. `hierarchyPath`) |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "deleted": "UnityEngine.BoxCollider",
  "from": "Canvas/MyObject",
  "globalObjectId": "GlobalObjectId_V1-...",
  "componentGlobalObjectId": "GlobalObjectId_V1-..."
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` is missing, malformed, or `type` is an unknown component name |
| 404 | `target` does not exist, or no component of the given `type` is on the target |
| 422 | `target` resolves to a GameObject but `type` was not provided |
| 403 | Scene Write category is disabled |

---

## PATCH /api/gameobjects/components

Updates serialized properties of the specified component, including object reference fields.
If `scenePath` is omitted, the active scene is used.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `target` | ✅ | Object reference resolving to a Component. Use `componentPath` (e.g. `{"type":"componentPath","value":"Obj:MeshRenderer"}`) or a Component `globalObjectId`. Alternatively, target a GameObject with `hierarchyPath` and add `?type=ComponentName` |
| `type` | ❌ | C# type name of the component to update. Required when `target` is a GameObject reference (e.g. `hierarchyPath`) |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Request Body (JSON)

```json
{
  "properties": {
    "m_Intensity": 2.0,
    "m_Color": { "r": 1, "g": 0.9, "b": 0.8, "a": 1 },
    "target": { "type": "componentPath", "value": "Canvas/Button:UnityEngine.UI.Text", "scenePath": "Assets/Scenes/Level_A.unity" },
    "textAsset": { "assetPath": "Assets/Data/config.txt", "assetType": "UnityEngine.TextAsset" },
    "optionalTarget": null
  }
}
```

Each key in `properties` is a `SerializedProperty.propertyPath`. Top-level field names are still accepted for compatibility.

Supported object reference values:

| Shape | Description |
|------|-------------|
| `null` | Clears the reference |
| `{ "type": "globalObjectId", "value": "GlobalObjectId_V1-..." }` | Assigns a scene GameObject or Component by GlobalObjectId |
| `{ "type": "hierarchyPath", "value": "Canvas/Button" }` | Assigns a scene GameObject |
| `{ "type": "componentPath", "value": "Canvas/Button:UnityEngine.UI.Text" }` | Assigns a component on a scene GameObject |
| `{ "type": "hierarchyPath", "value": "Canvas/Button", "scenePath": "Assets/Scenes/Level_A.unity" }` | Assigns a GameObject from a loaded scene |
| `{ "assetGuid": "...", "assetType": "UnityEngine.TextAsset" }` | Assigns an asset by GUID |
| `{ "assetPath": "Assets/Data/config.txt", "assetType": "UnityEngine.TextAsset" }` | Assigns an asset by path |

`assetType` is optional for asset references. When provided, it must resolve to a `UnityEngine.Object` type and the resolved object must be assignable to both that type and the serialized field type.

### Response

```json
{
  "path": "Directional Light",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.Light",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "updated": ["m_Intensity", "m_Color"]
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` is missing or malformed, `properties` is missing, or `type` is an unknown component name |
| 400 | An object reference payload is malformed, or a requested type cannot be resolved |
| 404 | The GameObject, component, or asset does not exist |
| 422 | `target` resolves to a GameObject but `type` was not provided; or the resolved object is not assignable to the requested type or field type |
| 403 | Scene Write category is disabled |

---

## POST /api/scene/save

Saves the current scene to disk.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Response

```json
{ "saved": true, "path": "Assets/Scenes/SampleScene.unity" }
```

---

## POST /api/editor/refresh

Calls `AssetDatabase.Refresh()` so Unity recognizes changes to scripts and assets.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Response

```json
{
  "refreshed": true,
  "isCompiling": true,
  "isUpdating": false,
  "isPlaying": false
}
```

> Before attaching a new script component, poll `GET /api/editor/status` and wait until `isCompiling: false`.

---

## GET /api/editor/menu-items

Lists currently discoverable Unity Editor menu item paths that can be used with `POST /api/editor/menu-item`.

> Can be called only when the Editor Actions category is enabled.
> The endpoint risk is `editorState`.
> Unity does not expose a stable public API for complete menu enumeration. This endpoint reports whether it used Unity's internal menu API or a fallback scan of `[MenuItem]` attributes.

### Query Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `root` | ― | Optional menu root such as `Window`, `Assets`, or `GameObject` |
| `search` | ― | Case-insensitive partial match on menu item path |
| `includeFolders` | `true` | Includes menu folder entries when internal menu enumeration is available |
| `includeAttributeFallback` | `true` | Adds `[MenuItem]` attribute paths to internal menu enumeration results |
| `limit` | `1000` | Maximum number of returned items, clamped to 1-5000 |

### Response

```json
{
  "enumerationMode": "unsupportedApi",
  "isComplete": true,
  "root": "Window",
  "count": 1,
  "items": [
    {
      "path": "Window/UnionAir/REST Bridge",
      "name": "REST Bridge",
      "parent": "Window/UnionAir",
      "depth": 2,
      "isFolder": false,
      "source": "unityMenu"
    }
  ],
  "warnings": []
}
```

When Unity's internal menu enumeration method is unavailable, the endpoint falls back to scanning loaded assemblies for `[MenuItem]` attributes:

```json
{
  "enumerationMode": "menuItemAttributes",
  "isComplete": false,
  "root": "",
  "count": 1,
  "items": [
    {
      "path": "Window/UnionAir/REST Bridge",
      "name": "REST Bridge",
      "parent": "Window/UnionAir",
      "depth": 2,
      "isFolder": false,
      "source": "menuItemAttribute"
    }
  ],
  "warnings": [
    "UnityEditor.Unsupported.GetSubmenus was not available; built-in Unity menu items may be incomplete."
  ]
}
```

| Field | Description |
|-------|-------------|
| `enumerationMode` | `unsupportedApi` when Unity internal menu enumeration was used, otherwise `menuItemAttributes` |
| `isComplete` | Whether the endpoint expects built-in Unity menu coverage to be complete |
| `items[].path` | Menu path intended for `POST /api/editor/menu-item` |
| `items[].isFolder` | Whether the item is a menu folder rather than an executable item |
| `items[].source` | `unityMenu` or `menuItemAttribute` |

### Examples

```bash
curl "http://localhost:8765/api/editor/menu-items?search=UnionAir"
curl "http://localhost:8765/api/editor/menu-items?root=Window&includeFolders=false"
```

---

## POST /api/editor/menu-item

Executes a Unity Editor menu item using `EditorApplication.ExecuteMenuItem()`.

> Can be called only when the Editor Actions category is enabled.
> Returns `409 Conflict` in Play mode.
> The risk is reported as `requestDependent` because the side effects depend on the requested menu item path.

### Request Body (JSON)

```json
{
  "path": "Window/UnionAir/REST Bridge"
}
```

| Field | Required | Description |
|-----------|------|------|
| `path` | ✅ | Unity Editor menu item path, such as `Window/UnionAir/REST Bridge` |

### Response

```json
{
  "executed": true,
  "path": "Window/UnionAir/REST Bridge"
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `path` is missing or empty |
| 403 | Editor Actions category is disabled |
| 404 | The menu item was not found, is disabled, or could not be executed |
| 409 | The Unity Editor is in Play mode |

### Examples

```bash
curl -X POST http://localhost:8765/api/editor/menu-item \
  -H "Content-Type: application/json" \
  -d '{"path":"Window/UnionAir/REST Bridge"}'
```

---

## POST /api/assets/prefabs

Creates a prefab from a GameObject in the scene.
If `scenePath` is omitted, the active scene is used.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "source": { "type": "hierarchyPath", "value": "Stage/Player" },
  "assetPath": "Assets/Prefabs/Player.prefab",
  "mode": "new",
  "scenePath": "Assets/Scenes/Level_A.unity"
}
```

| Field | Required | Description |
|-----------|------|------|
| `source` | ✅ | Object reference resolving to the source GameObject |
| `assetPath` | ✅ | Destination asset path (a `.prefab` file starting with `Assets/`) |
| `mode` | ✅ | `new` (create while connecting the instance) or `replace` (overwrite an existing prefab) |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "assetPath": "Assets/Prefabs/Player.prefab",
  "guid": "a1b2c3...",
  "sourceGlobalObjectId": "GlobalObjectId_V1-..."
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | Required fields are missing, or `mode` is invalid |
| 404 | `source` does not exist |
| 422 | `source` does not resolve to a GameObject |
| 403 | Asset Write category is disabled |

---

## POST /api/assets/prefabs/apply

Applies prefab instance overrides to the prefab asset.
If `scenePath` is omitted, the active scene is used.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{ "source": { "type": "hierarchyPath", "value": "Stage/Player" }, "scenePath": "Assets/Scenes/Level_A.unity" }
```

| Field | Required | Description |
|-----------|------|------|
| `source` | ✅ | Object reference resolving to the prefab instance |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "applied": "Stage/Player",
  "globalObjectId": "GlobalObjectId_V1-...",
  "prefabAssetPath": "Assets/Prefabs/Player.prefab"
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `source` is missing or malformed, or the object is not a prefab instance |
| 404 | `source` does not exist |
| 422 | `source` does not resolve to a GameObject |
| 403 | Asset Write category is disabled |

---

## POST /api/assets/prefabs/revert

Reverts a prefab instance to the state of the prefab asset.
If `scenePath` is omitted, the active scene is used.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{ "source": { "type": "hierarchyPath", "value": "Stage/Player" }, "scenePath": "Assets/Scenes/Level_A.unity" }
```

| Field | Required | Description |
|-----------|------|------|
| `source` | ✅ | Object reference resolving to the prefab instance |
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name |

### Response

```json
{
  "reverted": "Stage/Player",
  "globalObjectId": "GlobalObjectId_V1-...",
  "prefabAssetPath": "Assets/Prefabs/Player.prefab"
}
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `source` is missing or malformed, or the object is not a prefab instance |
| 404 | `source` does not exist |
| 422 | `source` does not resolve to a GameObject |
| 403 | Asset Write category is disabled |

---

## POST /api/assets/materials

Creates a new material asset.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "assetPath": "Assets/Materials/MyMat.mat",
  "shader": "Universal Render Pipeline/Lit"
}
```

| Field | Required | Description |
|-----------|------|------|
| `assetPath` | ✅ | Destination (`Assets/`-prefixed `.mat` file) |
| `shader` | ✅ | Shader name |

### Response

```json
{ "guid": "d4e5f6...", "assetPath": "Assets/Materials/MyMat.mat" }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | Required fields are missing |
| 422 | Shader not found |
| 403 | Asset Write category is disabled |

---

## PATCH /api/assets/materials

Updates material properties.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `guid` | ✅ | GUID of the target material |

### Request Body (JSON)

```json
{
  "properties": {
    "_BaseColor": { "r": 1, "g": 0, "b": 0, "a": 1 },
    "_Metallic": 0.5,
    "_BumpMap": "b1c2d3..."
  }
}
```

Types of values in `properties`:

| Type | Format |
|----|------|
| Color | `{"r":float,"g":float,"b":float,"a":float}` |
| Float | `float` |
| Vector | `{"x":float,"y":float,"z":float,"w":float}` |
| Texture | GUID string |

### Response

```json
{ "guid": "d4e5f6...", "updated": ["_BaseColor", "_Metallic"] }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `guid` is missing |
| 404 | No matching material exists |
| 403 | Asset Write category is disabled |

---

## DELETE /api/assets/{guid}

Deletes the asset and its `.meta` file.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-------------|------|
| `guid` | GUID of the asset to delete |

### Response

```json
{ "deleted": "Assets/Textures/old_icon.png" }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | GUID is empty |
| 404 | No matching asset exists |
| 403 | Asset Write category is disabled |

---

## POST /api/assets/move

Moves/renames an asset. Its GUID and references within the project are preserved.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "guid": "a1b2c3...",
  "newPath": "Assets/Textures/Renamed/icon.png"
}
```

| Field | Required | Description |
|-----------|------|------|
| `guid` | ✅ | GUID of the asset to move |
| `newPath` | ✅ | Destination path (starts with `Assets/`) |

### Response

```json
{ "guid": "a1b2c3...", "newPath": "Assets/Textures/Renamed/icon.png" }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `guid` or `newPath` is missing |
| 404 | No matching asset exists |
| 422 | Move operation failed (duplicate path, etc.) |
| 403 | Asset Write category is disabled |

---

## POST /api/assets/open

Opens an asset in the Unity Editor using `AssetDatabase.OpenAsset()`.

> Can be called only when the Editor Actions category is enabled.
> Returns `409 Conflict` in Play mode.
> The endpoint risk is `editorState`.

### Request Body (JSON)

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Scripts/Foo.cs"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `guid` | Conditional | GUID of the asset to open. Takes precedence when both fields are provided |
| `assetPath` | Conditional | Project asset path. Required when `guid` is omitted |

### Response

```json
{
  "opened": true,
  "guid": "a1b2c3...",
  "assetPath": "Assets/Scripts/Foo.cs"
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `guid` and `assetPath` are both missing |
| 403 | Editor Actions category is disabled |
| 404 | No matching asset exists |
| 409 | The Unity Editor is in Play mode |
| 422 | The asset could not be opened |

---

## POST /api/assets/reimport

Reimports one project asset using `AssetDatabase.ImportAsset()`.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "guid": "a1b2c3...",
  "recursive": false,
  "forceUpdate": false
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `guid` | Conditional | GUID of the asset to reimport. Takes precedence when both fields are provided |
| `assetPath` | Conditional | Project asset path. Required when `guid` is omitted |
| `recursive` | ❌ | Adds `ImportAssetOptions.ImportRecursive` |
| `forceUpdate` | ❌ | Adds `ImportAssetOptions.ForceUpdate` |

### Response

```json
{
  "reimported": true,
  "guid": "a1b2c3...",
  "assetPath": "Assets/Textures/Icon.png",
  "isCompiling": false,
  "isUpdating": true
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `guid` and `assetPath` are both missing |
| 403 | Asset Write category is disabled |
| 404 | No matching asset exists |
| 409 | The Unity Editor is in Play mode |

---

## GET /api/assets/scriptableobjects

Lists ScriptableObject assets in the project.

> Requires the Read category (enabled by default).

### Query Parameters

| Parameter | Required | Description |
|-----------|----------|-------------|
| `type` | ❌ | Filter by type name (e.g. `EnemyConfig`). Defaults to `ScriptableObject` (all SO assets) |
| `path` | ❌ | Restrict search to this folder (e.g. `Assets/Data`) |
| `search` | ❌ | Additional keyword passed to `AssetDatabase.FindAssets` |

### Response

```json
{
  "assets": [
    { "guid": "a1b2c3...", "path": "Assets/Data/EnemyConfig.asset", "type": "MyGame.EnemyConfig" }
  ],
  "total": 1,
  "returned": 1
}
```

Maximum 500 assets are returned per request.

---

## GET /api/assets/scriptableobjects/{guid}

Returns a ScriptableObject asset together with all readable serialized properties.

> Requires the Read category (enabled by default).

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the ScriptableObject asset |

### Response

```json
{
  "guid": "a1b2c3...",
  "path": "Assets/Data/EnemyConfig.asset",
  "type": "MyGame.EnemyConfig",
  "properties": {
    "health": 100,
    "speed": 3.5,
    "displayName": "Goblin",
    "primaryWeapon": { "assetGuid": "def456...", "assetPath": "Assets/Weapons/Sword.asset", "assetType": "MyGame.WeaponData" },
    "tags": null
  }
}
```

**Property serialization rules:**

| SerializedPropertyType | JSON representation |
|---|---|
| Boolean | `true` / `false` |
| Integer, Enum, LayerMask | Integer literal |
| Float | Float literal (round-trip format) |
| String | JSON string |
| Color | `{"r":…,"g":…,"b":…,"a":…}` |
| Vector2 | `{"x":…,"y":…}` |
| Vector3 | `{"x":…,"y":…,"z":…}` |
| Vector4, Quaternion | `{"x":…,"y":…,"z":…,"w":…}` |
| Rect | `{"x":…,"y":…,"width":…,"height":…}` |
| Bounds | `{"center":{"x":…,"y":…,"z":…},"extents":{"x":…,"y":…,"z":…}}` |
| ObjectReference (asset) | `{"assetGuid":…,"assetPath":…,"assetType":…}` |
| ObjectReference (null) | `null` |
| Arrays, nested generic types | `null` (ScriptableObject GET does not serialize arrays; use GET /api/gameobjects for array-valued component properties) |

### Errors

| Status | Cause |
|--------|-------|
| 400 | GUID is empty, or the asset is not a ScriptableObject |
| 404 | No asset found for the given GUID |

---

## POST /api/assets/scriptableobjects

Creates a new ScriptableObject asset. The type is resolved via reflection at runtime, so any project-defined ScriptableObject subclass is supported — no package changes required.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "typeName": "MyGame.EnemyConfig",
  "assetPath": "Assets/Data/Enemies/Goblin.asset",
  "properties": {
    "health": 100,
    "speed": 3.5
  }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `typeName` | ✅ | Fully qualified or simple type name of the ScriptableObject subclass |
| `assetPath` | ✅ | Destination path (must start with `Assets/` and end with `.asset`) |
| `properties` | ❌ | Initial property values (same format as PATCH) |

### Response (HTTP 201)

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Data/Enemies/Goblin.asset",
  "type": "MyGame.EnemyConfig",
  "updated": ["health", "speed"]
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | Required fields are missing, `assetPath` does not end with `.asset` or does not start with `Assets/`, type not found, type is not a ScriptableObject, or type is abstract |
| 403 | Asset Write category is disabled |
| 409 | Asset already exists at the specified path, or the Unity Editor is in Play mode |

---

## PATCH /api/assets/scriptableobjects

Updates serialized properties on an existing ScriptableObject asset. Array and nested generic properties are silently skipped.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Query Parameters

| Parameter | Required | Description |
|-----------|----------|-------------|
| `guid` | ✅ | GUID of the target ScriptableObject |

### Request Body (JSON)

```json
{
  "properties": {
    "health": 150,
    "primaryWeapon": { "assetGuid": "def456..." }
  }
}
```

For ObjectReference fields, supply an object with `assetGuid` or `assetPath`. To clear a reference, use `null`.

```json
{ "properties": { "primaryWeapon": null } }
```

### Response

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Data/Enemies/Goblin.asset",
  "type": "MyGame.EnemyConfig",
  "updated": ["health", "primaryWeapon"]
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `guid` is missing, asset is not a ScriptableObject, `properties` field is missing, or a property value is malformed |
| 404 | No asset found for the given GUID |
| 403 | Asset Write category is disabled |
| 409 | Unity Editor is in Play mode |

---

## DELETE /api/assets/scriptableobjects/{guid}

Deletes a ScriptableObject asset and its `.meta` file.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the ScriptableObject asset to delete |

### Response

```json
{ "deleted": "Assets/Data/Enemies/Goblin.asset", "guid": "a1b2c3..." }
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | GUID is empty, or the asset is not a ScriptableObject |
| 404 | No asset found for the given GUID |
| 403 | Asset Write category is disabled |
| 409 | Unity Editor is in Play mode |

---

## POST /api/editor/play

Enters Play mode (`EditorApplication.isPlaying = true`).

> Can be called only when the Play Mode category is enabled.
> If a domain reload occurs, the HTTP server will restart temporarily. Poll `GET /api/editor/status` and wait until `isPlaying: true`.

### Response

```json
{ "playing": true, "note": "Domain reload may occur. Poll GET /api/editor/status until isPlaying is true." }
```

---

## POST /api/editor/stop

Exits Play mode (`EditorApplication.isPlaying = false`).

> Can be called only when the Play Mode category is enabled.

### Response

```json
{ "playing": false }
```

---

## POST /api/editor/pause

Sets the paused state. If the body is omitted, toggles the current state.

> Can be called only when the Play Mode category is enabled.

### Request Body (JSON, optional)

```json
{ "paused": true }
```

### Response

```json
{ "paused": true }
```

---

## POST /api/editor/step

Advances by one frame. Valid only when `isPaused: true`.

> Can be called only when the Play Mode category is enabled.

### Response

```json
{ "stepped": true }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | Not in Play mode, or not paused |
| 403 | Play Mode category is disabled |

---

## GET /api/editor/capture

Captures the current view and returns a base64-encoded image.

- **Play mode**: reads `GameView.m_RenderTexture` via Unity internal reflection — the fully composited frame including Screen Space Overlay Canvas UI. Falls back to `ScreenCapture.CaptureScreenshotAsTexture()` if reflection is unavailable.
- **Edit mode**: renders the last active Scene View camera using `camera.Render()`.

The `target` parameter is not required; the endpoint automatically selects the appropriate source based on the current Editor state.

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `width` | native width (Play) / `640` (Edit) | Output width (px), max 1920 |
| `height` | native height (Play) / `360` (Edit) | Output height (px), max 1080 |
| `format` | `jpeg` | `png` or `jpeg` |
| `quality` | `85` | JPEG quality (1–100, valid when `format=jpeg`) |

### Response

```json
{
  "source": "screen",
  "cameraName": "Main Camera",
  "width": 1920,
  "height": 1080,
  "format": "jpeg",
  "mimeType": "image/jpeg",
  "image": "<base64-encoded image data>"
}
```

| Field | Description |
|-------|-------------|
| `source` | `"screen"` in Play mode, `"sceneView"` in Edit mode |
| `cameraName` | Name of `Camera.main` (Play mode) or the Scene View camera (Edit mode). Omitted when `Camera.main` is `null` |
| `width` / `height` | Actual output dimensions |
| `image` | Base64-encoded image data |

### Errors

| Status | Cause |
|--------|-------|
| 500 | Screen capture failed (Play mode) |
| 503 | No Scene View is currently open (Edit mode) |

### Examples

```bash
# Capture the current view at default resolution
curl http://localhost:8765/api/editor/capture

# Capture at a specific resolution in PNG
curl "http://localhost:8765/api/editor/capture?width=1280&height=720&format=png"
```

### Use with LLM / MCP Bridges

The `mimeType` and `image` fields can be passed directly to an MCP image content block, the same as `/api/cameras/capture`.

---

## GET /api/editor/capture/image

Same as `GET /api/editor/capture` but returns the binary image directly instead of a JSON wrapper.

### Query Parameters

Same as `GET /api/editor/capture` (`width`, `height`, `format`, `quality` — all optional).

### Response

`Content-Type: image/jpeg` (or `image/png`) binary stream. No JSON wrapper.

### Errors

| Status | Cause |
|--------|-------|
| 500 | Screen capture failed (Play mode) |
| 503 | No Scene View is currently open (Edit mode) |

### Examples

```bash
# Open in browser to view directly
open "http://localhost:8765/api/editor/capture/image"

# Save to file
curl -o screenshot.jpg "http://localhost:8765/api/editor/capture/image"

# Save PNG at specified resolution
curl -o view.png "http://localhost:8765/api/editor/capture/image?format=png&width=1280&height=720"
```
