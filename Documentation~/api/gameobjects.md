# API Reference — GameObjects & Components
**English** | [日本語](gameobjects.ja.md)

Base URL: `http://localhost:<port>/api/`, read from `<project>/.unionair/endpoint.txt` at connection time. See the [API Reference index](../api-reference.md) for endpoint discovery, response conventions, and category/security notes.

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
      "enabled": true,
      "properties": {
        "m_Interactable": true,
        "m_Transition": 1
      }
    }
  ]
}
```

`components[].enabled` is the checkbox in the component's Inspector header. It is omitted for a component that has none — a `Transform` or a `MeshFilter` shows no checkbox — so that a reader can tell "this cannot be disabled" from "this is disabled". It is not one of `properties`: Unity draws it outside the component body, and `properties` carries only what the body draws. Write it through the `enabled` field of `PATCH /api/gameobjects/components`.

`components[].blendShapeNames` is the blend shapes of a `SkinnedMeshRenderer`'s mesh, named in mesh order. It is omitted for every other component type, present and empty for a renderer whose mesh has no blend shapes, and present and empty for one with no mesh at all — `m_Mesh` already distinguishes those two. Like `enabled` it sits beside `properties` rather than in it, because a shape name belongs to the `Mesh` and not to any serialized field of the renderer.

The array is positional: index *i* names the shape that `properties.m_BlendShapeWeights[i]` drives, which is how a weight is written. Names are not unique — Unity permits two shapes on one mesh to carry the same name — so the index is the identity and the name is the description. The two arrays can also differ in length, and this is not an edge case: the names are read from the mesh the renderer points at now, the weights from what was serialized on the component, and Unity does not resize the serialized array the moment a mesh is assigned. Measured on 6000.0.80f1, assigning a three-shape mesh to a `SkinnedMeshRenderer` reports three names beside an empty `m_BlendShapeWeights`. Reimporting a mesh with fewer shapes leaves the array long for the same reason. Neither array is corrected to match the other; index defensively.

`components[].properties` are properties obtained via `SerializedObject`.
Supported `SerializedPropertyType` values: `bool`, `int`, `float`, `string`, `Color`, `Vector2/3/4`, `Rect`, `ObjectReference`. Arrays are serialized as JSON arrays whose elements follow the same type rules. Other types are `null`.

### An object reference is spelled the way the write reads it

A reference value can be sent straight back to `PATCH /api/gameobjects/components` without translation, which is what makes read-modify-write possible on a component.

An asset:

```json
"m_Mesh": {
  "assetGuid": "a1b2...",
  "assetPath": "Assets/Meshes/Rock.fbx",
  "assetType": "UnityEngine.Mesh"
}
```

An object in a loaded scene:

```json
"m_ProbeAnchor": {
  "type": "globalObjectId",
  "value": "GlobalObjectId_V1-2-..."
}
```

`type` here means what it means in the request — the *kind of reference*, not the object's class. The class is `assetType`, and only assets carry one. This is the same spelling `GET /api/assets/scriptableobjects/{guid}` uses for an asset reference, so both reads agree.

Two things follow from using Unity's own identities rather than a description:

- **There is no display name.** No field of the write carries one, so reporting it would mean the write either refusing the value again or accepting a key it ignores. `assetPath` names an asset; a scene object is named by resolving it.
- **A scene object in a scene that has never been saved cannot be addressed.** Unity has no `GlobalObjectId` for it and answers the null id `GlobalObjectId_V1-0-0000...-0-0`, which resolves to nothing. This is a property of the identity, not of this endpoint: save the scene and the reference addresses normally.

A reference to a **built-in Unity resource** — the mesh on a primitive, `Library/unity default resources` — reports its GUID and path like any asset, and sending it back answers `404`. Those objects are addressed by GUID *and* file ID, and the write vocabulary has no file ID. Reading them works; writing them is out of reach.

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
  },
  "enabled": false
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `properties` | Conditional | Serialized properties to write. Required when `enabled` is omitted |
| `enabled` | Conditional | The Inspector header checkbox. Required when `properties` is omitted |

Send either field or both; a body carrying neither answers `400`.

`enabled` is a field of its own rather than a key in `properties` because the checkbox is not a property this endpoint can address: Unity draws it in the component header rather than in the body, and `properties` reaches only what the body draws. Sending `m_Enabled` as a property key answers `400` and says so. A component that shows no checkbox — a `Transform`, a `MeshFilter` — has no enabled state, and `enabled` answers `400` naming the type.

Each key in `properties` is a `SerializedProperty.propertyPath`. Top-level field names are still accepted for compatibility.

Every key must be unique and name a property this endpoint can write, and only keys at the top level of `properties` are read — a name appearing inside another property's value is part of that value, not a request to write it. A duplicate key, a key that names nothing, one that names something unwritable, or one carrying a value of the wrong shape answers `400` and says which key and why, rather than being passed over. `updated` therefore always lists every key the request sent, and a `200` means the whole request was applied. An empty `properties` object is accepted and updates nothing.

Nested generic types and `m_Script` are among the properties this endpoint cannot write. Sending one is an error, not a no-op.

An array is written in one of three ways, all of them `SerializedProperty.propertyPath` spellings Unity itself produces:

| Key | Effect |
|------|------|
| `m_Materials` | Replaces the array, resizing it to the length of the JSON array |
| `m_Materials.Array.data[0]` | Writes one element, leaving the length alone |
| `m_Materials.Array.size` | Resizes only |

```json
{
  "properties": {
    "m_Materials": [
      { "assetPath": "Assets/Materials/Brick.mat", "assetType": "UnityEngine.Material" },
      null
    ]
  }
}
```

An element value takes the same shape a top-level property of that serialized type takes, so `null`, a scalar, `{r,g,b,a}`, `{x,y,…}`, and every object reference form below all work inside an array. An element that cannot be applied is reported by its own address rather than by the array's, so `{"m_Materials": [null, 5]}` names `m_Materials.Array.data[1]`.

Replacing an array is a replacement and not a merge: Unity keeps no identity per element, so the JSON array's length becomes the array's length. An element address never resizes, and an index past the end answers `400` naming the current length.

Growing an array fills the new slots the way Unity does, by copying the last element rather than clearing them. A length above 1,000,000 answers `400`. That bound is not a statement about what Unity supports: `Array.size` is the one write whose cost is not paid for in the request body, and without it a mistyped length asks the Editor to allocate whatever was sent.

One request must not both set an array's length and write its elements. Two element addresses are two independent writes and are accepted; a length beside them — whether spelled `m_Materials` or `m_Materials.Array.size` — answers `400`, because which of them applies first is not a question this endpoint answers on a caller's behalf.

An array whose elements are a serialized type this endpoint cannot write, such as a `List<T>` of a serializable struct, is refused through all three addresses. The read serializes such an element as `null`, so replacing or dropping one would destroy content a caller has never seen. Growing it is refused with the rest, rather than leaving a resize that works in one direction only.

A key that reaches inside an array in any other form is refused by name. `m_Materials.Array.data[0].name` is an element sub-path, and writing a field inside an element is not supported.

Color and vector objects are partial patches: omitted members retain their current values. At least one supported member must be present, every supplied member must be a JSON number, and unknown or duplicate members are rejected.

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
Object reference objects accept only the members shown in the supported shapes above; unknown or duplicate members are rejected.

### Response

```json
{
  "path": "Directional Light",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.Light",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "enabled": false,
  "updated": ["m_Intensity", "m_Color"]
}
```

`enabled` reports the state after the write whether or not the request set it, and is omitted for a component that has none. `updated` lists `properties` keys only.

### Errors

| Status | Cause |
|-----------|------|
| 400 | `target` is missing or malformed, `type` is an unknown component name, or the body carries neither `properties` nor `enabled` |
| 400 | `enabled` is not a JSON boolean, or the target component type has no enabled state |
| 400 | An object reference payload is malformed, contains an unknown or duplicate member, or requests a type that cannot be resolved |
| 400 | A key in `properties` names no serialized property on the component |
| 400 | A key in `properties`, or a member of a color, vector, or object reference value, is duplicated |
| 400 | A key names a property this endpoint cannot write: a nested generic type, `m_Script`, an array of elements it cannot write, or a serialized type with no write support |
| 400 | A key reaches inside an array in a form other than `name.Array.data[i]` or `name.Array.size` |
| 400 | An element index is past the end of its array, or an `Array.size` is negative |
| 400 | One request both sets an array's length and writes its elements |
| 400 | A value does not match the shape its property takes — a number sent as a string, a vector sent as a scalar |
| 400 | The value of a key is not well-formed JSON |
| 404 | The GameObject, component, or asset does not exist |
| 422 | `target` resolves to a GameObject but `type` was not provided; or the resolved object is not assignable to the requested type or field type |
| 403 | Scene Write category is disabled |

---
