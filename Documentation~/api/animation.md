# API Reference — Animation
**English** | [日本語](animation.ja.md)

Base URL: `http://localhost:<port>/api/`, read from `<project>/.unionair/endpoint.txt` at connection time. See the [API Reference index](../api-reference.md) for endpoint discovery, response conventions, and category/security notes.

---

## Undo

Some animation writes can be taken back with Ctrl+Z in the Editor and some cannot. The boundary is deliberate rather than incidental, so it is stated here once rather than per endpoint. Measured on Unity 6000.0.80f1.

| Write | Undoable |
|---|---|
| AnimatorController structure — parameters, layers, states, transitions, blend trees, state machines, state machine transitions | ✅ |
| AnimationClip contents — `.../curves`, `settings` on `PATCH .../animation-clips/{guid}`, and `.../events` | ❌ |
| Asset creation — `POST /api/assets/animation-clips`, `POST /api/assets/animator-controllers` | ❌ |

**Controller writes are undoable, and UnionAir registers none of it.** The `UnityEditor.Animations` editing APIs register their own undo, so a second registration would be redundant and UnionAir adds none.

**One request is one undo entry.** That part is not free. Unity's registration lands in whichever undo group is current, and nothing advances the group between two HTTP-triggered callbacks — Unity advances it after a *human* interaction with the Editor, which is exactly what does not happen here. So each write path opens its own group before it mutates anything and collapses to it afterwards, and the group carries the operation's name into **Edit > Undo History**. Without that, every controller write since the user last touched the Editor accumulated into one entry: measured on 6000.0.80f1, four consecutive `POST .../states` calls were all taken back by a single Ctrl+Z.

**Clip content writes are not undoable, by choice.** These APIs register nothing, and UnionAir does not register on their behalf. An asset write here is saved to disk before the response is sent, so a `200` means the file on disk already changed — recovery belongs to version control rather than to the undo stack. Registering undo would let Ctrl+Z revert the asset in memory while the file kept the written content until some later, unrelated save, leaving a state that is neither before nor after. This covers curves, the `settings` block, and animation events alike.

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
  "name": "Walk",
  "clipsAtPath": 1,
  "clipNames": ["Walk"],
  "imported": false,
  "importer": null,
  "writable": true,
  "frameRate": 60.0,
  "length": 1.0,
  "wrapMode": "Loop",
  "settings": {
    "loopTime": true,
    "loopBlend": false,
    "cycleOffset": 0.0,
    "loopBlendOrientation": false,
    "loopBlendPositionY": false,
    "loopBlendPositionXZ": false,
    "keepOriginalOrientation": false,
    "keepOriginalPositionY": true,
    "keepOriginalPositionXZ": false,
    "heightFromFeet": false,
    "mirror": false,
    "level": 0.0,
    "orientationOffsetY": 0.0,
    "startTime": 0.0,
    "stopTime": 1.0,
    "additiveReferencePoseTime": 0.0,
    "hasAdditiveReferencePose": false
  },
  "events": [
    {
      "time": 0.5,
      "functionName": "Footstep",
      "stringParameter": "left",
      "floatParameter": 0.0,
      "intParameter": 0,
      "objectReferenceParameter": null,
      "messageOptions": "RequireReceiver"
    }
  ],
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

### `wrapMode` is not Loop Time

`wrapMode` is a `WrapMode` on the clip object. **Whether the clip loops is `settings.loopTime`**, which is what the Animation Inspector labels Loop Time. Measured on 6000.0.80f1, an imported idle clip reports `"wrapMode": "Default"` and `"settings": { "loopTime": true }` — a client reading `wrapMode` to find out whether a clip loops gets an answer about something else.

Both are reported, next to each other, so the two cannot be mistaken for one.

### `settings`

