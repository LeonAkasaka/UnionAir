# API Reference — GameObjects & Components
**English** | [日本語](gameobjects.ja.md)

Base URL: read it from `<project>/.unionair/endpoint.txt` at connection time. The port mode defaults to Automatic, so there is no fixed default port, and the file must be reread after a refused connection. [Check the server](../index.md#2-check-the-server) describes the handshake. See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

Shell examples on this page assume `BASE_URL="$(tr -d '\r\n' < .unionair/endpoint.txt)"`, so `${BASE_URL}` already ends with `/api/`.

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
curl "${BASE_URL}search/gameobjects?name=Enemy"

# GameObjects with Camera component (include component list)
curl "${BASE_URL}search/gameobjects?component=Camera&includeComponents=true"

# References a specific asset + inactive only
curl "${BASE_URL}search/gameobjects?assetGuid=abc123&active=false"
```

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
