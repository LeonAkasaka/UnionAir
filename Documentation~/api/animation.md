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
| `type` | ✅ | C# type name (e.g. `Transform`, `UnityEngine.UI.Image`). Short names such as `Image` are also accepted |
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

### Request Body (JSON)

```json
{
  "bindings": [
    { "relativePath": "Hips", "type": "Transform", "property": "localPosition.y" },
    { "relativePath": "", "type": "UnityEngine.UI.Image", "property": "m_Sprite" }
  ]
}
```

### Response

```json
{
  "removed": ["localPosition.y", "m_Sprite"],
  "errors": []
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `bindings` is missing or empty, or a binding entry is malformed |
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
          "motion": { "guid": "d4e5f6...", "name": "IdleClip" },
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
