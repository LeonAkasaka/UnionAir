# API Reference — Assets
**English** | [日本語](assets.ja.md)

Base URL: `http://localhost:<port>/api/` (default port: **8765**). See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

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
> Returns `409 Conflict` without deleting anything when the target is a loaded scene or a folder containing loaded scenes.

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
| 409 | The target is a loaded scene, contains a loaded scene, or the Editor is in Play mode |

Loaded scenes are rejected regardless of their dirty state. UnionAir does not save, discard, or unload them automatically. Unload every reported scene explicitly before retrying the delete.

```json
{
  "error": "Cannot delete loaded scenes. Unload them before retrying to avoid deleting the backing asset of an open scene.",
  "code": "loaded_scene_delete_blocked",
  "assetPath": "Assets/Scenes",
  "loadedScenes": [
    {
      "path": "Assets/Scenes/Level.unity",
      "name": "Level",
      "isDirty": true,
      "isActive": true
    }
  ]
}
```

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
| `assetPath` | Conditional | Project-relative path under `Assets/` or `Packages/`. Required when `guid` is omitted. An existing file may be imported before Unity has assigned it a GUID |

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
| 422 | Unity imported the path but did not register an asset GUID |
| 422 | The asset could not be opened |

---

## POST /api/assets/reimport

Reimports one project asset using `AssetDatabase.ImportAsset()`.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.
> A loaded `.unity` scene cannot be reimported. Unity can otherwise show an interactive
> Reload dialog that blocks all API processing. Unload the scene before retrying.

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

When the target is a loaded scene, or `recursive: true` targets a folder containing
loaded scenes, the endpoint returns `409 Conflict` before calling
`AssetDatabase.ImportAsset()`:

```json
{
  "error": "Cannot reimport loaded scenes. Unload them before retrying to avoid Unity's interactive Reload dialog.",
  "code": "loaded_scene_reimport_blocked",
  "assetPath": "Assets/Scenes",
  "loadedScenes": [
    {
      "path": "Assets/Scenes/Level.unity",
      "name": "Level",
      "isDirty": true,
      "isActive": true
    }
  ]
}
```

| Conflict field | Type | Description |
|----------------|------|-------------|
| `code` | string | Stable value `loaded_scene_reimport_blocked` |
| `assetPath` | string | Resolved asset or folder path from the request |
| `loadedScenes` | array | Loaded scenes that conflict with the requested import, in scene-manager order |
| `loadedScenes[].path` | string | Scene asset path |
| `loadedScenes[].name` | string | Scene name |
| `loadedScenes[].isDirty` | bool | Whether the scene has unsaved Editor changes |
| `loadedScenes[].isActive` | bool | Whether the scene is active |

For a clean scene, call `POST /api/scenes/unload`, retry the reimport, and then call
`POST /api/scenes/open`. For a dirty scene, first choose explicitly whether to save
the Editor changes or unload with `discardUnsaved: true`. The reimport endpoint never
saves, unloads, or discards a scene automatically.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `guid` and `assetPath` are both missing |
| 403 | Asset Write category is disabled |
| 404 | No matching asset exists |
| 409 | The Unity Editor is in Play mode, or the request targets one or more loaded scenes |

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

## PATCH /api/assets/texture-importer/{guid}

Updates texture import settings and reimports the asset.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the texture asset |

### Request Body (JSON)

```json
{
  "textureType": "Sprite",
  "spriteMode": "Single",
  "pixelsPerUnit": 100
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `textureType` | ❌ | `Sprite`, `Default`, `NormalMap`, `GUI`, `Cursor`, `Cookie`, `Lightmap`, or `SingleChannel` |
| `spriteMode` | ❌ | `Single`, `Multiple`, or `Polygon` (only when `textureType` is `Sprite`) |
| `pixelsPerUnit` | ❌ | Pixels per unit for Sprite type |

At least one field must be provided.

### Response

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Actors/portrait.png",
  "textureType": "Sprite",
  "spriteMode": "Single",
  "pixelsPerUnit": 100.0
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | No recognized fields, unknown `textureType` value, or asset is not a texture |
| 404 | No asset found for the given GUID |
| 403 | Asset Write category is disabled |

---
