# API Reference — Editor
**English** | [日本語](editor.ja.md)

Base URL: `http://localhost:<port>/api/`, read from `<project>/.unionair/endpoint.txt` at connection time. See the [API Reference index](../api-reference.md) for endpoint discovery, response conventions, and category/security notes.

Shell examples on this page assume `BASE_URL="$(tr -d '\r\n' < .unionair/endpoint.txt)"`, so `${BASE_URL}` already ends with `/api/`.

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
  "unityVersion": "6000.3.5f2",
  "isTestRunning": false,
  "testRunSource": null,
  "testRunId": null,
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "lifecycleGeneration": 3,
  "settled": true,
  "hasCompileErrors": false,
  "compileState": null,
  "compileId": null,
  "compileSource": null,
  "buildState": null,
  "buildId": null,
  "activeActivity": null
}
```

| Field | Type | Description |
|-----------|-----|------|
| `isPlaying` | bool | Whether Play mode is enabled (`EditorApplication.isPlaying`) |
| `isPaused` | bool | Whether playback is paused in Play mode (`EditorApplication.isPaused`) |
| `isCompiling` | bool | Whether scripts are being compiled (`EditorApplication.isCompiling`) |
| `isUpdating` | bool | Whether asset update processing is in progress (`EditorApplication.isUpdating`) |
| `unityVersion` | string | Unity version string |
| `isTestRunning` | bool | Whether a Unity Test Framework run is active |
| `testRunSource` | string \| null | `unionAir` for an API-started run, `external` for a run started by another tool, otherwise `null` |
| `testRunId` | string \| null | UnionAir run ID; `null` for external runs and when idle |
| `sessionId` | string | Identifier regenerated once per Editor process |
| `lifecycleGeneration` | number | Assembly domain counter for the current Editor process, starting at 1 |
| `settled` | bool | Whether the Editor is neither compiling, updating assets, running a player build, nor switching build target |
| `hasCompileErrors` | bool | Hint from `EditorUtility.scriptCompilationFailed` |
| `compileState` | string \| null | `queued` or `running` for an in-flight compilation, otherwise `null` |
| `compileId` / `compileSource` | string \| null | Identity of the in-flight compilation |
| `buildState` | string \| null | `queued` or `running` for an in-flight player build, otherwise `null` |
| `buildId` | string \| null | Identity of the in-flight build |
| `activeActivity` | object \| null | What the Editor is busy with; see [Editor Activities](activities.md) |

This endpoint remains available while tests run. Other than health, help, logs, and Test Runner status/result/cancel operations, endpoints return `409` until the active run finishes.

### Detecting Domain Reloads

The server stops while the assembly domain reloads, so a request during that window fails to connect. `lifecycleGeneration` lets a client tell that apart from a crash: it increments on every domain load, so a value higher than the one observed before the connection dropped confirms a reload completed. The number of reloads so far is `lifecycleGeneration - 1`.

`sessionId` changes only when the Editor process restarts, which also resets `lifecycleGeneration` to 1.

> `settled` is a snapshot, not a guarantee that no reload is imminent. Compilation can finish — clearing `isCompiling` — moments before the domain reload actually begins. Clients must tolerate a dropped connection on any request and retry rather than treating `settled` as a completion signal.
>
> `activeActivity` is the one field to read when a request came back `409`. It names the activity, its source, and the id that owns it, and it applies the same priority the rejection does, so a client does not have to work out from `isCompiling`, `isUpdating`, `isPlaying`, and `isTestRunning` which one to wait for. See [Editor Activities](activities.md).
>
> `hasCompileErrors` is derived from the Console log, which Unity clears on recompile depending on the user's Console settings. Treat it as a hint rather than an authoritative result.

---

## GET /api/editor/logs

Returns Unity Console logs. Up to 1000 entries are kept in an in-memory ring buffer, and every entry is also appended to an NDJSON file so history is **retained across domain reloads** for the lifetime of the Editor process.

### Query Parameters

| Parameter | Default | Description |
|-------------|-----------|------|
| `type` | `all` | Case-insensitive `log` / `warning` / `error` / `exception` / `assert` / `all` |
| `search` | ―  | Case-insensitive partial-match filter on messages |
| `limit` | `100` | Maximum number of results to return (max: 1000) |
| `since` | ―  | Exclusive sequence cursor; returns only entries with `sequence` greater than this value |

### Response

```json
{
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "count": 2,
  "oldestSequence": 0,
  "latestSequence": 42,
  "truncated": false,
  "hasMore": false,
  "logs": [
    {
      "sequence": 42,
      "type": "error",
      "message": "NullReferenceException: Object reference not set...",
      "stackTrace": "MyScript.Update () (at Assets/MyScript.cs:42)",
      "timestamp": "2026-05-16T04:12:00.1234567Z"
    },
    {
      "sequence": 41,
      "type": "warning",
      "message": "Shader 'Custom/Foo' has no shadows pass",
      "stackTrace": "",
      "timestamp": "2026-05-16T04:11:58.7654321Z"
    }
  ]
}
```

| Field | Type | Description |
|-----------|-----|------|
| `sessionId` | string | Identifier regenerated once per Editor process |
| `oldestSequence` | number | Oldest sequence still held in memory, or `-1` when empty |
| `latestSequence` | number | Newest sequence still held in memory, or `-1` when empty |
| `truncated` | bool | Whether entries after `since` had already been evicted from the in-memory buffer |
| `hasMore` | bool | Whether more matching entries existed beyond `limit` |
| `sequence` | number | Monotonic entry number within the current Editor session |

> Logs are returned in newest-first order (`sequence` descending), including when `since` is supplied.
> `timestamp` is UTC ISO 8601.

### Polling With a Cursor

Pass the previous response's `latestSequence` as `since` to fetch only new entries. `since` is **exclusive** and is applied **before** the `type` and `search` filters, so `truncated` reports entries that were lost rather than entries that were filtered out.

`sequence` restarts at 0 in each new Editor process. Compare `sessionId` against the previous response and discard the cursor whenever it changes.

When `truncated` is `true`, fetch [`GET /api/editor/logs.ndjson`](#get-apieditorlogsndjson) to recover entries that are still inside the retained two-file NDJSON window.

Unknown `type` values return `400 Bad Request` instead of silently disabling the filter. A `since` value that is not a non-negative integer also returns `400`.

### Examples

```bash
# Latest 20 errors and exceptions
curl "${BASE_URL}editor/logs?type=error&limit=20"

