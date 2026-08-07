# API Reference — Scenes
**English** | [日本語](scenes.ja.md)

Base URL: read it from `<project>/.unionair/endpoint.txt` at connection time. The port mode defaults to Automatic, so there is no fixed default port, and the file must be reread after a refused connection. [Check the server](../index.md#2-check-the-server) describes the handshake. See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

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

## POST /api/scene/save

Saves the current scene to disk.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Response

```json
{ "saved": true, "path": "Assets/Scenes/SampleScene.unity" }
```

---