Everything the Animation Inspector shows above the curve list: `loopTime`, `loopBlend`, `cycleOffset`, `loopBlendOrientation`, `loopBlendPositionY`, `loopBlendPositionXZ`, `keepOriginalOrientation`, `keepOriginalPositionY`, `keepOriginalPositionXZ`, `heightFromFeet`, `mirror`, `level`, `orientationOffsetY`, `startTime`, `stopTime`, `additiveReferencePoseTime`, and `hasAdditiveReferencePose`. Set any subset through [`PATCH`](#patch-apiassetsanimation-clipsguid).

### `imported`, `writable`, and who owns the clip

| Field | Description |
|-------|-------------|
| `name` | The clip's own name. `assetPath` and `guid` identify the **file**, which inside an `.fbx` is not the clip |
| `clipsAtPath`, `clipNames` | How many AnimationClips share the path, and their names |
| `imported` | Whether an importer generated the clip |
| `importer` | The importer's type name, or `null` |
| `writable` | Whether this API will write to it |

A clip inside an `.fbx` is generated by a `ModelImporter`, which owns its settings. `AnimationUtility.SetAnimationClipSettings` on one mutates an in-memory object that the next reimport discards, so **every write endpoint refuses an imported clip with `409`** — including `POST` and `DELETE .../curves`, which used to accept the write and lose it silently. Changing an imported clip means changing the importer, which UnionAir does not expose yet.

Ownership is decided by asking `AssetImporter.GetAtPath` what imported the path, not by the file extension: a `.anim` is imported too, by a `NativeFormatImporter` that does not own the clip's settings.

`LoadAssetAtPath` returns whichever clip the importer lists first, so a path holding several takes exposes one by GUID and hides the rest. `clipsAtPath` above `1` says so. Addressing an individual clip inside an imported file is a sub-asset problem this endpoint does not solve.

### Errors

| Status | Cause |
|--------|-------|
| 400 | Asset is not an AnimationClip |
| 404 | No asset found for the given GUID |

---

## PATCH /api/assets/animation-clips/{guid}

Sets a clip's `frameRate`, `wrapMode`, and any subset of `settings`.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "frameRate": 30.0,
  "wrapMode": "Loop",
  "settings": { "loopTime": true, "cycleOffset": 0.0 }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `frameRate` | ❌ | Samples per second. Must be greater than zero |
| `wrapMode` | ❌ | `Once`, `Loop`, `PingPong`, `ClampForever`, or `Default`. **Not Loop Time** |
| `settings` | ❌ | Any subset of the fields listed under [`settings`](#settings) |

An omitted field is left unchanged, and an unknown one — including a settings field sent at the top level — is a `400` naming it. Every value is checked before the first is written, so a rejected request leaves the clip as it was.

### Response

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "name": "Walk",
  "applied": ["frameRate", "settings.loopTime"],
  "settings": {
    "loopTime": true,
    "loopBlend": false,
    "cycleOffset": 0.0,
    "loopBlendOrientation": false,
    "loopBlendPositionY": false,
    "loopBlendPositionXZ": false,
    "keepOriginalOrientation": false,
    "keepOriginalPositionY": true,
    "keepOriginalPositionXZ": false,
    "heightFromFeet": false,
    "mirror": false,
    "level": 0.0,
    "orientationOffsetY": 0.0,
    "startTime": 0.0,
    "stopTime": 1.0,
    "additiveReferencePoseTime": 0.0,
    "hasAdditiveReferencePose": false
  }
}
```

`applied` names what the request set; `settings` is the clip's whole settings object as it stands afterwards, not the subset that was patched.

### Errors

| Status | Cause |
|--------|-------|
| 400 | A value is malformed, `wrapMode` is unknown, or the body carries an unknown field |
| 404 | No asset found for the given GUID |
| 409 | The clip is generated by an importer |

---

## POST /api/assets/animation-clips/{guid}/events

Replaces every animation event on a clip.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "events": [
    { "time": 0.25, "functionName": "Footstep", "stringParameter": "left" },
    { "time": 0.75, "functionName": "Hit", "objectReferenceParameter": { "guid": "a1b2c3..." },
      "messageOptions": "DontRequireReceiver" }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `time` | ✅ | Time in seconds |
| `functionName` | ✅ | Method to call on the animated GameObject's components |
| `stringParameter`, `floatParameter`, `intParameter` | ❌ | Parameters |
| `objectReferenceParameter` | ❌ | `{guid}` of an asset, or `null` |
| `messageOptions` | ❌ | `RequireReceiver` or `DontRequireReceiver`. Defaults to `RequireReceiver`, which is Unity's default for a new event rather than this endpoint's choice |

**The array replaces the whole list.** Unity stores events as an ordered array with no identity per entry and rewrites it wholesale, so addressing one would mean inventing an identity the format does not have. Send `[]`, or use `DELETE`, to clear them. Omitting `events` is a `400` rather than a clear — an absent array must not read as a request to remove everything.

Every element is parsed and resolved before any is written, so a list whose fourth entry names a missing asset replaces nothing.

### Response

```json
{
  "assetPath": "Assets/Animations/Walk.anim",
  "eventCount": 2,
  "events": [
    {
      "time": 0.25,
      "functionName": "Footstep",
      "stringParameter": "left",
      "floatParameter": 0.0,
      "intParameter": 0,
      "objectReferenceParameter": null,
      "messageOptions": "RequireReceiver"
    },
    {
      "time": 0.75,
      "functionName": "Hit",
      "stringParameter": "",
      "floatParameter": 0.0,
      "intParameter": 0,
      "objectReferenceParameter": { "guid": "a1b2c3...", "name": "HitVfx" },
      "messageOptions": "DontRequireReceiver"
    }
  ]
}
```

The events are echoed back as stored, in the shape `GET` reports them — so an omitted `stringParameter` comes back as `""` and an omitted `messageOptions` as `RequireReceiver`, which is what the first entry above shows.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `events` is missing or malformed, an entry lacks `time` or `functionName`, or `messageOptions` is unknown |
| 404 | An `objectReferenceParameter` GUID resolves to nothing |
| 409 | The clip is generated by an importer |

---

## DELETE /api/assets/animation-clips/{guid}/events

Removes every animation event from a clip.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Response

```json
{ "assetPath": "Assets/Animations/Walk.anim", "removed": 2 }
```

### Errors

| Status | Cause |
|--------|-------|
| 404 | No asset found for the given GUID |
| 409 | The clip is generated by an importer |

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
| `property` | ✅ | Serialized property path (e.g. `localPosition.y`, `m_Sprite`). Not always the name that ends up on the clip — see below |
| `keys[].time` | ✅ | Time in seconds |
| `keys[].value` | ✅ (float curves) | Float value |
| `keys[].inTangent` / `outTangent` | ❌ | Tangents (default: 0) |
| `keys[].guid` | ✅ (object ref) | GUID of the referenced asset. For Sprite-mode textures, the Sprite sub-asset is loaded automatically |

> Both `curves` and `objectReferenceCurves` can be provided in the same request.

### One entry does not always mean one curve

`AnimationClip.SetCurve` recognizes four property names on `Transform` and rewrites each of them into a whole group of bindings, filling the components you did not ask for with that property's default value, held constant for the length of the curve. Writing `localPosition.y` stores `m_LocalPosition.x`, `.y`, and `.z`, so **animating one axis pins the other two** — to `0` for position, `1` for scale.

| `property` | Stored as |
|------------|-----------|
| `localPosition` or `m_LocalPosition` | `m_LocalPosition.x`, `.y`, `.z` |
| `localScale` or `m_LocalScale` | `m_LocalScale.x`, `.y`, `.z` |
| `localRotation` or `m_LocalRotation` | `m_LocalRotation.x`, `.y`, `.z`, `.w` — four, because the stored rotation is a quaternion |
| `localEulerAngles` or `localEulerAnglesRaw` | `localEulerAnglesRaw.x`, `.y`, `.z` |

The pinning is not caused by the group write. Unity combines a Transform's component curves into one Vector3 or Quaternion curve, and a component with no curve contributes its default rather than the object's authored value — so a clip holding `m_LocalPosition.y` alone drives `x` and `z` to `0` just the same, whether or not the other two bindings exist. What the group write changes is that the asset states it, and the Animation window shows it, instead of it happening invisibly at playback. A later write to another component of the same group replaces that component and leaves the ones already carrying curves alone, so the axes can be filled in over several requests.

Both spellings of a group reach the same bindings, and the component suffix selects which one carries your keys — it does not select the group. That is the whole of the rewriting. It is **not** a general translation from scripting names to serialized names: it is keyed on the type being `Transform` and on the name matching one of the eight spellings above, case-sensitively. Everything else is stored exactly as sent — including `position`, `rotation`, and `eulerAngles`, which are `Transform` scripting properties that animate nothing when written as curves, and including `Light.intensity`, which stays `intensity` rather than becoming the `m_Intensity` that works.

A scalar such as `Light.m_Intensity`, a single colour channel such as `Light.m_Color.r`, a blend shape, and a material property are each stored as one binding, so the behavior cannot be predicted from the shape of the name. The response states it instead: `bindings` lists what the entry produced, `requested` repeats what it was asked for.

Object reference curves are addressed exactly and are never expanded.

> The property name is **not** checked against what the type can animate. `SetCurve` accepts any name, so a misspelling becomes a binding that animates nothing and is reported as written. There is no reliable check to apply: `localEulerAnglesRaw`, blend shape, and material bindings all sit outside the animatable set `AnimationUtility.GetAnimatableBindings` reports for their type, so a check against it would reject working curves.

### Two entries that answer 200 and do not do what they say

Both are consequences of the group write, both are silent, and neither is currently detected. Measured on 6000.0.80f1.

**A component suffix that is missing or not part of the group loses your keys.** The suffix selects which component carries the curve; the group is selected by the prefix alone. When the suffix names no component of that group, the keys have nowhere to land and the group is created empty:

```
property = m_LocalPosition.y  ->  x [(0,0),(1,0)]   y [(0,7),(1,9)]   z [(0,0),(1,0)]
property = m_LocalPosition    ->  x []              y []              z []
property = m_LocalPosition.w  ->  x []              y []              z []
```

The response lists three bindings and no error, and the clip animates nothing. Send the exact component.

**Rotation written as a quaternion needs all four components in the request.** A single entry on `localRotation.y` creates all four bindings, but fills `w` with `0`, and a quaternion `(0, y, 0, 0)` normalizes to a half turn whatever `y` holds:

| Request | Result at t=1 |
|---------|---------------|
| one entry, `localRotation.y` → `0.7071` (90°) | **180°** |
| four entries, `m_LocalRotation.x/.y/.z/.w` → `(0, 0.7071, 0, 0.7071)` | 90° |
| one entry, `localEulerAngles.y` → `90` | 90° |

Euler is the one that works from a single entry, because there the unwritten components default to `0`, which is the identity. Use `localEulerAngles.*` unless you are writing all four quaternion components deliberately.

### Response

```json
{
  "added": ["m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_Sprite"],
  "addedFloat": ["m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z"],
  "addedObjectReference": ["m_Sprite"],
  "curves": [
    {
      "relativePath": "Hips",
      "type": "Transform",
      "requested": "localPosition.y",
      "bindings": ["m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z"]
    }
  ],
  "objectReferenceCurves": [
    { "relativePath": "", "type": "Image", "requested": "m_Sprite", "bindings": ["m_Sprite"] }
  ],
  "errors": []
}
```

| Field | Description |
|-------|-------------|
| `added` | Every binding the clip holds because of this request, under the serialized names `GET` reports and `DELETE .../curves` accepts |
| `addedFloat` / `addedObjectReference` | The same list split by curve kind |
| `curves[]` / `objectReferenceCurves[]` | One entry per entry in the request: `requested` is the name that was sent, `bindings` the names it produced |
| `errors` | Entries that were rejected, and bindings the write was expected to produce that the clip does not hold |

`added` reports what exists after the call rather than what was asked for, so a name taken from a write can be handed straight to `DELETE .../curves`. A binding is listed once per curve: the same property name on two paths is two bindings and appears twice.

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

The expansion belongs to `SetCurve`, not to the shorthand: passing the serialized name `m_LocalPosition.y` expands identically. It applies to Transform's local position, scale, rotation, and euler angles, and to nothing else — the four groups are listed under `POST .../curves`. A scalar such as `Light.m_Intensity`, and a single colour channel such as `Light.m_Color.r`, are each stored as one binding.

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
      "defaultState": "Idle",
      "states": [
        {
          "name": "Idle",
          "isDefault": true,
          "tag": "",
          "writeDefaultValues": true,
          "iKOnFeet": false,
          "mirror": false,
          "cycleOffset": 0.0,
          "speed": 1.0,
          "speedParameter": "",
          "speedParameterActive": false,
          "cycleOffsetParameter": "",
          "cycleOffsetParameterActive": false,
          "mirrorParameter": "",
          "mirrorParameterActive": false,
          "timeParameter": "",
          "timeParameterActive": false,
          "position": { "x": 156.0, "y": -48.0 },
          "behaviours": [],
          "motion": {
            "type": "AnimationClip",
            "guid": "d4e5f6...",
            "name": "IdleClip",
            "assetPath": "Assets/Animations/Idle.anim",
            "clipsAtPath": 1
          },
          "transitions": [
            {
              "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
              "destination": { "type": "State", "name": "Walk" },
              "hasExitTime": false,
              "exitTime": 0.0,
              "duration": 0.25,
              "fixedDuration": true,
              "offset": 0.0,
              "interruptionSource": "None",
              "orderedInterruption": true,
              "canTransitionToSelf": true,
              "mute": false,
              "solo": false,
              "conditions": [
                { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
              ]
            }
          ]
        }
      ],
      "anyStateTransitions": [],
      "entryTransitions": [],
      "stateMachineTransitions": [],
      "behaviours": [],
      "stateMachines": [
        {
          "name": "Combat",
          "path": ["Combat"],
          "position": { "x": 300.0, "y": 60.0 },
          "defaultState": null,
          "states": [],
          "anyStateTransitions": [],
          "entryTransitions": [
            {
              "transitionId": "GlobalObjectId_V1-3-a1b2c3...-1355314737468677203-0",
              "from": { "type": "Entry" },
              "destination": { "type": "StateMachine", "name": "Melee" },
              "solo": false,
              "mute": false,
              "conditions": []
            }
          ],
          "stateMachineTransitions": [],
          "behaviours": [],
          "stateMachines": []
        }
      ]
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
  "blendParameterY": "Blend",
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
      "directBlendParameter": "Blend",
      "motion": { "type": "AnimationClip", "guid": "...", "name": "Walk", "assetPath": "...", "clipsAtPath": 1 }
    }
  ]
}
```

Nesting is serialized to a depth of 10. A blend tree at that depth is reported with `"truncated": true` and **no** `children`, so a boundary is distinguishable from a leaf; an empty `children` array keeps meaning what it says, since a blend tree may genuinely have none.

### Sub-state machines

A layer's root state machine and every machine nested in it carry the same fields, so a client walks one structure rather than two. The root's `name` and `position` belong to the layer, which is what it is; a nested machine reports its own.

| Field | Description |
|-------|-------------|
| `name` | The machine's name. Also a segment of its `path` |
| `path` | Names from the layer root down to this machine. The same array a request sends as `stateMachinePath` |
| `position` | `{x, y}` graph position of the node in the parent machine |
| `defaultState` | Name of the state the machine starts in, or `null` |
| `states` | See [State fields](#state-fields) |
| `stateMachines` | Nested machines, in the same shape |
| `anyStateTransitions` | AnyState transitions **of this machine**. Every machine has its own |
| `entryTransitions` | Transitions from this machine's Entry node. See [Transitions between state machines](#transitions-between-state-machines) |
| `stateMachineTransitions` | Transitions that leave the machines nested in this one |
| `behaviours` | Type names of the attached `StateMachineBehaviour` instances. **Read-only**, as on a state |

### `layerIndex`

Every animation endpoint that addresses part of a controller takes `layerIndex`, and defaults it to `0`. It may be sent in the body or as a query parameter — `?layerIndex=1` — and the body wins when both are present. A value that is present and not an integer is a `400` rather than a silent fall back to the base layer.

`stateMachinePath` has no query form: it is an array, and a query string has no unambiguous way to carry one.

### `stateMachinePath`

Every endpoint that names a state accepts `stateMachinePath`, an array of state machine names from the layer root:

```json
{ "layerIndex": 0, "stateMachinePath": ["Combat", "Melee"], "name": "Swing" }
```

Omitted or `[]` means the layer's root state machine, which is what every request meant before the field existed — so no request that worked changes meaning.

**It is an array, not a `/`-joined string.** Unity does not forbid `/` in a state machine name, so a joined path would need an escaping rule, and an escaping rule is a thing clients get wrong quietly. An array has no separator to collide with.

Read responses report the same array as `path`, so a path read out of a response goes straight back into a request.

Unity permits two sibling machines to carry the same name — not through this API, which refuses to create one, but through a rename in the Animator window. A path that reaches such a pair is a `409` naming the ambiguity rather than a silent choice between them.

**Renaming a state machine invalidates every path a client holds**, including paths held for states inside it. There is no stable id for a state machine the way there is for a transition; re-read the controller after a rename.

### Transitions between state machines

Two different Unity types are involved, and the response keeps them apart.

`AnimatorStateTransition` connects states. It is what `states[].transitions` and `anyStateTransitions` hold, and it carries the timing and interruption fields documented under [Transition fields](#transition-fields).

`AnimatorTransition` connects state machines. It is what `entryTransitions` and `stateMachineTransitions` hold, and it carries **only** a source, a destination, `solo`, `mute`, and `conditions`. It has no `hasExitTime`, `duration`, `offset`, or interruption, and those fields are not emitted as zeros — a `"duration": 0` would read as a setting rather than as a field the type does not have. It carries its own `transitionId`, from the same mechanism.

| Field | Description |
|-------|-------------|
| `transitionId` | Address for `DELETE .../state-machine-transitions` |
| `from` | `{"type": "Entry"}` on an entry transition, or `{"type": "StateMachine", "name": "..."}` on one leaving a nested machine |
| `destination` | See below |
| `solo`, `mute` | The serialized values |
| `conditions` | Array of `{parameter, mode, threshold}` |

### `destination`

Every transition, of either type, reports its destination as a discriminated object rather than a name:

| `type` | Meaning |
|--------|---------|
| `State` | `name` is a state in the same machine |
| `StateMachine` | `name` is a state machine. Entering one starts it at its own Entry |
| `Exit` | The machine's Exit node. No `name` |
| `None` | The destination was deleted. Reported as what it is rather than as a `null` name |

A name alone cannot say what it names: once a destination may be a state or a state machine, `"Melee"` is one in one controller and the other in another, and a client walking the response cannot tell. This is the discipline the `motion` field already follows.

**`Entry` is not among the values.** Unity offers no destination that is an Entry node — entering a state machine is a destination of type `StateMachine`, and the Entry node appears only as the *source* of an entry transition. A value nothing can produce would be worse than its absence.

### State fields

| Field | Description |
|-------|-------------|
| `name` | State name. Also its address on `PATCH` and `DELETE` |
| `isDefault` | Whether this is the layer's default state |
| `tag` | The string runtime code matches with `AnimatorStateInfo.IsTag` |
| `writeDefaultValues` | Whether properties the state does not animate are reset to their defaults |
| `iKOnFeet` | Foot IK |
| `mirror` | Whether the motion plays mirrored |
| `cycleOffset` | Normalized offset into the motion's cycle |
| `speed` | Playback speed. **Not the speed in effect when `speedParameterActive` is `true`** |
| `speedParameter`, `cycleOffsetParameter`, `mirrorParameter`, `timeParameter` | Parameter driving each value |
| `speedParameterActive`, `cycleOffsetParameterActive`, `mirrorParameterActive`, `timeParameterActive` | Whether that override is in effect |
| `position` | Where the state sits in the Animator window's graph. See below |
| `behaviours` | Type names of the attached `StateMachineBehaviour` instances. **Read-only.** A `null` entry is one whose script is missing |
| `motion` | See [Motion](#motion) |
| `transitions` | See [Transition fields](#transition-fields) |

Each `*Parameter` is reported beside its own `*Active` flag rather than folded to an empty string when inactive. Unity stores both, and an inactive parameter name is content the asset holds — a client reproducing the state needs it.

#### `position` is graph layout

`position` is not a property of the state. It lives on `ChildAnimatorState`, the entry in the state machine's array, which is what the Animator window reads to lay out the graph — so writing it moves the node, and a controller authored entirely through the API otherwise stacks every state at the origin.

Unity's field is a `Vector3` and the graph is flat: `z` is unused there, so it is not reported. A write sets `x` and `y` and leaves whatever `z` holds.

#### `behaviours` is read-only

The read reports what is attached so that a state which runs script is distinguishable from one that does not. Attaching one is not offered: it means resolving a script type and instantiating it as a sub-asset of the controller, which is an ownership problem of its own. `behaviours` sent in a request body is reported in `unsupported` rather than ignored.

### Transition fields

Every transition, on a state and on AnyState alike, carries these.

| Field | Description |
|-------|-------------|
| `transitionId` | Stable address for this transition. See [Addressing a transition](#addressing-a-transition) |
| `destination` | Discriminated destination. See [`destination`](#destination) |
| `hasExitTime` | Whether exit time triggers the transition |
| `exitTime` | Normalized time at which exit time triggers |
| `duration` | Blend duration. **Seconds when `fixedDuration` is `true`, a fraction of the source state when it is `false`** |
| `fixedDuration` | `AnimatorStateTransition.hasFixedDuration`. Unity gives a new transition `true` |
| `offset` | Normalized time offset in the destination state |
| `interruptionSource` | `None`, `Source`, `Destination`, `SourceThenDestination`, or `DestinationThenSource` |
| `orderedInterruption` | Whether interruption respects transition order |
| `canTransitionToSelf` | Consulted on AnyState transitions only; stored and reported on every transition |
| `mute`, `solo` | The serialized values, as they are. What the Animator window computes from them across a layer is not reported |
| `conditions` | Array of `{parameter, mode, threshold}` |

`duration` and `fixedDuration` always travel together, because neither means anything alone: the same number is seconds under one and a fraction of the source state under the other.

### Addressing a transition

A state pair may carry any number of transitions — that is how a pair gets several routes, one per condition set — so `from` plus `to` names one transition only while there is one. `transitionId` names exactly one, always.

The id is a Unity `GlobalObjectId` for the transition, which is a sub-asset of the controller. Measured on 6000.0.80f1:

- it resolves to the same transition after a domain reload;
- it follows the transition when the state's `transitions` array is reordered, rather than the position;
- it is already valid on the transition `POST` just created, before `SaveAssets`;
- it stops resolving once the transition is deleted, which is what makes a stale id a `404` rather than a wrong hit.

Treat it as opaque and re-read it after deleting transitions.

### Not described by this response

The response describes the **asset**, not a playing Animator. There is no runtime surface: no endpoint reads the state a live Animator is in, its normalized time, or the effective layer weights in Play mode, and none drives parameters or `CrossFade`. `defaultWeight` is the stored field, which on the base layer is not the weight in effect — see [`defaultWeight` on the base layer](#defaultweight-on-the-base-layer).

Two further limits are reported rather than hidden, and each is described where it applies:

- `behaviours` gives type names only, on states and on state machines alike. See [`behaviours` is read-only](#behaviours-is-read-only).
- Blend tree and state machine nesting is serialized to a depth of 10. A node at that depth carries `"truncated": true` instead of its contents, so a boundary is never mistaken for an empty one.

`mute` and `solo` are the serialized values. What the Animator window computes from them across a whole layer is not reported.

### Errors

| Status | Cause |
|--------|-------|
| 400 | Asset is not an AnimatorController |
| 404 | No asset found for the given GUID |

---

## POST /api/assets/animator-controllers/{guid}/parameters

Adds a parameter, or updates one that already exists.

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
| `defaultValue` | ❌ | Default value. Not stored for a `Trigger` — see below |

A parameter that already exists **with the same type** is updated in place and keeps its position in the parameter array. Only a `type` change destroys and recreates it, and that orphans every reference; see [Parameter references](#parameter-references).

Every value is checked before anything is created or replaced, so a request rejected with `400` adds no parameter and changes no type.

Use [`PATCH`](#patch-apiassetsanimator-controllersguidparameters) to rename.

### Response (HTTP 201)

```json
{ "added": "Speed", "type": "Float", "unsupported": [] }
```

When the type changed, the response also carries `"replacedType": true` and an `orphanedReferences` array in the shape shown under [Parameter references](#parameter-references).

### `defaultValue` on a Trigger

A Trigger is set and consumed within a frame, so Unity keeps no default for one. The request is not refused — the field is named in `unsupported` instead of being dropped, which is what this endpoint used to do while answering `201` as though it had applied.

---

## PATCH /api/assets/animator-controllers/{guid}/parameters

Renames a parameter, or sets its default value, in place.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{ "name": "Speed", "newName": "MoveSpeed", "defaultValue": 0.5 }
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | ✅ | The parameter to change |
| `newName` | ❌ | New name. Every reference is rewritten with it |
| `defaultValue` | ❌ | New default value |

Either or both of `newName` and `defaultValue`. The parameter keeps its position in the array in both cases.

**A rename is atomic**, and so is a request that renames and sets a default value together. Every value is parsed and every check made before the first write, so a request that is refused — for a collision, or for a malformed `defaultValue` — leaves the parameter and all of its references exactly as they were. A half-applied rename is the corruption this endpoint exists to prevent.

### Response

```json
{
  "name": "MoveSpeed",
  "type": "Float",
  "renamed": { "from": "Speed", "to": "MoveSpeed" },
  "referencesUpdated": 3,
  "references": [
    { "kind": "condition", "layerIndex": 0, "stateMachinePath": ["Combat"], "transitionId": "GlobalObjectId_V1-3-...", "conditionIndex": 0 },
    { "kind": "blendParameter", "layerIndex": 0, "stateMachinePath": [], "state": "Locomotion", "childPath": [0] },
    { "kind": "speedParameter", "layerIndex": 0, "stateMachinePath": [], "state": "Run" }
  ],
  "unsupported": []
}
```

A count alone would not be enough: a caller that renamed a parameter needs to be able to check the sites, and one that sees zero references where it expected some has found a bug in the controller or here.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `name` is missing, `newName` is empty, a value is malformed, `type` was sent, or the body carries an unknown field |
| 404 | Parameter not found |
| 409 | `newName` already names a parameter. Nothing is changed |

### `type` cannot be changed here

A type change invalidates every condition that names the parameter, in a way nothing can resolve on the client's behalf: `Greater` with a threshold of `0.1` is a sentence about a Float, and it has no reading at all once the parameter is a Trigger. A `type` field is rejected with that reason rather than ignored. `DELETE` then `POST` is the honest route, and both report the references the change orphans.

---

## Parameter references

A parameter is named from four kinds of site, and **none of them is a reference Unity maintains** — every one is a plain string:

| `kind` | Where | Extra fields |
|--------|-------|--------------|
| `condition` | `AnimatorCondition.parameter`, on state, AnyState, entry, and state machine transitions | `transitionId`, `conditionIndex` |
| `blendParameter`, `blendParameterY` | A blend tree, including nested ones | `state`, `childPath` |
| `speedParameter`, `cycleOffsetParameter`, `mirrorParameter`, `timeParameter` | A state's override | `state` |

Every reference carries `layerIndex` and `stateMachinePath`, so a site inside a sub-state machine is addressable from the report.

Measured on Unity 6000.0.80f1: renaming a parameter by assigning a modified `parameters` array renames it and leaves **every** one of those strings naming what no longer exists. The controller still loads, the conditions still serialize, and they never evaluate again. That is why `PATCH` rewrites them and why a rename built out of `DELETE` plus `POST` is not equivalent.

`DELETE` and a `type` change through `POST` report the references they orphan in the same shape. They still go through — deciding what a condition should become without its parameter is not a decision this API can make — but they are no longer silent.

Conditions already orphaned by earlier deletes are not repaired. Find them by comparing the conditions in `GET /api/assets/animator-controllers/{guid}` against its `parameters` list.

---

## DELETE /api/assets/animator-controllers/{guid}/parameters

Removes a parameter by name, and reports the references it orphans.

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
{
  "removed": "Speed",
  "orphanedReferences": 2,
  "references": [
    { "kind": "blendParameter", "layerIndex": 0, "stateMachinePath": [], "state": "Locomotion", "childPath": [] },
    { "kind": "condition", "layerIndex": 0, "stateMachinePath": [], "transitionId": "GlobalObjectId_V1-3-...", "conditionIndex": 0 }
  ]
}
```

The parameter is deleted either way. See [Parameter references](#parameter-references) for why the conditions are left alone.

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
  "writeDefaultValues": false,
  "tag": "Locomotion",
  "position": { "x": 300, "y": 120 },
  "setAsDefault": false
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | ✅ | State name |
| `layerIndex` | ❌ | Target layer index (default: 0) |
| `setAsDefault` | ❌ | If `true`, sets this state as the layer's default (entry) state |

Every writable setting from [State fields](#state-fields) may also be supplied, so a state can be created fully formed rather than created and then patched: `motion`, `speed`, `tag`, `writeDefaultValues`, `iKOnFeet`, `mirror`, `cycleOffset`, the four `*Parameter` fields with their `*Active` flags, and `position`. See [PATCH](#patch-apiassetsanimator-controllersguidstates) for what each accepts.

### Response (HTTP 201)

```json
{ "added": "Walk", "layerIndex": 0, "isDefault": false, "unsupported": [] }
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `name` is missing, `layerIndex` is out of range, a motion GUID is not found, a setting is malformed, a `*Parameter` names no parameter on the controller, or the body carries an unknown field |

Every value is checked before the state is created, so a request rejected with `400` adds nothing.

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
  "writeDefaultValues": false,
  "cycleOffset": 0.25,
  "speedParameter": "Speed",
  "speedParameterActive": true,
  "position": { "x": 300, "y": 120 },
  "setAsDefault": true
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | ✅ | Current state name (used to identify the state) |
| `layerIndex` | ❌ | Layer index (default: 0) |
| `newName` | ❌ | New name for the state |
| `setAsDefault` | ❌ | Set this state as the layer default |
| `motion` | ❌ | Object with `guid` referencing a Motion asset. Replaces the assigned motion |
| `speed` | ❌ | Playback speed |
| `tag` | ❌ | The string runtime code matches with `AnimatorStateInfo.IsTag`. `""` clears it |
| `writeDefaultValues` | ❌ | Whether properties the state does not animate are reset to their defaults |
| `iKOnFeet` | ❌ | Foot IK |
| `mirror` | ❌ | Whether the motion plays mirrored |
| `cycleOffset` | ❌ | Normalized offset into the motion's cycle |
| `speedParameter`, `cycleOffsetParameter`, `mirrorParameter`, `timeParameter` | ❌ | Parameter driving each value. `""` clears the override |
| `speedParameterActive`, `cycleOffsetParameterActive`, `mirrorParameterActive`, `timeParameterActive` | ❌ | Whether that override is in effect |
| `position` | ❌ | `{x, y}` graph position. See [`position` is graph layout](#position-is-graph-layout) |
| `behaviours` | ❌ | Accepted and **not applied** — read-only, and reported in `unsupported` |

An omitted field is left unchanged. Every value is checked before the first is written, so a request rejected with `400` leaves the state exactly as it was rather than partly updated.

A `*Parameter` and its `*Active` flag are one decision, and the check is made on the pair the request would leave behind — the value it carries where it carries one, the state's current value otherwise. An override that is on and names nothing is a state that cannot play, and neither half has to look wrong on its own to produce one.

| Request | Result |
|---|---|
| A name the controller does not have | `400`. Neither half is written |
| `*Active: true` with no name in the request and none on the state | `400` — the override would drive nothing |
| `*Parameter: ""` while the flag stays `true` | `400`, for the same reason. Send `*Active: false` in the same request to clear both |
| `*Active: true` alone, where the state already names a parameter that exists | Accepted. The name does not have to be resent |
| `*Parameter: ""` with `*Active: false` | Accepted. Clears the override |

A name already on the state is not re-checked while the override stays off, so a patch to an unrelated field is not refused because someone deleted the parameter a dormant override still names.

**Unknown fields are rejected** with a `400` that lists the accepted ones, so a typo such as `writeDefaults` cannot pass for a setting that did nothing.

### Response

```json
{ "updated": "Run", "layerIndex": 0, "unsupported": [] }
```

`unsupported` names each field that was accepted but not applied — today only `behaviours`.

### Errors

| Status | Cause |
|--------|-------|
| 400 | A setting is malformed, a `*Parameter` names no parameter on the controller, or the body carries an unknown field |
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
  "fixedDuration": true,
  "offset": 0.0,
  "conditions": [
    { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `from` | ✅ | Source state name, or `"AnyState"` |
| `to` | ❌ | Destination state name, or `"Exit"` |
| `toStateMachine` | ❌ | Destination is a state machine, addressed as a path from the machine this transition belongs to. This is how a state enters a sub-state machine |
| `layerIndex` | ❌ | Layer index (default: 0) |
| `stateMachinePath` | ❌ | Which state machine owns the transition. See [`stateMachinePath`](#statemachinepath) |
| `hasExitTime` | ❌ | Whether the transition has an exit time trigger |
| `exitTime` | ❌ | Normalized time at which exit time triggers (when `hasExitTime: true`) |
| `duration` | ❌ | Blend duration. Seconds when `fixedDuration` is `true`, a fraction of the source state when it is `false` |
| `fixedDuration` | ❌ | Whether `duration` is seconds. Unity gives a new transition `true` |
| `offset` | ❌ | Normalized time offset in the destination state |
| `interruptionSource` | ❌ | `None`, `Source`, `Destination`, `SourceThenDestination`, or `DestinationThenSource` |
| `orderedInterruption` | ❌ | Whether interruption respects transition order |
| `canTransitionToSelf` | ❌ | AnyState transitions only. Sent for any other transition it is stored and named in `unsupported` |
| `mute` | ❌ | Mute the transition |
| `solo` | ❌ | Solo the transition |
| `conditions` | ❌ | Array of condition objects. Replaces the whole array |

**`to` and `toStateMachine` are each optional and exactly one is required.** Neither alone is mandatory — a transition to a state sends `to`, a transition into a sub-state machine sends `toStateMachine` — and sending both, or neither, is a `400`.

**Condition modes:** `If`, `IfNot` (Bool/Trigger), `Greater`, `Less`, `Equals`, `NotEqual` (Float/Int)

Every field is parsed and checked before the transition is created, so a request rejected with `400` adds nothing to the controller. A condition whose `mode` is not one of the six above is rejected rather than skipped, and so is a `threshold` that is present but not a number — a quoted `"0.5"`, a `null`, `NaN`. An **omitted** `threshold` is `0`, which is what `If` and `IfNot` use.

Adding a second transition between a pair that already has one is legal and stays legal. The response returns the new transition's `transitionId`, which is how it can be addressed afterwards.

### Response (HTTP 201)

```json
{
  "added": true,
  "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0,
  "unsupported": []
}
```

`unsupported` names each field that was stored but will not be consulted — today only `canTransitionToSelf` on a transition that does not leave AnyState.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `from` or `to` is missing, a setting is malformed, `interruptionSource` or a condition `mode` is unknown, or `"AnyState"` → `"Exit"` was requested |
| 404 | Source or destination state not found |

---

## PATCH /api/assets/animator-controllers/{guid}/transitions

Updates one transition.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{
  "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
  "duration": 0.1,
  "fixedDuration": false,
  "interruptionSource": "Destination",
  "conditions": [
    { "parameter": "Speed", "mode": "Greater", "threshold": 0.5 }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `transitionId` | ❌ | Address from the read response. Names exactly one transition |
| `from`, `to` | ❌ | Address by state names. Accepted while the pair carries exactly one transition |
| `layerIndex` | ❌ | Layer to look in (default: 0) |

Either `transitionId` or both `from` and `to` must be present; `transitionId` wins when both are sent. Every setting listed for `POST` is accepted, and an omitted setting is left unchanged.

`conditions` replaces the whole array. An **empty array clears the conditions** — it is not treated as "leave them alone", which is what omitting the field means.

Every value is parsed and checked before the first one is written, so a request rejected with `400` leaves the transition exactly as it was rather than partly updated.

### Response

```json
{
  "updated": true,
  "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0,
  "unsupported": []
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | No address was sent, `transitionId` is malformed, or a setting is malformed or unknown |
| 404 | No transition matched, or `transitionId` no longer resolves. An id belonging to another layer says which layer it is in |
| 409 | `from` plus `to` matched more than one transition — see below |
| 422 | `transitionId` resolves to something that is not an `AnimatorStateTransition` |

### 409 on an ambiguous name pair

```json
{
  "error": "2 transitions match Idle -> Walk. Address one by transitionId; 'matches' lists every candidate with its conditions.",
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0,
  "matches": [
    {
      "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
      "conditions": [ { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 } ]
    },
    {
      "transitionId": "GlobalObjectId_V1-3-a1b2c3...-10875748444440948623-0",
      "conditions": [ { "parameter": "Jump", "mode": "If", "threshold": 0.0 } ]
    }
  ]
}
```

`409` rather than `400`: the request is well formed, and what makes the address unusable is the controller's own shape. The conditions travel with each candidate because they are what tells the routes apart, so a client can pick without a second request. Nothing is written.

---

## DELETE /api/assets/animator-controllers/{guid}/transitions

Removes one transition. The transition is a sub-asset of the controller and is destroyed with the removal.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{ "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0" }
```

Addressed exactly as `PATCH` is: `transitionId`, or `from` plus `to` while that pair carries one transition. `transitionId`, `from`, and `to` may also be sent as query parameters.

### Response

```json
{
  "removed": true,
  "transitionId": "GlobalObjectId_V1-3-a1b2c3...-14749150960317597279-0",
  "from": "Idle",
  "to": "Walk",
  "layerIndex": 0
}
```

The `transitionId` is the one that was removed; it no longer resolves.

### Errors

| Status | Cause |
|--------|-------|
| 400 | No address was sent, or `transitionId` is malformed |
| 404 | No transition matched, or `transitionId` no longer resolves |
| 409 | `from` plus `to` matched more than one transition. The body is the shape shown for `PATCH`, and nothing is removed |
| 422 | `transitionId` resolves to something that is not an `AnimatorStateTransition` |

---

## POST /api/assets/animator-controllers/{guid}/state-machines

Creates a sub-state machine.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Path Parameters

| Parameter | Description |
|-----------|-------------|
| `guid` | GUID of the AnimatorController asset |

### Request Body (JSON)

```json
{
  "layerIndex": 0,
  "stateMachinePath": ["Combat"],
  "name": "Melee",
  "position": { "x": 300, "y": 120 }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | ✅ | Name of the new machine |
| `layerIndex` | ❌ | Layer index (default: 0) |
| `stateMachinePath` | ❌ | The machine to create it inside. Omitted or `[]` is the layer's root |
| `position` | ❌ | `{x, y}` graph position of the node in the parent |

A `name` a sibling already carries answers `409`. Unity's own `AddStateMachine` does not duplicate it — measured on 6000.0.80f1, it quietly hands back a different name — so the alternative would be reporting a name the caller did not ask for, and a path addresses by name, so an address built on the requested name would not work.

### Response (HTTP 201)

```json
{ "added": "Melee", "layerIndex": 0, "stateMachinePath": ["Combat", "Melee"] }
```

The returned `stateMachinePath` addresses the new machine.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `name` is missing, `position` is malformed, or the body carries an unknown field |
| 404 | `stateMachinePath` does not resolve |
| 409 | A sibling already carries `name`, or the path is ambiguous |

---

## DELETE /api/assets/animator-controllers/{guid}/state-machines

Removes a sub-state machine and everything it holds.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{ "layerIndex": 0, "stateMachinePath": ["Combat", "Melee"], "recursive": true }
```

| Field | Required | Description |
|-------|----------|-------------|
| `stateMachinePath` | ✅ | The machine to remove. `[]` names the layer's root and is refused |
| `layerIndex` | ❌ | Layer index (default: 0) |
| `recursive` | ❌ | Confirms removing a machine that holds anything |

A state machine owns its states, its transitions, the machines nested in it, and the blend trees those states hold — all sub-assets of the controller. That makes this a larger operation than `DELETE .../states`, so a machine that holds anything answers `409` unless `recursive` is `true`:

```json
{
  "error": "State machine 'Combat' holds 2 state(s) and 1 nested state machine(s) in total, which removing it would take with it. Send recursive true to confirm.",
  "layerIndex": 0,
  "stateMachinePath": ["Combat"],
  "totalStates": 2,
  "totalStateMachines": 1,
  "states": [],
  "stateMachines": ["Melee"]
}
```

`totalStates` and `totalStateMachines` count the whole subtree, because that is what the removal costs. `states` and `stateMachines` name the direct children, which is what a caller recognises. A machine that directly holds no states but holds one that holds five is reported as costing five.

### Response

```json
{
  "removed": "Melee",
  "layerIndex": 0,
  "stateMachinePath": ["Combat", "Melee"],
  "removedStates": 3,
  "removedStateMachines": 0,
  "destroyedBlendTrees": 1
}
```

`destroyedBlendTrees` counts the blend trees this endpoint destroyed by hand after Unity's removal left them in the asset, exactly as `DELETE .../states` reports.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `stateMachinePath` is empty or malformed, or the body carries an unknown field |
| 404 | `stateMachinePath` does not resolve |
| 409 | The machine holds something and `recursive` was not `true`, or the path is ambiguous |

---

## POST /api/assets/animator-controllers/{guid}/state-machine-transitions

Adds an `AnimatorTransition` — the type that connects state machines. Without one, a sub-state machine can be created and never entered.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{
  "layerIndex": 0,
  "stateMachinePath": ["Combat"],
  "from": "Entry",
  "toStateMachine": ["Melee"],
  "conditions": []
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `from` | ✅ | `"Entry"` for an entry transition, or the name of a state machine nested in the addressed one |
| `layerIndex` | ❌ | Layer index (default: 0) |
| `stateMachinePath` | ❌ | The machine that owns the transition |
| `to` | ❌ | Destination state name, in the owning machine |
| `toStateMachine` | ❌ | Destination state machine, as a path from the owning machine |
| `toExit` | ❌ | Destination is the machine's Exit node |
| `solo`, `mute` | ❌ | |
| `conditions` | ❌ | Array of condition objects. Replaces the whole array |

Exactly one of `to`, `toStateMachine`, and `toExit` — the same reason the read gives a destination a discriminator: a bare name cannot say whether it means a state or a state machine, and a controller may use one name for both.

An entry transition cannot target Exit: Entry chooses where the machine starts.

### Response (HTTP 201)

```json
{ "added": true, "transitionId": "GlobalObjectId_V1-3-...", "layerIndex": 0, "from": "Entry" }
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `from` is missing, no destination or several were sent, `"Entry"` targeted Exit, or the body carries an unknown field |
| 404 | The source machine, destination state, or a path does not resolve |
| 409 | A path or the source name is ambiguous |

---

## DELETE /api/assets/animator-controllers/{guid}/state-machine-transitions

Removes an `AnimatorTransition`. The transition is a sub-asset of the controller and is destroyed with the removal.

> Can be called only when the Asset Write category is enabled.
> Returns `409 Conflict` in Play mode.

### Request Body (JSON)

```json
{ "transitionId": "GlobalObjectId_V1-3-..." }
```

| Field | Required | Description |
|-------|----------|-------------|
| `transitionId` | ✅ | From `entryTransitions` or `stateMachineTransitions` in the read |
| `layerIndex` | ❌ | Layer to search (default: 0) |

There is no name-pair form. These transitions have no source state to name, and an entry transition has no source at all beyond the Entry node.

### Response

```json
{ "removed": true, "transitionId": "GlobalObjectId_V1-3-...", "kind": "entry", "layerIndex": 0 }
```

`kind` is `entry` or `stateMachine`.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `transitionId` is missing or malformed, or the body carries an unknown field |
| 404 | The transition is not in that layer, or the id no longer resolves |
| 422 | The id resolves to an `AnimatorStateTransition`. Use `DELETE .../transitions` for those |

---