# Logs containing "NullReference"
curl "${BASE_URL}editor/logs?search=NullReference"

# Only entries newer than sequence 42
curl "${BASE_URL}editor/logs?since=42"
```

---

## GET /api/editor/logs.ndjson

Downloads the retained NDJSON logs for the current Editor session, including entries already evicted from the in-memory ring buffer. One JSON object per line, in oldest-first order, with the same fields as the `logs` array above.

- Content type: `application/x-ndjson`
- Content disposition: `attachment; filename="console.ndjson"`
- Returns `404` when the log file is not available

The active file is rotated when it reaches approximately 8 MiB. The response concatenates the same-session rotated predecessor (`console.1.ndjson`) followed by the active file (`console.ndjson`), so their JSON lines remain oldest-first across the rotation boundary. At most these two files are retained; entries older than the predecessor cannot be recovered. A predecessor left by an earlier Editor process is never included.

```bash
curl -O "${BASE_URL}editor/logs.ndjson"
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
curl "${BASE_URL}cameras"

# Capture Main Camera at default resolution in JPEG
curl --get "${BASE_URL}cameras/capture" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'

# Capture in PNG at HD resolution
curl --get "${BASE_URL}cameras/capture" \
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
open "${BASE_URL}cameras/capture/image?target=%7B%22type%22%3A%22hierarchyPath%22%2C%22value%22%3A%22Main%20Camera%22%7D"

# Save to file with curl
curl --get -o screenshot.png "${BASE_URL}cameras/capture/image" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}' \
  --data-urlencode "format=png"

# Save HD JPEG
curl --get -o hd.jpg "${BASE_URL}cameras/capture/image" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}' \
  --data-urlencode "width=1280" --data-urlencode "height=720" --data-urlencode "quality=90"
