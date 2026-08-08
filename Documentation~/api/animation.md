# API Reference — Animation
**English** | [日本語](animation.ja.md)

Base URL: `http://localhost:<port>/api/`, read from `<project>/.unionair/endpoint.txt` at connection time. See the [API Reference index](../api-reference.md) for endpoint discovery, response conventions, and category/security notes.

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
      "weight": 1.0,
      "blendingMode": "Override",
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

## POST /api/assets/animator-controllers/{guid}/layers

Adds a layer to an AnimatorController.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{ "name": "Arms", "weight": 1.0 }
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | ✅ | Layer name |
| `weight` | ❌ | Default layer weight (0–1). Additional layers default to 0 |

### Response (HTTP 201)

```json
{ "added": "Arms", "layerIndex": 1 }
```

---

## Blend trees

A blend tree has no GUID. It is a sub-asset owned by the controller, so it is addressed by where it sits:

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [1] }
```

| Field | Description |
|-------|-------------|
| `layerIndex` | Layer holding the state. Defaults to `0` |
| `state` | Name of the state whose motion is the root blend tree |
| `childPath` | Child indices from that root. `[]` or omitted means the root itself, `[1]` the second child, `[1, 0]` the first child of that |

### `childPath` is positional

Removing or reordering children invalidates any path a client is holding. That is a property of the asset rather than a choice made here: Unity gives a `ChildMotion` no identity beyond its index — there is no name to key on and no id to preserve, and inventing one would mean maintaining a mapping the `.controller` file does not store.

A `childPath` that does not resolve answers `404` naming the index and the depth that failed, so a stale path reports where it went wrong rather than failing blankly.

---

## POST /api/assets/animator-controllers/{guid}/blend-trees

Creates a blend tree as the motion of an existing state, or adds a child to one.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Creating the root tree

Without `addChild`, the request creates the state's root blend tree:

```json
{ "layerIndex": 0, "state": "Locomotion", "name": "Locomotion",
  "blendType": "Simple1D", "blendParameter": "Speed",
  "useAutomaticThresholds": false, "minThreshold": 0, "maxThreshold": 0.8 }
```

A state that already holds a blend tree answers `409`; delete it first, or use `addChild`.

### Adding a child

With `addChild`, the request appends to the tree at `childPath` — a nested blend tree by default, or a clip when `motion` carries a GUID:

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [], "addChild": true,
  "name": "Runs", "blendType": "Simple1D", "blendParameter": "Direction", "threshold": 0.8 }
```

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [0], "addChild": true,
  "motion": { "guid": "a1b2c3..." }, "threshold": -1 }
```

A nested tree is created only through `addChild`; there is no tree literal to pass, so there is exactly one way to bring a sub-asset into existence.

### Fields

| Field | Applies to | Description |
|-------|-----------|-------------|
| `name`, `blendType`, `blendParameter`, `blendParameterY`, `useAutomaticThresholds`, `minThreshold`, `maxThreshold` | the tree | `blendType` is one of `Simple1D`, `SimpleDirectional2D`, `FreeformDirectional2D`, `FreeformCartesian2D`, `Direct` |
| `threshold`, `position`, `timeScale`, `cycleOffset`, `mirror`, `directBlendParameter` | the child entry | Only with `addChild` |
| `motion` | the child entry | `{guid}` of an `AnimationClip`. Its absence is what makes the child a nested tree |

### Response (HTTP 201)

```json
{ "created": "BlendTree", "layerIndex": 0, "state": "Locomotion",
  "childPath": [1], "name": "Runs", "ignored": [] }
```

`childPath` is where the new tree or child ended up, ready to address in the next request.

---

## PATCH /api/assets/animator-controllers/{guid}/blend-trees

Updates the addressed tree, and the addressed child entry when `childPath` is non-empty.

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [1], "threshold": 0.8 }
```

Tree fields and child fields are the same as for `POST`, plus `motion` to swap what a child holds.

### A child does not have to be a blend tree

