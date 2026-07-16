# API Reference — Play Mode
**English** | [日本語](playmode.ja.md)

Base URL: `http://localhost:<port>/api/` (default port: **8765**). See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

---

## POST /api/editor/play

Enters Play mode (`EditorApplication.isPlaying = true`).

> Can be called only when the Play Mode category is enabled.
> If a domain reload occurs, the HTTP server will restart temporarily. Poll `GET /api/editor/status` and wait until `isPlaying: true`.

### Response

```json
{ "playing": true, "note": "Domain reload may occur. Poll GET /api/editor/status until isPlaying is true." }
```

---

## POST /api/editor/stop

Exits Play mode (`EditorApplication.isPlaying = false`).

> Can be called only when the Play Mode category is enabled.

### Response

```json
{ "playing": false }
```

---

## POST /api/editor/pause

Sets the paused state. If the body is omitted, toggles the current state.

> Can be called only when the Play Mode category is enabled.

### Request Body (JSON, optional)

```json
{ "paused": true }
```

### Response

```json
{ "paused": true }
```

---

## POST /api/editor/step

Advances by one frame. Valid only when `isPaused: true`.

> Can be called only when the Play Mode category is enabled.

### Response

```json
{ "stepped": true }
```

### Errors

| Status | Cause |
|-----------|------|
| 400 | Not in Play mode, or not paused |
| 403 | Play Mode category is disabled |

---

## GET /api/playmode/input/actions

Lists enabled Unity Input System actions in the running game.

> Requires the optional `com.unity.inputsystem` package.
> Can be called only when the Play Mode category is enabled.
> Returns `409 Conflict` outside Play mode.

### Response

```json
{
  "actions": [
    {
      "name": "Jump",
      "map": "Player",
      "actionType": "Button",
      "expectedControlType": "Button",
      "bindings": ["<Keyboard>/space", "<Gamepad>/buttonSouth"]
    }
  ],
  "count": 1
}
```

| Field | Description |
|-------|-------------|
| `actions[].name` | InputAction name. A bare name may be used by `perform` or `set` when it is unique |
| `actions[].map` | Action map name, or empty string. Combine non-empty map and action names as `Map/Action` for an unambiguous identifier |
| `actions[].actionType` | Unity InputAction type, such as `Button`, `Value`, or `PassThrough` |
| `actions[].expectedControlType` | Expected control type declared by the action |
| `actions[].bindings` | Non-empty effective binding paths exposed by the action |

### Errors

| Status | Cause |
|--------|-------|
| 403 | Play Mode category is disabled |
| 409 | Unity Editor is not in Play mode |

---

## POST /api/playmode/input/perform

Performs a Button InputAction through a UnionAir virtual device. `action` accepts a case-insensitive `Map/Action` identifier or a bare action name when that name is unique.

> Requires the optional `com.unity.inputsystem` package.
> Can be called only when the Play Mode category is enabled.
> Returns `409 Conflict` outside Play mode.

### Request Body (JSON)

Tap a Button action:

```json
{ "action": "Player/Jump" }
```

The shorter `{ "action": "Jump" }` form is also accepted when only one collected action is named `Jump`.

`mode` is optional and defaults to `tap`, which sends press -> update -> release -> update.

Hold a Button action:

```json
{ "action": "Player/Jump", "mode": "press" }
```

Release all controls held by UnionAir for that action:

```json
{ "action": "Player/Jump", "mode": "release" }
```

| Field | Required | Description |
|-------|----------|-------------|
| `action` | Yes | `Map/Action` identifier, or a bare InputAction name when unique |
| `mode` | No | `tap`, `press`, or `release`. Defaults to `tap` |

`value` is not accepted by this endpoint. Axis, Vector2, and Stick actions use `POST /api/playmode/input/set`.

For `tap` and `press`, UnionAir uses the first supported non-composite Button binding in the action's binding order. Supported Button devices are Keyboard, Gamepad, Mouse, and `<Pointer>/press` (mapped to the virtual Mouse left button). Touch, Pen, XR, custom devices, and composite bindings return `422`.

For `release`, UnionAir releases every control currently held by UnionAir for that action. Callers do not need to specify the binding selected during `press`.

### Response

Tap:

```json
{
  "success": true,
  "action": "Jump",
  "controlType": "Button",
  "mode": "tap",
  "pressedBinding": "<Keyboard>/space",
  "pressedControl": "/UnionAirVirtualKeyboard/space",
  "releasedControl": "/UnionAirVirtualKeyboard/space"
}
```