```

---

## POST /api/previews/render

Renders a scene GameObject, prefab, or imported model without relying on a Camera in the user's scene. The endpoint copies the target into an isolated preview scene, optionally evaluates animation, frames renderer bounds, creates its own camera and lights, renders every requested time, and closes the preview scene before returning.

The endpoint is in the always-enabled Read category, but it is blocked during Play mode, test runs, compilation, asset updates, builds, and build-target switches. Sampling and rendering happen atomically in this request; an animation pose does not survive for a later capture request.

### Request

```json
{
  "target": { "assetPath": "Assets/Characters/Hero.prefab" },
  "focusPath": "Rig/Head",
  "width": 640,
  "height": 640,
  "format": "png",
  "times": [0.0, 0.5, 1.0],
  "view": {
    "preset": "front",
    "fieldOfView": 30.0,
    "padding": 0.1
  },
  "background": { "r": 0.18, "g": 0.18, "b": 0.18, "a": 1.0 },
  "lighting": {
    "keyIntensity": 1.0,
    "fillIntensity": 0.5,
    "keyColor": { "r": 1.0, "g": 1.0, "b": 1.0 },
    "fillColor": { "r": 0.65, "g": 0.72, "b": 1.0 }
  },
  "animation": {
    "mode": "state",
    "state": "Base Layer.Idle",
    "layer": 0
  }
}
```

All request objects reject unknown and duplicate fields. Numbers must be finite.

### Target and focus

`target` is required and accepts either kind of reference:

- a scene object reference such as `{ "type": "hierarchyPath", "value": "Character" }` or a GameObject `globalObjectId`; `scenePath` selects a loaded scene for a path reference;
- an asset reference such as `{ "assetGuid": "..." }` or `{ "assetPath": "Assets/Character.prefab" }`, resolving to a prefab or the root GameObject of an imported model.

A prefab/model is instantiated with `PrefabUtility.InstantiatePrefab`. A scene object, including one with no prefab connection, is copied with `Object.Instantiate`, detached as a preview root, and moved into the preview scene. The source scene object is never moved or sampled.

`focusPath` is an optional `/`-separated Transform path below the copied target. Only active, enabled Renderers in that subtree contribute bounds. Omit it to frame the whole target. A missing path is `404`; a subtree with no finite, non-zero renderer bounds is `422`.

### View and framing

`view` is optional:

| Field | Default | Description |
|---|---:|---|
| `preset` | `front` | `front`, `back`, `left`, `right`, `top`, `bottom`, or `isometric` |
| `yaw` / `pitch` | — | Explicit orbit in degrees instead of `preset`; yaw 0 is the front (+Z camera position), positive yaw moves toward +X, and positive pitch moves above the target |
| `distance` | auto | Positive world-space distance through 1,000,000. When omitted, every bounds corner is fitted against both horizontal and vertical field of view |
| `fieldOfView` | `30` | Vertical perspective field of view, 1–120 degrees |
| `padding` | `0.1` | Reserved fraction on each image edge, from 0 inclusive to 0.5 exclusive; `0.1` fits into the central 80% |

Do not combine a preset with yaw/pitch. Automatic framing projects all eight corners of the axis-aligned Renderer bounds into the resolved camera axes; it does not approximate the target as a bounding sphere. Framing is recalculated after animation for every frame, so a pose whose bounds change receives its own distance.

### Animation modes

Omit `animation`, or send `{ "mode": "none" }`, to render the copied target as authored. The other modes require exactly one Animator. When the target contains several, select one with `animation.animatorPath`; ambiguity returns `409`.

| Mode | Fields | Meaning of each `times` value |
|---|---|---|
| `clip` | `clip`: AnimationClip asset reference; optional `clipName` | Seconds on an `AnimationClipPlayable` evaluated through the Animator |
| `state` | `state`: full state name; optional `layer` (default 0) | Normalized state time passed to `Animator.Play` |
| `parameters` | `parameters`: array of `{name,value}` | Seconds advanced after rebinding and applying the complete parameter set |

Parameter value types come from the Animator: Float requires a finite JSON number, Int an integer, Bool a boolean, and Trigger a boolean (`true` sets it, `false` resets it). Unknown or repeated names and wrong value types are rejected before rendering. State and parameter modes require a RuntimeAnimatorController.

Clip evaluation uses `AnimationClipPlayable`, not `AnimationMode.SampleAnimationClip`. The playable targets the copied Animator so humanoid retargeting remains the Animator's responsibility; the source Animator and Avatar are never reassigned. For each contributing clip, `appliedBindings` lists bindings whose path and component exist on the copy, while `skippedBindings` lists paths/components that do not.

An `.anim` file and an imported file containing one AnimationClip need no `clipName`. When an imported file contains several clips, omitting it returns `409` with the available names instead of silently selecting one; supply the exact name to choose the sub-asset.

### Sizing, background, and lighting

| Field | Default | Limit / behavior |
|---|---:|---|
| `width`, `height` | `640`, `640` | 1–1920 by 1–1080 |
| `format` | `png` | `png` or `jpeg` |
| `quality` | `85` | JPEG quality, 1–100 |
| `times` | `[0]` | 1–16 values from 0 through 1,000,000; width × height × frame count may not exceed 16,777,216 pixels |

Colours use required `r`, `g`, and `b` values from 0–1 and optional `a` (default 1). The camera uses a solid background. Lighting is independent of the user's scene: two directional lights, no shadows, with default white key intensity 1.0 and blue-tinted fill intensity 0.5. `keyIntensity` and `fillIntensity` accept 0–8. The response repeats the exact background and light model used.

At most eight requests may own a preview scene at once because preview scenes consume finite culling-mask bits. A ninth request returns `429`. Every success and failure closes the scene in a `finally`; closing destroys the clone, camera, and lights and releases the bit. The endpoint does not change the active scene, dirty state, selection, user cameras, Animator assignments, assets, Undo history, or AnimationMode.

### Response

```json
{
  "target": {
    "kind": "asset",
    "name": "Hero",
    "assetGuid": "...",
    "assetPath": "Assets/Characters/Hero.prefab"
  },
  "focusPath": "Rig/Head",
  "width": 640,
  "height": 640,
  "format": "png",
  "mimeType": "image/png",
  "rigType": "humanoid",
  "animatorPath": "",
  "animation": { "mode": "state", "state": "Base Layer.Idle", "layer": 0 },
  "view": {
    "preset": "front",
    "yaw": 0.0,
    "pitch": 0.0,
    "requestedDistance": null,
    "fieldOfView": 30.0,
    "padding": 0.1
  },
  "background": { "r": 0.18, "g": 0.18, "b": 0.18, "a": 1.0 },
  "lighting": {
    "model": "twoDirectionalNoShadows",
    "keyIntensity": 1.0,
    "keyColor": { "r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0 },
    "fillIntensity": 0.5,
    "fillColor": { "r": 0.65, "g": 0.72, "b": 1.0, "a": 1.0 }
  },
  "frames": [{
    "time": 0.0,
    "framing": {
      "bounds": {
        "center": { "x": 0.0, "y": 1.0, "z": 0.0 },
        "size": { "x": 0.7, "y": 0.8, "z": 0.6 }
      },
      "cameraPosition": { "x": 0.0, "y": 1.0, "z": 2.5 },
      "cameraRotation": { "x": 0.0, "y": 1.0, "z": 0.0, "w": 0.0 },
      "distance": 2.5
    },
    "states": [{
      "layer": 0,
      "fullPathHash": 1168970017,
      "shortNameHash": 987654321,
      "normalizedTime": 0.0,
      "length": 1.0,
      "loop": true,
      "clips": [{ "name": "Idle", "weight": 1.0 }]
    }],
    "appliedBindings": [{ "path": "Rig/Hips", "type": "UnityEngine.Transform", "property": "m_LocalPosition.x" }],
    "skippedBindings": [],
    "mimeType": "image/png",
    "image": "<base64>"
  }]
}
```

`rigType` is `humanoid`, `generic`, or `none`. `states` contains every Animator layer for state/parameter evaluation and is empty for direct clip evaluation, which has no AnimatorController state. Hashes are the resolved `AnimatorStateInfo` values, not echoes of the request. Frame order matches `times` order.

### Errors

| Status | Cause |
|---|---|
| 400 | Invalid JSON shape, field, type, range, mode, preset, format, time count, or aggregate pixel count |
| 404 | Target, focus path, Animator path, clip asset, or requested `clipName` was not found |
| 409 | Editor activity conflict or several Animators without `animatorPath` |
| 422 | Target is not a GameObject asset/object, has no usable bounds, lacks an Animator/controller/state, or animation input is incompatible |
| 429 | Eight preview requests already own preview scenes |
| 500 | Unity failed while cloning, evaluating, rendering, or encoding |

### Example

```bash
curl -X POST "${BASE_URL}previews/render" \
  -H "Content-Type: application/json" \
  -d '{
    "target":{"assetPath":"Assets/Characters/Hero.prefab"},
    "times":[0,0.5,1],
    "animation":{"mode":"clip","clip":{"assetPath":"Assets/Animations/Idle.anim"}}
  }'