`childPath` may address a child holding a clip. The child fields — `threshold`, `position`, `timeScale`, `cycleOffset`, `mirror`, `directBlendParameter`, `motion` — belong to the entry in the parent, not to what the entry holds, so they apply either way. Most children of a real tree hold a clip.

The tree fields do not. Sending one for a child that holds a clip answers `400` rather than being dropped.

| Request | Result |
|---|---|
| `threshold` with an empty `childPath` | `400` — the root tree is not a child of anything |
| a tree field on a child holding a clip | `400` naming the mismatch |
| `motion` together with a tree field | `400` — the tree fields would be written to a tree the same request discards |

### `motion` destroys what it displaces

Swapping a child's motion drops whatever was there. If that was a blend tree, Unity leaves it in the asset exactly as it does for a removed child, so the subtree is destroyed here — the same handling `DELETE` of a child needs.

### A failed request applies nothing

Every value is resolved against the controller before the first write — tree fields and child fields alike — so a request that sets several fields and fails on one leaves the tree exactly as it was. Setting `name` and an unknown `blendParameter` in the same request changes neither, and a `POST` with `addChild` that fails on a child field adds no child and no sub-asset.

---

## DELETE /api/assets/animator-controllers/{guid}/blend-trees

Removes the addressed tree or child.

```json
{ "layerIndex": 0, "state": "Locomotion", "childPath": [1] }
```

An empty or omitted `childPath` clears the state's motion. A non-empty one removes that child.

```json
{ "removed": "child", "layerIndex": 0, "state": "Locomotion",
  "childPath": [1], "destroyedSubTrees": 2 }
```

`destroyedSubTrees` counts the blend trees destroyed with the child, which is why the field exists — see below.

---

## What happens to the sub-assets

A blend tree lives inside the `.controller` file, so removing one from the graph is not the same as removing it from the asset. Measured on Unity 6000.0.80f1, against the file rather than against the API's own read:

| Operation | Unity's behaviour | What UnionAir does |
|---|---|---|
| Clearing a state's motion | Destroys the tree **and every descendant** | Nothing extra. Adding cleanup here would be code with nothing to do |
| `DELETE` of a child | Detaches the entry and **leaves the whole subtree in the file** | Collects the subtree before removing the entry, then destroys it |
| `DELETE .../states` on a state owning a tree | Destroys the state's own tree but **not its descendants** | Collects the subtree first and destroys whatever survives. Reported as `destroyedBlendTrees` |

The third row is why `DELETE .../states` now reports a count. A flat blend tree is cleaned up correctly by Unity, so the leak only appears once a tree is nested — a test that built a one-level tree would have reported success.

Created sub-assets carry `HideFlags.HideInHierarchy`, matching what the Animator window produces. `BlendTree.CreateBlendTreeChild` sets it already; a root tree created on an existing state has it set explicitly, because the only route to one is `AssetDatabase.AddObjectToAsset` and that route does not.

---

## Validation

- `blendParameter` and `blendParameterY` must name an existing `Float` parameter on the controller. A tree pointing at a parameter that does not exist is a broken controller that the read cannot tell from a working one, so it answers `400` rather than being stored.
- An unknown `blendType` answers `400` naming the accepted values.
- A `childPath` that does not resolve answers `404`.

### Fields that are stored but not consulted

Some fields are meaningful only for some blend types. They are stored — Unity stores them too, and the read reports them, so refusing would make the API narrower than the asset — and named in `ignored` so they never pass silently:

```json
{ "created": "AnimationClip", "childPath": [1, 0], "ignored": [
  "position is stored but not consulted: the parent blendType is Simple1D, and position applies to the 2D types.",
  "threshold is not kept because the parent has useAutomaticThresholds true; Unity recomputes it. Set the parent's useAutomaticThresholds to false to keep a threshold."
] }
```

A child's `position`, `directBlendParameter`, and `threshold` are judged against the **parent**: the child does not decide whether they are read, the blend its parent performs does.

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