Press:

```json
{
  "success": true,
  "action": "Jump",
  "controlType": "Button",
  "mode": "press",
  "pressedBinding": "<Keyboard>/space",
  "pressedControl": "/UnionAirVirtualKeyboard/space"
}
```

Release:

```json
{
  "success": true,
  "action": "Jump",
  "controlType": "Button",
  "mode": "release",
  "releasedControls": ["/UnionAirVirtualKeyboard/space"]
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | `action` is missing, `mode` is invalid, `value` was provided, or the action is not a Button action |
| 403 | Play Mode category is disabled |
| 404 | Action not found |
| 409 | Unity Editor is not in Play mode, a pointer operation is in progress, or a bare action name matches multiple maps. Ambiguous responses include `candidates` |
| 422 | Button action exists, but no supported Keyboard/Gamepad/Mouse/Pointer Button binding can be simulated |

---

## POST /api/playmode/input/set

Sets an Axis, Vector2, or Stick InputAction value through a UnionAir virtual device. `action` accepts a case-insensitive `Map/Action` identifier or a bare action name when that name is unique. The value remains active until another `set` call changes it, Play mode changes, or UnionAir cleans up its virtual devices.

> Requires the optional `com.unity.inputsystem` package.
> Can be called only when the Play Mode category is enabled.
> Returns `409 Conflict` outside Play mode.

### Request Body (JSON)

Vector2 / Stick action:

```json
{ "action": "Player/Move", "value": [1.0, 0.0] }
```

Return to neutral:

```json
{ "action": "Move", "value": [0.0, 0.0] }
```

Axis action:

```json
{ "action": "Throttle", "value": 1.0 }
```

Return to neutral:

```json
{ "action": "Throttle", "value": 0.0 }
```

| Field | Required | Description |
|-------|----------|-------------|
| `action` | Yes | `Map/Action` identifier, or a bare InputAction name when unique |
| `value` | Yes | Axis: finite number; Vector2/Stick: `[x, y]` |

For actions with multiple bindings, UnionAir uses the first supported direct Gamepad value binding in the action's binding order. Supported set bindings are `<Gamepad>/leftStick`, `<Gamepad>/rightStick`, `<Gamepad>/leftTrigger`, `<Gamepad>/rightTrigger`, and Gamepad stick x/y axes. Keyboard composites such as WASD, arrow-key composites, Touch, Pen, XR, custom devices, and other controls return `422`.

### Response

Vector2:

```json
{
  "success": true,
  "action": "Move",
  "controlType": "Vector2",
  "value": [1.0, 0.0],
  "setBinding": "<Gamepad>/leftStick",
  "setControl": "/UnionAirVirtualGamepad/leftStick"
}
```

Axis:

```json
{
  "success": true,
  "action": "Throttle",
  "controlType": "Axis",
  "value": 1.0,
  "setBinding": "<Gamepad>/rightTrigger",
  "setControl": "/UnionAirVirtualGamepad/rightTrigger"
}
```

### Notes

UnionAir reports the binding/control it wrote, but Unity Input System remains responsible for action resolution after the virtual device event is queued. `PlayerInput` device pairing, control schemes, binding masks, interactions, processors, and action enablement can still prevent an action from observing the virtual device.

### Errors

| Status | Cause |
|--------|-------|
| 400 | `action` is missing, `value` is malformed or missing, or the action is a Button action |
| 403 | Play Mode category is disabled |
| 404 | Action not found |
| 409 | Unity Editor is not in Play mode, a pointer operation is in progress, or a bare action name matches multiple maps. Ambiguous responses include `candidates` |
| 422 | Action exists, but no supported direct Gamepad Axis/Vector2 binding can be set |

---

## POST /api/playmode/input/pointer

Simulates a mouse click, press, release, or move at a screen coordinate through the UnionAir virtual mouse. The move, press, and release phases are queued on separate player-loop frames, so the running game observes them exactly like real input: `InputSystemUIInputModule` raycasts (including `PhysicsRaycaster` hits on 3D objects), `<Pointer>`/`<Mouse>` action bindings, and code polling `Mouse.current` all react as they would to a genuine click.

> Requires the optional `com.unity.inputsystem` package.
> Can be called only in Play mode and only when the Play Mode category is enabled.
> The response is sent after the final input frame has been consumed — a `tap` takes about 3–4 player frames. Player frames must be advancing (focus the Game view, or set the Input System package's Background Behavior accordingly); otherwise the request times out after 5 seconds.
> Only one pointer operation can run at a time; concurrent requests return `409`.
> Limitations: legacy Input Manager APIs (`Input.GetMouseButton`, `OnMouseDown`, …) do not observe Input System events, and a virtual `Touchscreen` device (EnhancedTouch) is not yet supported. To verify what a coordinate would hit before clicking, use `POST /api/playmode/screen/hittest`.

### Request Body (JSON)

```json
{
  "normalizedPosition": { "x": 0.5, "y": 0.5 },
  "origin": "topLeft",
  "mode": "tap"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `position` | ✅* | Pixel coordinate `{ "x", "y" }` in the Game view (`Screen.width` × `Screen.height`). Out-of-range values return `422` |
| `normalizedPosition` | ✅* | Normalized coordinate `{ "x", "y" }` in `0`–`1`, clamped |
| `origin` | ❌ | `bottomLeft` (default, Unity screen space) or `topLeft`. Use `topLeft` when picking coordinates from `/api/editor/capture` screenshots |
| `mode` | ❌ | `tap` (default), `press`, `release`, or `move` |
| `button` | ❌ | `left` (default), `right`, or `middle` |
| `holdFrames` | ❌ | `tap` only: player frames to hold the button between press and release, `1`–`300` (default `1`) |

*Provide exactly one of `position` or `normalizedPosition`. For `mode: "release"` the coordinate is optional and defaults to the current virtual mouse position.

`press` keeps the button held (released automatically when Play mode ends); pair it with `release` for drags or long presses. `move` only updates the virtual mouse position. The position persists between calls and is also used by `POST /api/playmode/input/perform` for Mouse/Pointer bindings.

### Response

```json
{
  "success": true,
  "mode": "tap",
  "button": "left",
  "position": { "x": 640, "y": 360 },
  "screenSize": { "width": 1280, "height": 720 },
  "pressFrame": 1204,
  "releaseFrame": 1205
}
```

| Field | Description |
|-------|-------------|
| `position` | Resolved pixel coordinate in Unity screen space (bottom-left origin) |
| `screenSize` | Game view resolution the coordinate was resolved against |
| `pressFrame` / `releaseFrame` | `Time.frameCount` when the press / release event was queued (`press` mode omits `releaseFrame`; `move` omits both) |
| `released` | `release` mode only: `false` when the button was not held by a previous `press` |

### Errors

| Status | Cause |
|--------|-------|
| 400 | Both or neither of `position`/`normalizedPosition` given; invalid `origin`, `mode`, `button`, or `holdFrames` |
| 403 | Play Mode category is disabled |
| 409 | Not in Play mode, the editor is paused, another pointer operation is in progress, or Play mode ended during the sequence |
| 422 | Pixel `position` is outside the screen |
| 500 | Player frames did not advance within 5 seconds |

---

## POST /api/playmode/screen/hittest

Read-only: raycasts a screen coordinate and reports what a pointer click there would hit, without sending any input. Combines the active `EventSystem`'s raycast (all raycasters — `GraphicRaycaster` for uGUI, `PhysicsRaycaster` for 3D colliders — honoring their event masks) with a plain `Physics.Raycast` from `Camera.main`. Use it to verify a coordinate before `POST /api/playmode/input/pointer`.

> Can be called only in Play mode and only when the Play Mode category is enabled.
> Does not require the `com.unity.inputsystem` package.

### Request Body (JSON)

```json
{
  "normalizedPosition": { "x": 0.5, "y": 0.5 },
  "origin": "topLeft"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `position` | ✅* | Pixel coordinate `{ "x", "y" }` in the Game view. Out-of-range values return `422` |
| `normalizedPosition` | ✅* | Normalized coordinate `{ "x", "y" }` in `0`–`1`, clamped |
| `origin` | ❌ | `bottomLeft` (default) or `topLeft`. Use `topLeft` when picking coordinates from `/api/editor/capture` screenshots |

*Provide exactly one of `position` or `normalizedPosition`.

### Response

```json
{
  "success": true,
  "position": { "x": 640, "y": 360 },
  "screenSize": { "width": 1280, "height": 720 },
  "eventSystemHits": [
    {
      "path": "Cube",
      "globalObjectId": "GlobalObjectId_V1-...",
      "module": "UnityEngine.EventSystems.PhysicsRaycaster",
      "distance": 9.4
    }
  ],
  "physicsCamera": "Main Camera",
  "physicsHit": {
    "path": "Cube",
    "globalObjectId": "GlobalObjectId_V1-...",
    "distance": 9.4,
    "point": [0.1, 0.5, -2.0]
  }
}
```

| Field | Description |
|-------|-------------|
| `eventSystemHits` | Raycast results in EventSystem order (what a pointer event would hit first); `null` when no `EventSystem` is active |
| `eventSystemHits[].module` | Raycaster type that produced the hit |
| `physicsCamera` | Hierarchy path of `Camera.main`, or `null` when absent |
| `physicsHit` | First `Physics.Raycast` hit from `Camera.main` through the point, or `null` when nothing was hit |

### Errors

| Status | Cause |
|--------|-------|
| 400 | Both or neither of `position`/`normalizedPosition` given, or invalid `origin` |
| 403 | Play Mode category is disabled |
| 409 | Unity Editor is not in Play mode |
| 422 | Pixel `position` is outside the screen, or neither an `EventSystem` nor `Camera.main` exists |

---

## GET /api/playmode/ui/elements

Lists active Unity UI (uGUI) and TextMeshPro UI elements in the loaded scene that can be targeted by the Play Mode UI interaction APIs.

> Can be called only in Play mode and only when the Play Mode category is enabled.
> v1 supports Unity UI and TextMeshPro UI components. `backend` values in responses are reserved for future UI Toolkit support.

### Query Parameters

| Parameter | Required | Description |
|-------------|------|------|
| `scenePath` | ❌ | Loaded scene asset path or unambiguous scene name. If omitted, the active scene is used |

### Response

```json
{
  "backend": "unityUi",
  "elements": [
    {
      "path": "Canvas/StartButton",
      "globalObjectId": "GlobalObjectId_V1-...",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
      "type": "UnityEngine.UI.Button",
      "interactable": true
    },
    {
      "path": "Canvas/NameInput",
      "globalObjectId": "GlobalObjectId_V1-...",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
      "type": "UnityEngine.UI.InputField",
      "interactable": true,
      "text": "Player"
    },
    {
      "path": "Canvas/TMPDropdown",
      "globalObjectId": "GlobalObjectId_V1-...",
      "componentGlobalObjectId": "GlobalObjectId_V1-...",
      "type": "TMPro.TMP_Dropdown",
      "interactable": true,
      "value": 0,
      "optionCount": 3
    }
  ],
  "count": 3
}
```

### Errors

| Status | Cause |
|--------|-------|
| 403 | Play Mode category is disabled |
| 404 | `scenePath` does not match a loaded scene |
| 409 | Not in Play mode, or `scenePath` is ambiguous |

---

## POST /api/playmode/ui/click

Clicks a Unity UI `Button` or a component implementing `IPointerClickHandler`.

If the targeted element itself is not clickable (e.g. the `Text` child of a Button),
the click falls back to the nearest ancestor click handler, mirroring how a real
pointer click bubbles through the raycast. The response reports the component that
actually received the click.

> Can be called only in Play mode and only when the Play Mode category is enabled.
> Requires an active `EventSystem` in the scene. UnionAir does not create one automatically.

### Request Body (JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/StartButton" },
  "backend": "unityUi",
  "normalizedPosition": { "x": 0.5, "y": 0.5 }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `target` | ✅ | Object reference resolving to a GameObject, `Button`, or `IPointerClickHandler` component |
| `backend` | ❌ | `unityUi` (default). Other values are reserved for future UI Toolkit support |
| `scenePath` | ❌ | Loaded scene selector for `hierarchyPath` and `componentPath` targets |
| `normalizedPosition` | ❌ | Pointer position inside the target `RectTransform`, where `{ "x": 0.5, "y": 0.5 }` is the center. Missing or non-numeric coordinates default to `0.5`; values outside `0`–`1` are clamped |

### Response

```json
{
  "success": true,
  "backend": "unityUi",
  "action": "click",
  "path": "Canvas/StartButton",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.UI.Button",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "clicked": true
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | Unsupported `backend`, missing `target`, or `target` is not an ObjectRef JSON object |
| 403 | Play Mode category is disabled |
| 404 | Target or scene was not found |
| 409 | Not in Play mode, no active `EventSystem`, or target is not interactable |
| 422 | Target does not resolve to a click-capable Unity UI element |

---

## POST /api/playmode/ui/text

Sets text on a Unity UI `InputField` or TextMeshPro `TMP_InputField` and optionally submits it.

> Can be called only in Play mode and only when the Play Mode category is enabled.

### Request Body (JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/NameInput" },
  "text": "Player",
  "submit": true
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `target` | ✅ | Object reference resolving to a GameObject, `UnityEngine.UI.InputField`, or `TMPro.TMP_InputField` component |
| `text` | ✅ | Text to assign |
| `submit` | ❌ | When `true`, invokes the input field end-edit callback after setting the value |
| `backend` | ❌ | `unityUi` (default) |
| `scenePath` | ❌ | Loaded scene selector for `hierarchyPath` and `componentPath` targets |

### Response

```json
{
  "success": true,
  "backend": "unityUi",
  "action": "text",
  "path": "Canvas/NameInput",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.UI.InputField",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "text": "Player"
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | Missing `text`, unsupported `backend`, or malformed `target` |
| 403 | Play Mode category is disabled |
| 404 | Target or scene was not found |
| 409 | Not in Play mode, no active `EventSystem`, or target is not interactable |
| 422 | Target does not resolve to a Unity UI `InputField` or `TMP_InputField` |

---

## POST /api/playmode/ui/scroll

Scrolls a Unity UI `ScrollRect` by scroll delta or by setting its normalized position.

> Can be called only in Play mode and only when the Play Mode category is enabled.

### Request Body (JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/List" },
  "delta": { "x": 0, "y": -1 }
}
```

Or set a normalized position directly:

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/List" },
  "normalizedPosition": { "x": 0, "y": 1 }
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `target` | ✅ | Object reference resolving to a GameObject or `UnityEngine.UI.ScrollRect` component |
| `delta` | ❌ | Scroll wheel delta object with `x` and/or `y` values |
| `normalizedPosition` | ❌ | Direct normalized scroll position with `x` and/or `y` values |
| `backend` | ❌ | `unityUi` (default) |
| `scenePath` | ❌ | Loaded scene selector for `hierarchyPath` and `componentPath` targets |

Provide either `delta` or `normalizedPosition`.

### Response

```json
{
  "success": true,
  "backend": "unityUi",
  "action": "scroll",
  "path": "Canvas/List",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.UI.ScrollRect",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "normalizedPosition": { "x": 0, "y": 1 }
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | Missing both `delta` and `normalizedPosition`, unsupported `backend`, or malformed values |
| 403 | Play Mode category is disabled |
| 404 | Target or scene was not found |
| 409 | Not in Play mode, no active `EventSystem`, or target is inactive |
| 422 | Target does not resolve to a Unity UI `ScrollRect` |

---

## POST /api/playmode/ui/value

Sets a semantic value on a Unity UI `Toggle`, `Slider`, `Dropdown`, or TextMeshPro `TMP_Dropdown`.

> Can be called only in Play mode and only when the Play Mode category is enabled.

### Request Body (JSON)

```json
{
  "target": { "type": "hierarchyPath", "value": "Canvas/MusicToggle" },
  "value": true
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `target` | ✅ | Object reference resolving to a GameObject, `Toggle`, `Slider`, `Dropdown`, or `TMP_Dropdown` component |
| `value` | ✅ | Boolean for `Toggle`, number for `Slider`, integer option index for `Dropdown` or `TMP_Dropdown` |
| `backend` | ❌ | `unityUi` (default) |
| `scenePath` | ❌ | Loaded scene selector for `hierarchyPath` and `componentPath` targets |

Slider values outside `[minValue, maxValue]` are clamped to the range and the response
includes `"clamped": true`. Dropdown option indexes out of range are rejected with 400.

### Response

```json
{
  "success": true,
  "backend": "unityUi",
  "action": "value",
  "path": "Canvas/MusicToggle",
  "globalObjectId": "GlobalObjectId_V1-...",
  "component": "UnityEngine.UI.Toggle",
  "componentGlobalObjectId": "GlobalObjectId_V1-...",
  "value": true
}
```

### Errors

| Status | Cause |
|--------|-------|
| 400 | Missing or invalid `value`, unsupported `backend`, or malformed `target` |
| 403 | Play Mode category is disabled |
| 404 | Target or scene was not found |
| 409 | Not in Play mode, no active `EventSystem`, or target is not interactable |
| 422 | Target does not resolve to a supported Unity UI value component |

---
