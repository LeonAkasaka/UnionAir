# API Reference — Assets
**English** | [日本語](assets.ja.md)

Base URL: `http://localhost:<port>/api/`, read from `<project>/.unionair/endpoint.txt` at connection time. See the [API Reference index](../api-reference.md) for endpoint discovery, response conventions, and category/security notes.

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
| `subAssets` | object[] | Objects the file holds besides its main asset. Omitted entirely for a path that holds only its main asset |

```json
"subAssets": [
  { "localIdentifier": "4300014", "name": "BLW_DEF", "type": "UnityEngine.Mesh" },
  { "localIdentifier": "4300038", "name": "button",  "type": "UnityEngine.Mesh" }
]
```

`localIdentifier` is what an [object reference](general.md#naming-one-object-inside-a-file) sends to name one of them, so this is where a client reads it. `name` describes; it does not resolve, and two sub-assets may share one.

The field appearing at all is the signal that the path cannot be addressed by path and type alone.

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

## GET /api/assets/materials/{guid}

Returns a material's shader, render queue, enabled keywords, and the current value of every property its shader declares.

> Requires the Read category (enabled by default).

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the material asset |

### Response

```json
{
  "guid": "5ebb6c...",
  "assetPath": "Assets/Materials/Hair.mat",
  "shader": "Toon/Toon",
  "renderQueue": 2000,
  "keywords": ["_EMISSIVE_SIMPLE", "_IS_CLIPPING_OFF"],
  "properties": [
    { "name": "_BaseColor", "type": "Color", "value": { "r": 1, "g": 1, "b": 1, "a": 1 }, "flags": [] },
    { "name": "_BumpScale", "type": "Range", "value": 1.0, "range": { "min": 0, "max": 1 }, "flags": [] },
    {
      "name": "_MainTex",
      "type": "Texture",
      "value": { "assetGuid": "cbb65e...", "assetPath": "Assets/Textures/hair.tga", "assetType": "UnityEngine.Texture2D" },
      "flags": []
    },
    { "name": "_ToonMaterialVersion", "type": "Int", "value": 0, "flags": ["HideInInspector"] }
  ]
}
```

| Field | Description |
|-------|-------------|
| `shader` | The shader's name — the same string `POST /api/assets/materials` takes, so a material can be recreated |
| `renderQueue` | `Material.renderQueue`. Reported, not writable |
| `keywords` | Enabled shader keywords. Reported, not writable |
| `properties[].name` | Shader property name, and a key `PATCH /api/assets/materials` accepts |
| `properties[].type` | `Color`, `Float`, `Range`, `Int`, `Vector`, or `Texture` |
| `properties[].value` | The current value, spelled the way the write reads it |
| `properties[].range` | `{min, max}`, present only on a `Range` property |
| `properties[].flags` | Unity's shader property flag names, such as `HideInInspector`, `Normal`, `Gamma`, `PerRendererData` |

Properties are listed in the order the shader declares them, and every declared property appears, hidden ones included. A shader can declare a couple of hundred; `flags` is how a client tells the material's real surface from its plumbing.

### The value is spelled the way the write reads it

Every `value` can be sent back to [`PATCH /api/assets/materials`](#patch-apiassetsmaterials) without translation, so a read, a change to one value, and a write back is a round trip that holds.

| Shader property type | Reported as |
|---|---|
| `Color` | `{"r":float,"g":float,"b":float,"a":float}` |
| `Float`, `Range` | `float` |
| `Int` | `int` |
| `Vector` | `{"x":float,"y":float,"z":float,"w":float}` |
| `Texture` | An [object reference](general.md#object-references), or `null` when nothing is assigned — which is also what the write takes to clear one |

`properties` is an array here because `type`, `range` and `flags` have nowhere to live in a name-to-value map, and they are the part only Unity can answer: a `.mat` file carries the overrides Unity recorded, not the property set or its types. The write takes a map, and every `name` in this array is a key it accepts.

`renderQueue` and `keywords` are the two fields that do not round trip. They are reported because they are usually why a material built from another one's property values still does not look the same; writing them is not part of this endpoint's counterpart.

Texture scale and offset are not reported. They are not shader properties of their own, and the write has no vocabulary for them.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `guid` is empty, or the asset is not a material |
| 404 | No asset exists for the GUID |

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
    "_BumpMap": { "assetGuid": "b1c2d3..." }
  }
}
```

Each key in `properties` is a shader property name. Names are the ones the shader declares and are case-sensitive.

Types of values in `properties`:

| Shader property type | Format |
|----|------|
| Color | `{"r":float,"g":float,"b":float,"a":float}` |
| Float, Range | `float` |
| Int | `int` |
| Vector | `{"x":float,"y":float,"z":float,"w":float}` |
| Texture | An [object reference](general.md#object-references) naming an asset — `assetGuid`, `assetPath`, optional `assetType` — or `null` to clear it |

`Color` and `Vector` values may omit components; an omitted component keeps the material's current value. An object carrying an unknown, duplicate or non-numeric component, or no component at all, answers `400`.

### Every key is written, or none is

A key that names no property on the material's shader, one whose value is the wrong shape for that property's type, and a duplicate key each answer `400` naming the key and the reason. The material is not touched: the request is resolved completely before the first value is applied, so a refusal leaves it exactly as it was.

`updated` therefore always lists every key the request sent, and a `200` means the whole request was applied. It is not a filter to compare against the request.

A texture is named the way `GET /api/gameobjects` reports one, so a texture read from a renderer's material can be sent back without translation. A bare GUID string is not an object reference and answers `400`.

### Response

```json
{ "updated": ["_BaseColor", "_Metallic"] }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | `guid` is missing, `properties` is missing or not a JSON object, or a key names no shader property, repeats another key, or carries a value of the wrong shape |
| 404 | No matching material exists, or a texture value names an asset that does not exist |
| 403 | Asset Write category is disabled |

---

## GET /api/assets/shaders/{guid}

Returns a shader's import state, cached compiler messages, declared keywords, declared properties with their defaults, and the subshaders Unity compiled.

> Requires the Read category (enabled by default).

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the shader asset |

### Response

```json
{
  "guid": "5ebb6c...",
  "assetPath": "Assets/Shaders/Toon.shader",
  "name": "Toon/Toon",
  "isSupported": true,
  "hasError": false,
  "hasWarnings": false,
  "messages": [],
  "renderQueue": 2000,
  "maximumLOD": -1,
  "subshaderCount": 1,
  "passCount": 2,
  "keywords": [
    { "name": "_ALPHATEST_ON", "isOverridable": false, "isDynamic": false }
  ],
  "properties": [
    { "name": "_BaseColor", "type": "Color", "description": "Base Color", "defaultValue": { "r": 1, "g": 1, "b": 1, "a": 1 }, "flags": ["MainColor"], "attributes": [] },
    { "name": "_Cutoff", "type": "Range", "description": "Alpha Cutoff", "defaultValue": 0.5, "range": { "min": 0, "max": 1 }, "flags": [], "attributes": [] },
    { "name": "_AlphaClip", "type": "Float", "description": "Alpha Clipping", "defaultValue": 0, "flags": [], "attributes": ["Toggle(_ALPHATEST_ON)"] },
    { "name": "_MainTex", "type": "Texture", "description": "Base Map", "defaultValue": "white", "textureDimension": "Tex2D", "flags": ["MainTexture"], "attributes": [] }
  ],
  "activeSubshaderIndex": 0,
  "subshaders": [
    {
      "levelOfDetail": 300,
      "passes": [
        { "name": "ForwardLit", "lightMode": "UniversalForward", "isGrabPass": false },
        { "name": "ShadowCaster", "lightMode": "ShadowCaster", "isGrabPass": false }
      ]
    }
  ]
}
```

| Field | Description |
|-------|-------------|
| `guid`, `assetPath` | The asset the shader was imported from. A shader Unity built into the editor reports the shared built-in resource container instead — `Standard` reports `Resources/unity_builtin_extra` and the GUID every built-in asset shares — so its `guid` is not an identity and reading it back answers `400`. The lookup by name is how such a shader is reached |
| `name` | The shader's name — the string `POST /api/assets/materials` takes and `GET /api/assets/materials/{guid}` reports. `null` only when the ShaderLab parse failed before the name was read |
| `isSupported` | Unity's capability signal: whether this shader can run on the current GPU, with fallbacks taken into account. It is **not** a statement about the import and **not** a statement about whose declaration the fields below describe — see [What `isSupported` does and does not tell you](#what-issupported-does-and-does-not-tell-you) |
| `renderQueue` | The queue declared by the shader, which a material can override |
| `maximumLOD` | `Shader.maximumLOD`. `-1` when the shader sets no cap, which is the ordinary case |
| `hasError`, `hasWarnings` | Whether Unity recorded errors or warnings for the last import. `hasError` does **not** mean the shader is unusable; see `isSupported` |
| `messages[]` | The compiler messages Unity cached at import. See [Diagnostics come from the last import](#diagnostics-come-from-the-last-import) |
| `keywords[]` | The shader's **effective local keyword space** — every keyword valid on it, enabled or not, with `isOverridable` and `isDynamic`. Wider than the source: it also carries keywords reached through `Fallback` and `UsePass` dependencies, and keywords Unity adds by itself. Measured on 6000.0.80f1, a shader declaring exactly one keyword through `multi_compile` reports five, the other four being `STEREO_INSTANCING_ON`, `UNITY_SINGLE_PASS_STEREO`, `STEREO_MULTIVIEW_ON` and `STEREO_CUBEMAP_RENDER_ON`. A name appearing here is **not** evidence that it appears in the file |
| `properties[]` | Every declared property, in declaration order, hidden ones included |
| `properties[].type` | `Color`, `Float`, `Range`, `Int`, `Vector`, or `Texture` |
| `properties[].description` | The Inspector label, or `null` when the declaration carries none |
| `properties[].defaultValue` | The value a new material starts at, spelled the way `PATCH /api/assets/materials` reads it. A `Texture` reports its built-in texture name (`"white"`, `"bump"`) rather than an object reference, because that is what the declaration carries |
| `properties[].range` | `{min, max}`, present only on a `Range` property |
| `properties[].textureDimension` | What kind of texture the property expects (`Tex2D`, `Cube`, `Tex3D`, …), present only on a `Texture` property |
| `properties[].flags` | Unity's shader property flag names, the same set `GET /api/assets/materials/{guid}` reports. Unity turns some declaration attributes into flags: `[HideInInspector]`, `[MainTexture]`, `[MainColor]`, `[HDR]`, and `[NoScaleOffset]` all arrive here rather than in `attributes` |
| `properties[].attributes` | The declaration attributes Unity did not turn into a flag, verbatim and with their arguments — `Toggle(_ALPHATEST_ON)`, `KeywordEnum(...)`, a custom drawer's name. `Toggle` is the one worth reading: it names the keyword a property drives, which no flag reports |
| `activeSubshaderIndex` | Which subshader Unity selected for the current platform and pipeline |
| `subshaders[]` | The subshaders Unity **compiled**, which is not always what the file declares — when a shader's own subshaders are unusable and it names a `Fallback`, these are the fallback's. A pass's `name` is `null` when the shader did not name it, and `lightMode` is its `LightMode` tag, or `null` when it declares none |

### What this answers that the file does not

A client can write a `.shader` or `.hlsl` file itself, and this endpoint does not take that over. Two things are not in the file:

- **Whether Unity accepted it.** Shader compilation happens at import, and a shader that failed still sits on disk looking exactly as it did. `hasError` and `messages` close the edit-import-diagnose loop the same way [`POST /api/compile`](compile.md) closes it for C#.
- **What the import produced.** `activeSubshaderIndex` is decided by the current render pipeline and platform and is written nowhere. A Shader Graph asset does not expose its properties, keywords or passes in readable form at all — they are generated during import.

### Diagnostics come from the last import

`messages` is what Unity cached when the asset was last imported, not a fresh compile. After editing the file, reimport it with [`POST /api/assets/reimport`](#post-apiassetsreimport) or [`POST /api/editor/refresh`](editor.md#post-apieditorrefresh), then read again.

Each message keeps its context rather than being flattened into a string:

| Field | Description |
|-------|-------------|
| `severity` | `Error` or `Warning` |
| `message` | The compiler message |
| `messageDetails` | Unity's longer form, or `null` where it has none |
| `file` | The file the message points at, which can be an included file rather than the shader. `null` when the message names none |
| `line` | The line in that file, or `0` when the message names none |
| `platform` | The graphics API the message came from, which is why the same edit can fail on one and pass on another. `null` when the message has no API behind it — a ShaderLab parse error happens before any is involved, and Unity reports an undefined platform there |

### When Unity read nothing from the file

The structural fields are `null` in exactly one case: the ShaderLab parse failed before the shader's name was read, so nothing in the response could have come from the file. Measured on 6000.0.80f1 against a shader declaring one property and one pass that failed that way, `name` was `""`, `properties` was empty, `keywords` listed four stereo keywords the shader never declared, and `passCount` was `3` against the one pass in the file. None of that is distinguishable from a real answer, and a client building a material from `properties` would build one with no properties and never learn why.

So when that happens, `renderQueue`, `maximumLOD`, `subshaderCount`, `passCount`, `keywords`, `properties`, `activeSubshaderIndex`, and `subshaders` are `null` together, and `messages` is the answer instead. `guid`, `assetPath`, `isSupported`, `hasError`, `hasWarnings`, and `messages` are always reported.

That case is narrow on purpose. Every other shader — including one that fails to compile, and one that cannot run here — reports what it declares.

### What `isSupported` does and does not tell you

`isSupported` is [Unity's own capability signal](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Shader-isSupported.html): whether the shader can run on the current GPU, with fallbacks taken into account. Two measurements on 6000.0.80f1 show why it cannot be read as anything more:

| Shader | `hasError` | `isSupported` | What the fields describe |
| --- | --- | --- | --- |
| Valid, but its only pass is excluded for the current renderer | `false` | **`false`** | Its own declaration — `properties`, `name` and the rest are correct |
| The same shader with `Fallback "Diffuse"` | `false` | **`true`** | `properties` is its own, but `subshaders` is `Legacy Shaders/Diffuse`'s — two subshaders and four passes, identical to reading that shader directly |

So a `false` does not mean the import failed, and a `true` does not establish that `subshaders` belongs to the file you wrote. Read `isSupported` as "can this shader be used here", `hasError` as "did my edit compile cleanly", and neither as a claim about provenance.

`hasError` is likewise not a usability signal. Measured against a shader whose first subshader fails to compile and whose second does not, `hasError` is `true` while `isSupported` is `true`, and Unity selects the working subshader.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `guid` is empty, or the asset is not a shader |
| 404 | No asset exists for the GUID |

---

## GET /api/assets/shaders

Returns the same report for the shader with a given name.

> Requires the Read category (enabled by default).

### Query Parameters

| Parameter | Description |
|-----------|-------------|
| `name` | The shader name, as `GET /api/assets/materials/{guid}` reports it and `POST /api/assets/materials` takes it |

The name is what a material carries, and it is not the file name. This is also the only way to reach a shader that ships with Unity rather than living in the project's assets. `Standard` is the example: it reports `Resources/unity_builtin_extra` and the GUID every built-in asset shares, and sending that GUID to `GET /api/assets/shaders/{guid}` answers `400 Asset is not a Shader`, because the container's main asset is not one. The name is the only handle such a shader has.

The lookup is the same one `POST /api/assets/materials` performs, so a name this endpoint answers 404 for is a name that endpoint would also fail on. Asking here first is how a client finds that out before creating the material.

### Response

The same document as [`GET /api/assets/shaders/{guid}`](#get-apiassetsshadersguid).

### Errors

| Status | Cause |
|--------|-------|
| 400 | `name` is missing or empty |
| 404 | No shader carries the name |

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
    "tags": ["fire", "aoe"]
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
| Array | JSON array whose elements follow the same rules |
| Nested generic types | `null` |

### Errors

| Status | Cause |
|--------|-------|
| 400 | GUID is empty, or the asset is not a ScriptableObject |
| 404 | No asset found for the given GUID |

---

## POST /api/assets/scriptableobjects

Creates a new ScriptableObject asset. The type is resolved via reflection at runtime, so any project-defined ScriptableObject subclass is supported — no package changes required.

When `properties` is present, it follows the same all-or-nothing validation as PATCH: every key must be unique, name a writable serialized property, and carry a compatible JSON value. A rejected request does not create the asset.

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
| 400 | An initial `properties` key is duplicated, names no writable serialized property, or carries a value of the wrong shape |
| 403 | Asset Write category is disabled |
| 409 | Asset already exists at the specified path, or the Unity Editor is in Play mode |

---

## PATCH /api/assets/scriptableobjects

Updates serialized properties on an existing ScriptableObject asset.

Every key in `properties` must be unique and name a property this endpoint can write, and only keys at the top level are read — a name appearing inside another property's value is part of that value, not a request to write it. A duplicate key, a key that names nothing, one that names something unwritable, or one carrying a value of the wrong shape answers `400` and says which key and why. `updated` therefore always lists every key the request sent. Nested generic types and `m_Script` cannot be written; sending one is an error rather than a no-op. An empty `properties` object is accepted and updates nothing.

An array is written whole as a JSON array (`"tags": ["fire", "aoe"]`), one element at a time as `tags.Array.data[0]`, or resized as `tags.Array.size`. The rules are the same ones [PATCH /api/gameobjects/components](gameobjects.md#patch-apigameobjectscomponents) documents in full: a whole-array write is a replacement rather than a merge, an element address never resizes, one request must not both set a length and write elements, an array whose elements are a serialized type this endpoint cannot write is refused through all three addresses, a key reaching inside an array in any other form is refused by name, and a length above 1,000,000 is refused. An element object reference resolves assets only, as every object reference on this endpoint does.

Color and vector objects are partial patches: omitted members retain their current values. At least one supported member must be present, every supplied member must be a JSON number, and unknown or duplicate members are rejected.

ObjectReference values accept only `assetGuid`, `assetPath`, and optional `assetType`; unknown or duplicate members are rejected.

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

For ObjectReference fields, supply an object with `assetGuid` or `assetPath` and optional `assetType`. Unknown or duplicate members are rejected. To clear a reference, use `null`.

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
| 400 | `guid` is missing, asset is not a ScriptableObject, `properties` is missing, a property value is malformed, or a composite value contains an unknown member |
| 400 | A key in `properties` names no serialized property on the asset |
| 400 | A key in `properties`, or a member of a color, vector, or object reference value, is duplicated |
| 400 | A key names a property this endpoint cannot write: a nested generic type, `m_Script`, an array of elements it cannot write, or a serialized type with no write support |
| 400 | A key reaches inside an array in a form other than `name.Array.data[i]` or `name.Array.size` |
| 400 | An element index is past the end of its array, or an `Array.size` is negative |
| 400 | One request both sets an array's length and writes its elements |
| 400 | A value does not match the shape its property takes |
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

## GET /api/assets/audio-importer/{guid}

Returns typed settings for an audio asset's `AudioImporter`, the platform override
catalog for this Editor, and the imported `AudioClip` metadata.

> This endpoint belongs to the Read category.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of an asset whose importer is an `AudioImporter` |

### Response

```json
{
  "guid": "a1b2c3...",
  "assetPath": "Assets/Audio/theme.ogg",
  "forceToMono": false,
  "normalize": true,
  "ambisonic": false,
  "loadInBackground": false,
  "defaultSampleSettings": {
    "loadType": "CompressedInMemory",
    "compressionFormat": "Vorbis",
    "quality": 0.7,
    "preloadAudioData": true,
    "sampleRateSetting": "PreserveSampleRate",
    "sampleRateOverride": 0,
    "conversionMode": 0
  },
  "defaultCompressionFormats": ["PCM", "Vorbis", "ADPCM"],
  "supportedConversionModes": [0],
  "platforms": [{
    "platform": "WebGL",
    "installed": false,
    "compressionFormats": ["AAC"],
    "override": false,
    "inherited": {
      "loadType": "CompressedInMemory",
      "compressionFormat": "Vorbis",
      "quality": 0.7,
      "preloadAudioData": true,
      "sampleRateSetting": "PreserveSampleRate",
      "sampleRateOverride": 0,
      "conversionMode": 0
    },
    "effective": {
      "loadType": "CompressedInMemory",
      "compressionFormat": "AAC",
      "quality": 0.7,
      "preloadAudioData": true,
      "sampleRateSetting": "OverrideSampleRate",
      "sampleRateOverride": 44100,
      "conversionMode": 0
    }
  }],
  "audioClip": {
    "name": "theme",
    "length": 12.5,
    "channels": 2,
    "frequency": 44100,
    "samples": 551250,
    "loadType": "CompressedInMemory",
    "preloadAudioData": true,
    "ambisonic": false,
    "loadInBackground": false,
    "loadState": "Loaded"
  }
}
```

`normalize` is a boolean when the current Editor exposes the serialized normalization
setting; otherwise GET returns `null`. PATCH still requires a boolean and returns `400`
when that Editor cannot update the setting.

`defaultSampleSettings` and each platform's `inherited` object are the stored default
baseline. `effective` is what `AudioImporter.GetOverrideSampleSettings()` reports for
that platform. When `override` is `false`, Unity may translate the inherited baseline;
WebGL changing a default codec to `AAC` is one example. When `override` is `true`,
`effective` is the explicit override.

`platforms` is derived from the non-obsolete build targets known to this Editor.
`installed` reports whether at least one target in that group has its platform module
installed; an uninstalled platform remains readable and may still have serialized
override settings.

### Compression Format Compatibility

The response's `compressionFormats` arrays are authoritative for the current request.
The compatibility model is:

| Settings | Accepted formats |
|----------|------------------|
| Default, `Standalone`, `WSA` | `PCM`, `Vorbis`, `ADPCM` |
| `WebGL` | `AAC` |
| `PS4`, `PS5` | `PCM`, `Vorbis`, `ADPCM`, `MP3`, `ATRAC9` |
| `GameCoreScarlett`, `GameCoreXboxSeries`, `GameCoreXboxOne` | `PCM`, `Vorbis`, `ADPCM`, `MP3`, `XMA` |
| Other platforms reported by this Editor | `PCM`, `Vorbis`, `ADPCM`, `MP3` |

Current platform names are used (`iOS`, `WSA`), not their legacy enum aliases
(`iPhone`, `Metro`).

### Errors

| Status | Cause |
|--------|-------|
| 400 | The asset does not use an `AudioImporter` |
| 404 | No asset found for the given GUID |

---

## PATCH /api/assets/audio-importer/{guid}

Validates and updates AudioImporter settings, calls `SaveAndReimport()` once when
anything changed, and returns the final state described above.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode or during a conflicting Editor activity.

### Request Body (JSON)

```json
{
  "forceToMono": true,
  "normalize": true,
  "defaultSampleSettings": {
    "loadType": "CompressedInMemory",
    "compressionFormat": "Vorbis",
    "quality": 0.7,
    "preloadAudioData": true,
    "sampleRateSetting": "OptimizeSampleRate"
  },
  "platformOverrides": [{
    "platform": "Android",
    "override": true,
    "sampleSettings": {
      "compressionFormat": "Vorbis",
      "quality": 0.5,
      "preloadAudioData": false
    }
  }, {
    "platform": "WebGL",
    "override": false
  }]
}
```

Top-level fields:

| Field | Type | Description |
|-------|------|-------------|
| `forceToMono` | bool | Converts the imported source to mono |
| `normalize` | bool | Normalizes the source after forcing it to mono |
| `ambisonic` | bool | Treats the clip as ambisonic audio |
| `loadInBackground` | bool | Loads clip data without blocking the main thread |
| `defaultSampleSettings` | object | Partial patch applied to the stored default sample settings |
| `platformOverrides` | array | Platform override creations, updates, or removals |

Sample settings are partial patches:

| Field | Type | Accepted values |
|-------|------|-----------------|
| `loadType` | string | `DecompressOnLoad`, `CompressedInMemory`, `Streaming` |
| `compressionFormat` | string | One value from the corresponding `compressionFormats` array |
| `quality` | number | Finite value from `0` to `1` |
| `preloadAudioData` | bool | Preload policy, stored per default/platform sample settings |
| `sampleRateSetting` | string | `PreserveSampleRate`, `OptimizeSampleRate`, `OverrideSampleRate` |
| `sampleRateOverride` | integer | `1..192000` with `OverrideSampleRate`; otherwise `0` |
| `conversionMode` | integer | `0` only; Unity exposes the field but defines no non-zero public flags |

Changing `sampleRateSetting` away from `OverrideSampleRate` clears an omitted
`sampleRateOverride` to `0`. Supplying a non-zero override with either other mode is rejected.

Unity 6 makes preload policy part of sample settings rather than a global
`AudioImporter` property. Keeping it in the nested object is the contract common to
Unity 2022.3 and Unity 6 and also permits platform-specific preload overrides.

Every platform entry requires `platform` and the boolean `override`.
`override: true` also requires a non-empty `sampleSettings` object; it patches the
current effective settings and registers the result as an explicit override.
`override: false` forbids `sampleSettings` and clears the override. Clearing an
already inherited platform is an unchanged request.

The full request is validated before reimport. Unknown or duplicate fields, wrong JSON
types, unknown enums/platforms, duplicate platform entries, incompatible codecs, and
invalid ranges/combinations return `400` without reimporting. If Unity refuses one
staged platform override, every staged override is restored and the request fails.

### Response

The response has the same importer, platform, and `audioClip` fields as GET, followed
by:

```json
{
  "...": "...",
  "reimported": true,
  "diagnostics": [{
    "severity": "warning",
    "message": "Import message",
    "file": "Assets/Audio/theme.ogg",
    "line": 0
  }]
}
```

`diagnostics` contains warning and error entries from Unity's import log for the final
import. An unchanged request returns `reimported: false`, an empty diagnostics array,
and does not call `SaveAndReimport()`.

### Errors

| Status | Cause |
|--------|-------|
| 400 | Invalid request, unsupported setting combination, unknown platform, Unity refused an override, or the asset is not audio |
| 403 | Asset Write category is disabled |
| 404 | No asset found for the given GUID |
| 409 | The Unity Editor is in Play mode or a conflicting activity is active |
| 500 | Normalization could not be written, reimport threw, or the importer disappeared after reimport |

---

## GET /api/assets/model-importer/{guid}

Returns a versioned, normalized view of an asset's `ModelImporter` core settings and
its durable imported sub-assets. This Read endpoint is unavailable during asset updates.

```json
{
  "schemaVersion": 1,
  "guid": "a1b2c3...",
  "assetPath": "Assets/Models/robot.fbx",
  "capabilities": {
    "unityVersion": "6000.0.80f1",
    "useFileUnits": true,
    "tangentImport": true,
    "bakeIk": true,
    "settings": {
      "model.useFileUnits": true,
      "tangents.import": true,
      "clips.definitions": true,
      "clips.avatarMask": true,
      "clips.events": true,
      "clips.curves": false,
      "rig.humanDescription": false
    },
    "unavailableSettings": ["rig.humanDescription", "clips.curves"]
  },
  "settings": {
    "model": { "globalScale": 1.0, "fileScale": 1.0, "useFileScale": true, "useFileUnits": true, "bakeAxisConversion": false, "preserveHierarchy": false, "isReadable": false },
    "mesh": { "compression": "Off", "indexFormat": "Auto", "keepQuads": false, "weldVertices": true, "skinWeights": "Standard", "maxBonesPerVertex": 4, "minBoneWeight": 0.001, "optimizePolygons": true, "optimizeVertices": true },
    "geometry": { "addCollider": false, "importBlendShapes": true, "importCameras": true, "importLights": true, "importVisibility": true, "importConstraints": true, "swapUvChannels": false, "generateSecondaryUv": false, "secondaryUvMarginMethod": "Manual", "secondaryUvAngleDistortion": 8.0, "secondaryUvAreaDistortion": 15.0, "secondaryUvHardAngle": 88.0, "secondaryUvPackMargin": 4.0 },
    "normals": { "import": "Import", "blendShapeImport": "Calculate", "calculationMode": "AreaAndAngleWeighted", "smoothingSource": "PreferSmoothingGroups", "smoothingAngle": 60.0 },
    "tangents": { "import": "CalculateMikk" }
  },
  "subAssets": [{ "guid": "a1b2c3...", "localIdentifier": "4300000", "name": "Body", "type": "UnityEngine.Mesh" }]
}
```

`fileScale` is read-only. `subAssets` contains imported `Mesh`, `Material`, `Avatar`,
and `AnimationClip` objects for which `AssetDatabase.IsSubAsset` is true. Preview
objects are excluded. `localIdentifier` is a decimal string so clients do not lose
64-bit precision; `(guid, localIdentifier)` is the durable imported-object identity.

The same `settings` object also contains:

```json
{
  "materials": {
    "importMode": "ImportViaMaterialDescription",
    "location": "InPrefab",
    "naming": "BasedOnMaterialName",
    "search": "RecursiveUp"
  },
  "materialRemaps": [{
    "source": { "type": "UnityEngine.Material", "name": "Body" },
    "target": { "guid": "def456...", "localIdentifier": "2100000", "name": "RobotBody", "type": "UnityEngine.Material" }
  }],
  "rig": {
    "animationType": "Human",
    "avatarSetup": "CopyFromOther",
    "sourceAvatar": { "guid": "987abc...", "localIdentifier": "9000000", "name": "RobotAvatar", "type": "UnityEngine.Avatar" },
    "autoGenerateAvatarMappingIfUnspecified": false,
    "humanoidOversampling": "X2",
    "optimizeGameObjects": true,
    "extraExposedTransformPaths": ["Root/WeaponSocket"]
  },
  "clips": {
    "derivedFromDefaults": false,
    "definitions": [{
      "takeName": "Take 001",
      "name": "Idle",
      "firstFrame": 0.0,
      "lastFrame": 60.0,
      "wrapMode": "Default",
      "loop": false,
      "loopTime": true,
      "loopPose": true,
      "mirror": false,
      "lockRootRotation": true,
      "keepOriginalOrientation": true,
      "rotationOffset": 0.0,
      "lockRootHeightY": true,
      "keepOriginalPositionY": true,
      "heightFromFeet": false,
      "heightOffset": 0.0,
      "lockRootPositionXZ": true,
      "keepOriginalPositionXZ": true,
      "cycleOffset": 0.0,
      "hasAdditiveReferencePose": false,
      "additiveReferencePoseFrame": 0.0,
      "maskType": "CreateFromThisModel",
      "maskSource": null,
      "maskNeedsUpdating": false,
      "events": []
    }]
  },
  "unsupportedInitialSettings": ["rig.humanDescription", "clips.curves"]
}
```

`materialRemaps` is the Material subset of `GetExternalObjectMap()`. A missing target
is returned as null so clients can identify and remove stale remaps. `humanDescription`
is deliberately not writable through arbitrary serialized properties; its unsupported
status is explicit in every settings snapshot.
When `clipAnimations` is empty, `clips.definitions` is populated from
`defaultClipAnimations` and `derivedFromDefaults` is true. This separates “no stored
override array” from “no animation take.” Clip curves are unsupported in schema version 1.

---

## POST /api/assets/model-importer/{guid}/preflight

Validates the PATCH contract and reports `valid`, `reimportRequired`, `changedFields`,
and normalized `before` and `after` settings without mutation or import.

```json
{
  "schemaVersion": 1,
  "model": { "globalScale": 1.0, "isReadable": true },
  "normals": { "import": "Calculate" },
  "tangents": { "import": "CalculateMikk" }
}
```

`schemaVersion` is required and must be integer `1`. At least one non-empty settings
group is required. Groups are partial patches; omitted fields preserve current values.
Unknown or duplicate fields and wrong JSON types are rejected. Enum fields accept the
names GET returns, case-insensitively.

| Group | Writable fields |
|-------|-----------------|
| `model` | `globalScale` (`> 0`, `<= 100000`), `useFileScale`, `useFileUnits`, `bakeAxisConversion`, `preserveHierarchy`, `isReadable` |
| `mesh` | `compression`, `indexFormat`, `keepQuads`, `weldVertices`, `skinWeights`, `maxBonesPerVertex` (`1..255`), `minBoneWeight` (`0..1`), `optimizePolygons`, `optimizeVertices` |
| `geometry` | `addCollider`, `importBlendShapes`, `importCameras`, `importLights`, `importVisibility`, `importConstraints`, `swapUvChannels`, `generateSecondaryUv`, `secondaryUvMarginMethod`, `secondaryUvAngleDistortion` (`1..75`), `secondaryUvAreaDistortion` (`1..75`), `secondaryUvHardAngle` (`0..180`), `secondaryUvPackMargin` (`1..64`) |
| `normals` | `import`, `blendShapeImport`, `calculationMode`, `smoothingSource`, `smoothingAngle` (`0..180`) |
| `tangents` | `import` |
| `materials` | `importMode`, `location`, `naming`, `search` |
| `materialRemaps` | Array of `{source: {type: "UnityEngine.Material", name}, target}`; `target: null` removes the source remap |
| `rig` | `animationType`, `avatarSetup`, `sourceAvatar`, `autoGenerateAvatarMappingIfUnspecified`, `humanoidOversampling`, `optimizeGameObjects`, `extraExposedTransformPaths` |
| `clips` | Full ordered replacement array; each entry requires `takeName`, unique `name`, `firstFrame`, and `lastFrame` |

`useFileUnits` and tangent import are checked against source capabilities. Tangents
must be `None` when normals are `None`.

Material and Avatar targets use `{guid, localIdentifier}`. `localIdentifier` may be
omitted only when the referenced asset contains exactly one object of the required
type. Missing, wrong-type, and ambiguous targets reject the complete request before
any setting or remap changes. Repeating a remap source in one request is also rejected.

Material naming/search fields require material import and are incompatible with
`location: InPrefab`; adding or replacing remaps requires an import mode other than `None`.
Removing a stale remap remains allowed. `CopyFromOther`
requires a valid compatible source Avatar. `None` and `Legacy` rigs require `NoAvatar`;
humanoid oversampling is Human-only, automatic mapping additionally requires
`CreateFromThisModel`, and exposed transform paths require optimization. Incompatible
fields are rejected rather than ignored.

### Imported clip definitions

`clips` replaces `ModelImporter.clipAnimations` as one ordered array. Sending `[]`
removes the stored array, so the next read is default-derived. Each definition starts
from the same stored `(takeName, name)` definition when one exists, or otherwise from
the named default take. Omitted optional fields retain that baseline.

Optional fields are `wrapMode`, `loop`, `loopTime`, `loopPose`, `mirror`,
`lockRootRotation`, `keepOriginalOrientation`, `rotationOffset`, `lockRootHeightY`,
`keepOriginalPositionY`, `heightFromFeet`, `heightOffset`, `lockRootPositionXZ`,
`keepOriginalPositionXZ`, `cycleOffset`, `hasAdditiveReferencePose`,
`additiveReferencePoseFrame`, `maskType`, `maskSource`, and `events`.

The take must exist in `defaultClipAnimations`, and the finite frame range must be
ordered and stay inside that take. Clip names are unique across the replacement.
`loopPose` requires `loopTime`; an additive reference frame requires the additive mode
and must lie in the clip range; mirror is Human-only.
`maskType: CopyFromOther` requires an `AvatarMask` `maskSource`, while other mask types
require null. Mask and event object references use the same GUID/local-identifier rules
as Material and Avatar references.

`events` is an ordered full replacement per definition. Every event requires finite,
non-negative `time` and non-empty `functionName`; optional fields are `stringParameter`,
`floatParameter`, `intParameter`, `objectReferenceParameter`, and `messageOptions`
(`DontRequireReceiver` or `RequireReceiver`). Unknown nested fields reject the whole
array before mutation.

---

## PATCH /api/assets/model-importer/{guid}

Applies the preflight contract and calls `SaveAndReimport()` at most once. An unchanged
patch returns `reimported: false`. A changed response includes complete settings and
sub-assets under `before` and `after`, `subAssetDelta`, `diagnostics`, and `rollback`.
If reimport throws, UnionAir attempts to restore the original settings and returns the
structured rollback result with `500`.

| Status | Cause |
|--------|-------|
| 400 | Invalid schema, field, type, range, enum, combination, capability, or non-model asset |
| 403 | Asset Write category is disabled |
| 404 | No asset found for the GUID |
| 409 | Play mode, conflicting activity, loaded-scene conflict, or non-editable importer |
| 500 | Reimport failed or the importer disappeared after reimport |

---