```

---

## POST /api/previews/render/image

Uses the same body and isolation rules as `POST /api/previews/render`, but requires exactly one `times` value and returns the encoded image directly with `Content-Type: image/png` or `image/jpeg`. Use the JSON endpoint when framing, resolved state, or binding diagnostics are needed.

```bash
curl -X POST "${BASE_URL}previews/render/image" \
  -H "Content-Type: application/json" \
  -d '{"target":{"type":"hierarchyPath","value":"Character"},"times":[0],"format":"png"}' \
  -o preview.png
```

---

## POST /api/editor/refresh

Calls `AssetDatabase.Refresh()` so Unity recognizes changes to scripts and assets.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.
> Returns `409 Conflict` without calling `AssetDatabase.Refresh()` when a loaded scene file has changed externally. This prevents Unity's interactive Reload dialog from blocking the API.

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

### Loaded Scene Conflict — 409

```json
{
  "error": "Cannot refresh assets while loaded scenes have external file changes. Unload them before retrying to avoid Unity's interactive Reload dialog.",
  "code": "loaded_scene_external_change_blocked",
  "loadedScenes": [
    {
      "path": "Assets/Scenes/Level.unity",
      "name": "Level",
      "isDirty": true,
      "isActive": true,
      "reason": "modified"
    }
  ]
}
```

| Field | Description |
|-------|-------------|
| `code` | Stable machine-readable identifier: `loaded_scene_external_change_blocked` |
| `loadedScenes` | Conflicting loaded scenes in Scene Manager order |
| `isDirty` | Whether the in-memory scene has unsaved Editor changes |
| `isActive` | Whether the scene is the active scene |
| `reason` | `modified`, `missing`, `unreadable`, or `untracked`; `untracked` means no trusted disk baseline was recorded |

UnionAir does not save, discard, unload, or reload a scene automatically. Choose which version wins before retrying:

- To keep the in-memory Editor version, save the scene explicitly, overwriting the external file change, then refresh.
- To keep the external file version, unload the scene first. If it is dirty, explicitly save it or unload it with `discardUnsaved: true`; then refresh and reopen it.

An `untracked` scene is never adopted as a new baseline during refresh because doing so could hide a real external change. Save it explicitly to keep the in-memory version, or unload and reopen it to keep the disk version.

Immediately after a cold Editor start, `untracked` can be transient while the background-safe baseline bootstrap waits for scene restoration and asset updating to settle. Retry after several Editor updates before choosing either recovery action. If it persists, use the explicit save or unload-and-reopen procedure above.

The baseline is updated when a scene is opened or saved and is retained across assembly domain reloads. This guard applies to UnionAir-triggered refreshes; Unity's own focus auto-refresh and manual Editor refresh remain subject to Unity's normal behavior.

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
curl "${BASE_URL}editor/menu-items?search=UnionAir"
curl "${BASE_URL}editor/menu-items?root=Window&includeFolders=false"
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
curl -X POST "${BASE_URL}editor/menu-item" \
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
curl "${BASE_URL}editor/capture"

# Capture and resize the output image to a specific resolution in PNG
curl "${BASE_URL}editor/capture?width=1280&height=720&format=png"
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
open "${BASE_URL}editor/capture/image"

# Save to file
curl -o screenshot.jpg "${BASE_URL}editor/capture/image"

# Save PNG with the output image resized to the specified resolution
curl -o view.png "${BASE_URL}editor/capture/image?format=png&width=1280&height=720"
```
