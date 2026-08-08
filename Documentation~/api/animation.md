# API Reference — Animation
**English** | [日本語](animation.ja.md)

Base URL: `http://localhost:<port>/api/`, read from `<project>/.unionair/endpoint.txt` at connection time. See the [API Reference index](../api-reference.md) for endpoint discovery, response conventions, and category/security notes.

---

## Undo

Some animation writes can be taken back with Ctrl+Z in the Editor and some cannot. The boundary is deliberate rather than incidental, so it is stated here once rather than per endpoint. Measured on Unity 6000.0.80f1.

| Write | Undoable |
|---|---|
| AnimatorController structure — parameters, layers, states, transitions | ✅ |
| AnimationClip curves — `POST` and `DELETE .../curves` | ❌ |
| Asset creation — `POST /api/assets/animation-clips`, `POST /api/assets/animator-controllers` | ❌ |

**Controller writes are undoable and UnionAir does nothing to make them so.** The `UnityEditor.Animations` editing APIs register their own undo, so a request that adds a state is taken back by one Ctrl+Z. UnionAir adds no registration of its own on these paths, because a second one is redundant.

**Clip curve writes are not undoable, by choice.** These APIs register nothing, and UnionAir does not register on their behalf. An asset write here is saved to disk before the response is sent, so a `200` means the file on disk already changed — recovery belongs to version control rather than to the undo stack. Registering undo would let Ctrl+Z revert the asset in memory while the file kept the written content until some later, unrelated save, leaving a state that is neither before nor after.

**Asset creation is not undoable in Unity itself**, and UnionAir does not change that. Delete the asset to reverse a create.

Scene writes are a different matter and are undoable; see [`api/gameobjects.md`](gameobjects.md).

---

## POST /api/assets/animation-clips

Creates an AnimationClip asset.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "frameRate": 60,
  "wrapMode": "Loop"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `assetPath` | ✅ | Destination path (must end with `.anim`) |
| `frameRate` | ❌ | Samples per second (default: Unity default, typically 60) |
| `wrapMode` | ❌ | `Once`, `Loop`, `PingPong`, `ClampForever`, or `Default` |

### Response (HTTP 201)

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "guid": "a1b2c3...",
  "frameRate": 60.0,
  "length": 0.0
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `assetPath` is missing or does not end with `.anim` |
| 403 | Asset Write category is disabled |

---

## GET /api/assets/animation-clips/{guid}

Returns AnimationClip metadata together with all float curves and object reference curves.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimationClip asset |

### Response

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "guid": "a1b2c3...",
  "frameRate": 60.0,
  "length": 1.0,
  "wrapMode": "Loop",
  "curveCount": 1,
  "curves": [
    {
      "relativePath": "Hips",
      "type": "Transform",
      "property": "localPosition.y",
      "keyCount": 3,
      "keys": [
        { "time": 0.0, "value": 0.0, "inTangent": 0.0, "outTangent": 1.0 },
        { "time": 0.5, "value": 1.0, "inTangent": 0.0, "outTangent": 0.0 },
        { "time": 1.0, "value": 0.0, "inTangent": -1.0, "outTangent": 0.0 }
      ]
    }
  ],
  "objectReferenceCurveCount": 1,
  "objectReferenceCurves": [
    {
      "relativePath": "",
      "type": "Image",
      "property": "m_Sprite",
      "keys": [
        { "time": 0.0, "guid": "a1b2c3...", "name": "sprite_01" },
        { "time": 0.1667, "guid": "d4e5f6...", "name": "sprite_02" }
      ]
    }
  ]
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | Asset is not an AnimationClip |
| 404 | No asset found for the given GUID |

---

## POST /api/assets/animation-clips/{guid}/curves

Adds or replaces float curves and/or object reference curves on an AnimationClip. At least one of `curves` or `objectReferenceCurves` must be provided.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimationClip asset |

### Request Body — float curves

```json
{
  "curves": [
    {
      "relativePath": "Hips",
      "type": "Transform",
      "property": "localPosition.y",
      "keys": [
        { "time": 0.0, "value": 0.0, "inTangent": 0.0, "outTangent": 1.0 },
        { "time": 0.5, "value": 1.0, "inTangent": 0.0, "outTangent": 0.0 },
        { "time": 1.0, "value": 0.0, "inTangent": -1.0, "outTangent": 0.0 }
      ]
    }
  ]
}
```

