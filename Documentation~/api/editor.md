# API Reference — Editor
**English** | [日本語](editor.ja.md)

Base URL: `http://localhost:<port>/api/` (default port: **8765**). See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

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
| `type` | `all` | Case-insensitive `log` / `warning` / `error` / `exception` / `assert` / `all` |
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

Unknown `type` values return `400 Bad Request` instead of silently disabling the filter.

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
# Open in browser to view directly (URL-encoded target)
open "http://localhost:8765/api/cameras/capture/image?target=%7B%22type%22%3A%22hierarchyPath%22%2C%22value%22%3A%22Main%20Camera%22%7D"

# Save to file with curl
curl --get -o screenshot.png "http://localhost:8765/api/cameras/capture/image" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}' \
  --data-urlencode "format=png"

# Save HD JPEG
curl --get -o hd.jpg "http://localhost:8765/api/cameras/capture/image" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}' \
  --data-urlencode "width=1280" --data-urlencode "height=720" --data-urlencode "quality=90"
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

> Before using a newly written asset or attaching a new script component, poll `GET /api/editor/status` and wait until both `isUpdating: false` and `isCompiling: false`. Script changes can restart the server during a domain reload; retry connection failures with backoff and confirm the idle state again after the server returns.

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

## GET /api/editor/capture

Captures the current view and returns a base64-encoded image.

- **Play mode**: reads `GameView.m_RenderTexture` via Unity internal reflection - the fully composited current GameView frame including Screen Space Overlay Canvas UI. Falls back to `ScreenCapture.CaptureScreenshotAsTexture()` if reflection is unavailable. `width` and `height` resize the captured output image; they do not resize the GameView, Canvas, viewport, or render a new frame at that resolution.
- **Edit mode**: renders the last active Scene View camera using `camera.Render()`.

The `target` parameter is not required; the endpoint automatically selects the appropriate source based on the current Editor state.

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `width` | native width (Play) / `640` (Edit) | Output width (px), max 1920. In Play mode this scales the captured GameView frame instead of re-rendering |
| `height` | native height (Play) / `360` (Edit) | Output height (px), max 1080. In Play mode this scales the captured GameView frame instead of re-rendering |
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

# Capture and resize the output image to a specific resolution in PNG
curl "http://localhost:8765/api/editor/capture?width=1280&height=720&format=png"
```

### Use with LLM / MCP Bridges

The `mimeType` and `image` fields can be passed directly to an MCP image content block, the same as `/api/cameras/capture`.

---

## GET /api/editor/capture/image

Same as `GET /api/editor/capture` but returns the binary image directly instead of a JSON wrapper.
In Play mode, `width` and `height` resize the captured GameView frame; they do not re-render the GameView at that resolution.

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

# Save PNG with the output image resized to the specified resolution
curl -o view.png "http://localhost:8765/api/editor/capture/image?format=png&width=1280&height=720"
```