### Request Body — object reference curves (e.g. Sprite swap)

```json
{
  "objectReferenceCurves": [
    {
      "relativePath": "",
      "type": "UnityEngine.UI.Image",
      "property": "m_Sprite",
      "keys": [
        { "time": 0.0,    "guid": "a1b2c3..." },
        { "time": 0.1667, "guid": "d4e5f6..." }
      ]
    }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `relativePath` | ✅ | Child path relative to the Animator's GameObject. Use `""` for the Animator's own GameObject |
| `type` | ✅ | C# type name, short or fully qualified: `Transform` and `UnityEngine.Transform` both resolve, as do `Image` and `UnityEngine.UI.Image`. The type must derive from `UnityEngine.Object`; anything else answers `Unknown type` |
| `property` | ✅ | Serialized property path (e.g. `localPosition.y`, `m_Sprite`) |
| `keys[].time` | ✅ | Time in seconds |
| `keys[].value` | ✅ (float curves) | Float value |
| `keys[].inTangent` / `outTangent` | ❌ | Tangents (default: 0) |
| `keys[].guid` | ✅ (object ref) | GUID of the referenced asset. For Sprite-mode textures, the Sprite sub-asset is loaded automatically |

> Both `curves` and `objectReferenceCurves` can be provided in the same request.

### Response

```json
{
  "added": ["localPosition.y", "m_Sprite"],
  "addedFloat": ["localPosition.y"],
  "addedObjectReference": ["m_Sprite"],
  "errors": []
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | Required curve fields missing, unknown type, or no valid curves provided |
| 404 | No asset found for the given GUID |
| 403 | Asset Write category is disabled |

---

## DELETE /api/assets/animation-clips/{guid}/curves

Removes curves from an AnimationClip by binding. Works for both float curves and object reference curves.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimationClip asset |

### `property` uses the name `GET` returns

**This is not always the name you wrote**, because adding and removing go through different Unity APIs.

`POST .../curves` writes through `AnimationClip.SetCurve`, which expands a Transform vector property into all of its components. A curve written on `localPosition.y` is stored as three bindings — `m_LocalPosition.x`, `.y`, and `.z` — and the components you did not ask for are filled with that property's default value, held constant for the length of the curve: `0` for position, `1` for scale. Animating one axis therefore pins the other two.

The expansion belongs to `SetCurve`, not to the shorthand: passing the serialized name `m_LocalPosition.y` expands identically. It applies to Transform's position, scale, and euler angles; a scalar such as `Light.m_Intensity`, and a single colour channel such as `Light.m_Color.r`, are each stored as one binding.

`DELETE .../curves` removes through `AnimationUtility.SetEditorCurve`, which is exact: one entry addresses one binding. So removing `m_LocalPosition.y` leaves `.x` and `.z` in place, and removing a whole expanded property means listing each component.

A `property` that matches no binding on the clip is reported in `errors`, and the message lists the property names that are bound at that path and type, so the correct name can be read off the failure.

### Request Body (JSON)

```json
{
  "bindings": [
    { "relativePath": "Hips", "type": "Transform", "property": "m_LocalPosition.y" },
    { "relativePath": "", "type": "UnityEngine.UI.Image", "property": "m_Sprite" }
  ]
}
```

### Response

```json
{
  "removed": ["m_LocalPosition.y", "m_Sprite"],
  "errors": []
}
```

`removed` lists only bindings that were present before the call and absent after it. A binding that could not be removed is reported in `errors` instead. A binding listed more than once in the same request is removed and reported once -- an entry names a curve, so repeating it does not remove a second one.

```json
{
  "removed": [],
  "errors": [
    "No curve bound to 'localPosition.y' on 'Hips' (Transform). Bindings there: m_LocalPosition.x, m_LocalPosition.y, m_LocalPosition.z"
  ]
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `bindings` is missing or empty, or nothing was removed and at least one binding failed. A request that removed at least one binding answers `200` even when other entries failed, with the failures in `errors` |
| 404 | No asset found for the given GUID |
| 403 | Asset Write category is disabled |

---

## POST /api/assets/animator-controllers

Creates an AnimatorController asset with a default Base Layer.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{ "assetPath": "Assets/Animations/Character.controller" }
```

| Field | Required | Description |
|-------|----------|-------------|
| `assetPath` | ✅ | Destination path (must end with `.controller`) |

### Response (HTTP 201)

```json
{
  "assetPath": "Assets/Animations/Character.controller",
  "guid": "a1b2c3...",
  "layerCount": 1
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `assetPath` is missing or does not end with `.controller` |
| 403 | Asset Write category is disabled |

---

## GET /api/assets/animator-controllers/{guid}

Returns the full AnimatorController structure: parameters, layers, states, transitions, and any-state transitions.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Response

```json
{
  "assetPath": "Assets/Animations/Character.controller",
  "guid": "a1b2c3...",
  "parameters": [
    { "name": "Speed", "type": "Float", "defaultFloat": 0.0 },
    { "name": "IsGrounded", "type": "Bool", "defaultBool": false }
  ],
  "layers": [
    {
      "name": "Base Layer",
      "index": 0,
      "defaultWeight": 0.0,
      "isBaseLayer": true,
      "blendingMode": "Override",
      "avatarMask": null,
      "iKPass": false,
      "syncedLayerIndex": -1,
      "syncedLayerAffectsTiming": false,
      "states": [
        {
          "name": "Idle",
          "speed": 1.0,
          "isDefault": true,
          "motion": {
            "type": "AnimationClip",
            "guid": "d4e5f6...",
            "name": "IdleClip",
            "assetPath": "Assets/Animations/Idle.anim",
            "clipsAtPath": 1
          },
          "transitions": [
            {
              "to": "Walk",
              "hasExitTime": false,
              "exitTime": 0.0,
              "duration": 0.25,
              "conditions": [
                { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
              ]
            }
          ]
        }
      ],
      "anyStateTransitions": []
    }
  ]
}
```

### Motion

Every `motion` carries a `type`, and a state with no motion has `"motion": null`.

| `type` | Meaning |
|--------|---------|
| `AnimationClip` | The motion is a clip. `guid` addresses the asset holding it, which is the clip itself only when `clipsAtPath` is `1`. |
| `BlendTree` | The motion is a blend tree owned by this controller. `guid` is always `null`. |
| `Unknown` | A `Motion` subclass this version does not describe. Reported rather than presented as one of the above. `guid` is non-null only when the motion is the main asset at its path. |

A motion asset that has been deleted reports `"motion": null`, not `Unknown` — Unity resolves a missing reference to null before the type can be examined.

#### AnimationClip

| Field | Description |
|-------|-------------|
| `guid` | GUID of the asset holding the clip, or `null` when the clip is not saved |
| `name` | Clip name |
| `assetPath` | Path of the asset holding the clip, or `null` when the clip is not saved |
| `clipsAtPath` | Number of AnimationClips reachable at `assetPath`. **Absent** when `assetPath` is `null`, since there is no path to count at |

`clipsAtPath` is how precise `guid` is. A clip imported from a model file lives inside that file, so the GUID identifies the **file**, not the clip. When `clipsAtPath` is `1` the GUID is unambiguous. When it is greater than `1`, `GET /api/assets/animation-clips/{guid}` returns whichever clip the importer lists first, and the other takes cannot be addressed by GUID at all.

#### BlendTree

A blend tree is a sub-asset of the controller and has no GUID, so its structure is serialized inline instead of being fetched separately.

| Field | Description |
|-------|-------------|
| `blendType` | `Simple1D`, `SimpleDirectional2D`, `FreeformDirectional2D`, `FreeformCartesian2D`, or `Direct` |
| `blendParameter` | Parameter driving the blend, or its X axis for 2D types |
| `blendParameterY` | Parameter driving the Y axis. Consulted by the 2D types only |
| `useAutomaticThresholds` | Whether Unity recomputes child thresholds. Consulted by `Simple1D` only |
| `minThreshold`, `maxThreshold` | Threshold range. Consulted by `Simple1D` only |
| `children` | Child motions, in order |

Every field in the table above is present on every blend tree, whatever its `blendType`, because Unity stores them all regardless of which the blend consults -- `blendParameterY` and the threshold fields included, and the same for a child's `directBlendParameter`, which only `Direct` uses. "Consulted by" says which blend types read a field, not which ones report it. The one case where fields are absent is a tree at the depth cap, which carries `truncated` instead.

Each child carries `threshold`, `position` (`{x, y}`, used by the 2D types), `timeScale`, `cycleOffset`, `mirror`, `directBlendParameter`, and a `motion` of exactly the shape above — so a nested blend tree is described like any other.

```json
"motion": {
  "type": "BlendTree",
  "guid": null,
  "name": "Locomotion",
  "blendType": "Simple1D",
  "blendParameter": "Speed",
  "blendParameterY": "",
  "useAutomaticThresholds": true,
  "minThreshold": 0.0,
  "maxThreshold": 0.8,
  "children": [
    {
      "threshold": 0.0,
      "position": { "x": 0.0, "y": 0.0 },
      "timeScale": 1.0,
      "cycleOffset": 0.0,
      "mirror": false,
      "directBlendParameter": "",
      "motion": { "type": "AnimationClip", "guid": "...", "name": "Walk", "assetPath": "...", "clipsAtPath": 1 }
    }
  ]
}
```

Nesting is serialized to a depth of 10. A blend tree at that depth is reported with `"truncated": true` and **no** `children`, so a boundary is distinguishable from a leaf; an empty `children` array keeps meaning what it says, since a blend tree may genuinely have none.

### Not described by this response

Sub-state machines are not enumerated. Only the states directly on each layer's root state machine appear, so a layer whose states live inside a sub-state machine reports an empty `states` array.

### Errors

| Status | Cause |
|--------|-------|
| 400 | Asset is not an AnimatorController |
| 404 | No asset found for the given GUID |

---

## POST /api/assets/animator-controllers/{guid}/parameters

Adds a parameter to an AnimatorController. If a parameter with the same name already exists, it is replaced.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{ "name": "Speed", "type": "Float", "defaultValue": 0.0 }
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | ✅ | Parameter name |
| `type` | ✅ | `Float`, `Int`, `Bool`, or `Trigger` |
| `defaultValue` | ❌ | Default value (Float/Int/Bool only) |

### Response (HTTP 201)

```json
{ "added": "Speed", "type": "Float" }
```

---

## DELETE /api/assets/animator-controllers/{guid}/parameters

Removes a parameter from an AnimatorController by name.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{ "name": "Speed" }
```

### Response

```json
{ "removed": "Speed" }
```

### Errors

| Status | Cause |
|--------|-------|
| 404 | Parameter not found |

---

## Layer fields

Layers are addressed by `layerIndex`, never by name: Unity does not enforce unique layer names, and the state and transition endpoints already address layers by index.

| Field | Description |
|-------|-------------|
| `name` | Layer name. Not unique, and not an address |
| `index` | Position in the controller |
| `defaultWeight` | `AnimatorControllerLayer.defaultWeight`, verbatim. **Not the weight in effect on the base layer**, and **not clamped** — see below |
| `isBaseLayer` | True for layer 0 |
| `blendingMode` | `Override` or `Additive` |
| `avatarMask` | `null`, or `{guid, name}` of an `AvatarMask` asset. Unlike a blend tree, a mask is an ordinary asset and the GUID is fetchable |
| `iKPass` | Whether the layer runs an IK pass |
| `syncedLayerIndex` | Index of the layer this one takes its state machine from, or `-1` when not synced |
| `syncedLayerAffectsTiming` | Only consulted when the layer is synced |

### `defaultWeight` on the base layer

For layer 0, `defaultWeight` is not the weight in effect. The base layer runs at 1 whatever the field holds, and the Animator window shows no weight slider for it — a freshly created controller reports `"defaultWeight": 0` on a layer that is fully active. The field is a faithful reading of the serialized value, and `isBaseLayer` is what tells a client the value is not consulted, without the client having to know Unity's rule.

There is deliberately no `effectiveWeight`. Runtime weight belongs to a live `Animator`, not to the asset, and computing it here would be a guess presented as a reading.

### `defaultWeight` is not clamped

The meaningful range is 0 to 1, and nothing enforces it. Measured on 6000.0.80f1, Unity stores `5` and `-2` verbatim and reads them back unchanged, so this endpoint does not refuse them either — refusing would make the API narrower than the asset and than the Inspector's own data model, which is the same reason there is no `effectiveWeight`. A value outside 0–1 round-trips; what it does at runtime is Unity's business.

---

## POST /api/assets/animator-controllers/{guid}/layers

Adds a layer to an AnimatorController. Every setting `PATCH` accepts may be supplied here, so a masked layer takes one request rather than a create followed by a patch.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{ "name": "Arms", "defaultWeight": 1.0, "avatarMask": { "guid": "a1b2c3..." } }
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | ✅ | Layer name |
| `defaultWeight` | ❌ | Default layer weight. Meaningful over 0–1; **not clamped** — see below. Additional layers default to 0 |
| `weight` | ❌ | Accepted as a synonym for `defaultWeight` |
| `blendingMode` | ❌ | `Override` or `Additive` |
| `avatarMask` | ❌ | `{guid}` of an `AvatarMask` asset |
| `iKPass` | ❌ | Whether the layer runs an IK pass |
| `syncedLayerIndex` | ❌ | See `PATCH` for the values accepted |
| `syncedLayerAffectsTiming` | ❌ | Only meaningful on a synced layer |

### Response (HTTP 201)

```json
{ "added": "Arms", "layerIndex": 1, "applied": ["defaultWeight", "avatarMask"] }
```

`applied` names the settings that were set. A rejected setting answers `400` and **the layer is not created** — the create is taken back rather than leaving a layer that is half what was asked for.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `name` is missing, or a setting is invalid |
| 404 | No asset found for the given GUID, or `avatarMask.guid` names no `AvatarMask` |
| 403 | Asset Write category is disabled |

---

## PATCH /api/assets/animator-controllers/{guid}/layers

Updates one layer. Every field except `layerIndex` is optional, and **an omitted field is left unchanged**.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{ "layerIndex": 1, "defaultWeight": 0.5, "avatarMask": null }
```

| Field | Required | Description |
|-------|----------|-------------|
| `layerIndex` | ✅ | Layer to update |
| `name`, `defaultWeight`, `weight`, `blendingMode`, `iKPass`, `syncedLayerAffectsTiming` | ❌ | Set when present |
| `avatarMask` | ❌ | `{guid}` to set, explicit `null` to clear. Omitting it leaves the mask alone — `null` and absent mean different things here |
| `syncedLayerIndex` | ❌ | `-1` for no sync, or another layer's index |

### Response

```json
{ "layerIndex": 1, "applied": ["defaultWeight", "avatarMask"] }
```

### `syncedLayerIndex` is checked before it reaches Unity

An illegal value is rejected with `400` rather than passed through, because Unity answers one by damaging the controller rather than by refusing it. Measured on 6000.0.80f1: pointing a layer at **itself** removed a layer from the controller silently — three layers became two, with no error — and assigning **one index past the last layer** crashed the Editor. The out-of-range case is therefore bounded from the legal side rather than characterised further, since reproducing it costs an Editor session.

Accepted: `-1`, or `0` to `layerCount - 1` other than the layer's own index.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `layerIndex` missing or out of range, an invalid `syncedLayerIndex`, an unknown `blendingMode`, or an `avatarMask` that is neither an object nor `null` |
| 404 | No asset found for the given GUID, or `avatarMask.guid` names no `AvatarMask` |
| 403 | Asset Write category is disabled |

---

## DELETE /api/assets/animator-controllers/{guid}/layers

Removes one layer. The layer's `AnimatorStateMachine` is a sub-asset of the controller and is destroyed with it.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{ "layerIndex": 1 }
```

### Response

```json
{ "removed": "Arms", "layerIndex": 1, "layerCount": 1 }
```

### Layer 0 cannot be deleted

`AnimatorController.RemoveLayer(0)` does not refuse. Measured on 6000.0.80f1 it removes the base layer and promotes the next one, and on a single-layer controller it leaves a controller with **zero layers**, which no other endpoint can repair. The request answers `400` instead.

### A layer that another layer syncs to cannot be deleted

Removing a layer shifts every higher index down by one, and nothing fixes up a `syncedLayerIndex` that pointed at or above it — a reference can end up naming the wrong layer, or the layer itself, which is the case measured to remove a layer silently. Such a request answers `400` naming the layer in the way; clear that layer's `syncedLayerIndex` first.

Deleting a layer that is *itself* synced is fine. Its sync is cleared before removal, because `RemoveLayer` does not destroy the state machine of a synced layer and would otherwise leave it in the asset with no layer referring to it.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `layerIndex` missing, out of range, `0`, or a layer another layer syncs to |
| 404 | No asset found for the given GUID |
| 403 | Asset Write category is disabled |

---

## POST /api/assets/animator-controllers/{guid}/states

Adds a state to a layer of an AnimatorController.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{
  "name": "Walk",
  "layerIndex": 0,
  "motion": { "guid": "d4e5f6..." },
  "speed": 1.0,
  "setAsDefault": false
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | ✅ | State name |
| `layerIndex` | ❌ | Target layer index (default: 0) |
| `motion` | ❌ | Object with `guid` referencing an AnimationClip asset |
| `speed` | ❌ | Playback speed (default: 1.0) |
| `setAsDefault` | ❌ | If `true`, sets this state as the layer's default (entry) state |

### Response (HTTP 201)

```json
{ "added": "Walk", "layerIndex": 0, "isDefault": false }
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `name` is missing, `layerIndex` is out of range, or motion GUID is not found |

---

## PATCH /api/assets/animator-controllers/{guid}/states

Updates an existing state in an AnimatorController.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{
  "name": "Walk",
  "layerIndex": 0,
  "newName": "Run",
  "motion": { "guid": "e5f6a7..." },
  "speed": 1.5,
  "setAsDefault": true
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | ✅ | Current state name (used to identify the state) |
| `layerIndex` | ❌ | Layer index (default: 0) |
| `newName` | ❌ | New name for the state |
| `motion` | ❌ | Replace the assigned motion clip |
| `speed` | ❌ | Playback speed |
| `setAsDefault` | ❌ | Set this state as the layer default |

### Response

```json
{ "updated": "Run", "layerIndex": 0 }
```

### Errors

| Status | Cause |
|--------|-------|
| 404 | State not found |

---

## DELETE /api/assets/animator-controllers/{guid}/states

Removes a state from an AnimatorController layer.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{ "name": "Walk", "layerIndex": 0 }
```

### Response

```json
{ "removed": "Walk", "layerIndex": 0 }
```

### Errors

| Status | Cause |
|--------|-------|
| 404 | State not found |

---

## POST /api/assets/animator-controllers/{guid}/transitions

Adds a transition between states in an AnimatorController.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

Use `"AnyState"` as `from` for any-state transitions. Use `"Exit"` as `to` for exit transitions. `"AnyState"` → `"Exit"` is not a valid combination.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0,
  "hasExitTime": false,
  "duration": 0.25,
  "offset": 0.0,
  "conditions": [
    { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `from` | ✅ | Source state name, or `"AnyState"` |
| `to` | ✅ | Destination state name, or `"Exit"` |
| `layerIndex` | ❌ | Layer index (default: 0) |
| `hasExitTime` | ❌ | Whether the transition has an exit time trigger |
| `exitTime` | ❌ | Normalized time at which exit time triggers (when `hasExitTime: true`) |
| `duration` | ❌ | Transition blend duration in seconds |
| `offset` | ❌ | Normalized time offset in the destination state |
| `conditions` | ❌ | Array of condition objects |

**Condition modes:** `If`, `IfNot` (Bool/Trigger), `Greater`, `Less`, `Equals`, `NotEqual` (Float/Int)

### Response (HTTP 201)

```json
{ "added": true, "from": "Idle", "to": "Walk", "layerIndex": 0 }
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `from` or `to` is missing, or `"AnyState"` → `"Exit"` was requested |
| 404 | Source or destination state not found |

---

## PATCH /api/assets/animator-controllers/{guid}/transitions

Updates an existing transition. The transition is identified by the `from` and `to` state names.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0,
  "duration": 0.1,
  "conditions": [
    { "parameter": "Speed", "mode": "Greater", "threshold": 0.5 }
  ]
}
```

All fields except `from` and `to` are optional; only provided fields are updated.

### Response

```json
{ "updated": true, "from": "Idle", "to": "Walk", "layerIndex": 0 }
```

### Errors

| Status | Cause |
|--------|-------|
| 404 | Transition not found |

---

## DELETE /api/assets/animator-controllers/{guid}/transitions

Removes a transition from an AnimatorController. The transition is identified by the `from` and `to` state names.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{ "from": "Idle", "to": "Walk", "layerIndex": 0 }
```

### Response

```json
{ "removed": true, "from": "Idle", "to": "Walk", "layerIndex": 0 }
```

### Errors

| Status | Cause |
|--------|-------|
| 404 | Transition not found |

---
