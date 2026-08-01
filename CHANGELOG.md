# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [0.3.0] - 2026-08-01

### Added

- `POST /api/editor/play` now accepts an optional `inputs` list that schedules frame-accurate input, replayed from the first Play mode frame. `frame` means the frame the game observes the input on — the frame where `wasPressedThisFrame` is true inside the game's `Update()` — not the frame an event was queued. Events sharing a frame are merged into a single state snapshot per virtual device, so chords reach the game as simultaneous presses. Without `inputs` the endpoint is unchanged; with it the response is `202` and carries the replay id. Requires the `com.unity.inputsystem` package.
- Added `GET /api/playmode/input/result` in the always-enabled Read category, reporting the current or most recently finished replay. Each event carries the frame it was actually observed on rather than the frame requested, so a client can prove the schedule held instead of assuming it. The endpoint doubles as the completion signal: it is how a caller knows when inspecting the game's state is meaningful. A failed event is recorded and the replay continues, so `failedCount` matters even when `state` is `completed`.
- The whole `inputs` list is validated before Play mode is entered, and a single invalid entry returns `400` naming its index without arming a replay or leaving Edit mode. Entering Play mode causes a domain reload and the response is sent before the replay runs, so a problem found later could not be reported at all. There is no cap on list length or frame range; `POST /api/editor/stop` ends a runaway replay.
- `POST /api/playmode/input/{perform,set,pointer}` now return `409` while a replay is active. They call `InputSystem.Update()` synchronously, which would flush the replay's queued events outside the player loop and destroy its frame timing.

- Added a Compile API in the always-enabled Read category. `GET /api/compile` returns the in-flight cycle as `current` and the most recently completed Editor cycle as `latest`, and `GET /api/compile/{id}` returns one of the 20 most recently retained records. Diagnostics are structured with `severity`, `code`, project-relative `file`, `line`, and `column` instead of requiring callers to parse Console text.
- Added `POST /api/compile` in the Asset Write category, which requests a compilation and returns `202` with the id to poll. The record is persisted and the response sent before any compilation work starts, because refreshing and compiling block the Editor and can end in a domain reload that drops the connection. An optional caller-supplied `requestId` makes a lost response recoverable, and `409` responses carry `activeCompile` or `existingCompile` so a caller knows to poll rather than retry.
- `GET /api/editor/status` now reports `compileState`, `compileId`, and `compileSource` for an in-flight compilation, mirroring the existing test-run fields.
- Script compilations started outside UnionAir, such as an IDE save followed by Unity's focus auto-refresh, are adopted and recorded with `source: "external"`.
- Compile records distinguish `succeeded` from `upToDate`, so a caller can tell "compiled with no errors" from "Unity reported zero compiled assemblies." Neither result promises whether a domain reload follows; removal-only cycles can report `upToDate` and still reload. Cancelled and interrupted cycles resolve to `aborted` rather than remaining active.

- Unity Console logs are now retained across assembly domain reloads. Every entry is mirrored to an append-only NDJSON file under `Library/UnionAir/Logs`, and the in-memory ring buffer is rehydrated from it after each reload.
- `GET /api/editor/logs` now returns a monotonic `sequence` per entry plus `sessionId`, `oldestSequence`, `latestSequence`, `truncated`, and `hasMore`, and accepts an exclusive `since` cursor so callers can fetch only new entries. The cursor is applied before the `type` and `search` filters, so `truncated` reports lost entries rather than filtered ones.
- `GET /api/editor/status` now reports `sessionId`, `lifecycleGeneration`, `settled`, and `hasCompileErrors`. `lifecycleGeneration` increments on every domain reload, letting a client whose connection dropped confirm that a reload completed rather than that the Editor crashed.

- Added `GET /api/editor/logs.ndjson`, which downloads the retained raw logs for the current Editor session including entries already evicted from the in-memory buffer. Across a size rotation it concatenates the same-session predecessor and active file in oldest-first order; at most these two files are retained.

- Added a disabled-by-default Profiling API for AI-oriented `ProfilerRecorder` discovery and sessions, versioned JSON statistics, frame-level NDJSON, optional Unity Profiler raw captures, and downloadable Memory Profiler snapshots.
- Profiling sessions can be attached atomically to UnionAir EditMode and PlayMode Test Runner runs and report discontinuous segments across assembly reloads.
- Profiling and memory artifacts are stored under `Library/UnionAir`, include project-relative paths, sizes, and SHA-256 hashes, and use bounded count and shared-size retention.

- Added an optional, disabled-by-default Test Runner API when Unity Test Framework is installed: leaf-test discovery, asynchronous EditMode/PlayMode execution, current/latest status, cancellation, and complete NUnit XML download.
- Test Runner results retain only the current run metadata and latest completed UnionAir result. Complete NUnit XML is atomically stored under `Library/UnionAir/TestRuns` and exposed through HTTP for callers that need durable history.
- `GET /api/editor/status` now reports active Unity Test Framework runs and whether they were started by UnionAir or an external tool.
- Endpoint metadata now declares whether each route is allowed during a test run. Active UnionAir and external runs block all endpoints except health, help, editor status/logs, run status/result/cancel, and CORS preflight.
- `POST /api/playmode/input/set` now supports one-shot Mouse scroll deltas through `<Mouse>/scroll`, `<Mouse>/scroll/x`, and `<Mouse>/scroll/y` bindings while preserving the virtual Mouse position and held buttons.

- Added EditMode tests under `Tests/Editor` covering compiler-message parsing, path normalization, compile result and target decisions, retained-record safety, log cursor arithmetic, rotation selection, and bounded multi-file streaming. The test assembly is not compiled in a consumer project unless the project adds `com.leonakasaka.unionair` to `testables` in its manifest.

### Changed

- Lowered the minimum supported Editor version from `6000.0` to `2022.3`. Unity 6000.0 LTS and later remains the primary target and the only version every change is verified against; 2022.3 LTS is now a supported version whose builds and core behavior are verified; 2023.x is best effort. Nothing in the package needed a Unity version guard — the entire codebase already compiled on 2022.3 apart from the Test Runner assembly.
- The Test Runner assembly now requires `com.unity.test-framework` **1.4.0 or later** rather than merely requiring the package to be present. UnionAir uses `TestRunnerApi.RegisterTestCallback`, `SaveResultToFile`, and `CancelTestRun`, all of which were added in 1.4.0. Unity 2022.3 and 2023.1 default to 1.1.33 and 1.3.9, where those APIs do not exist and the assembly previously failed to compile with three `CS0117` errors. The assembly is now skipped on versions below 1.4.0, so such projects compile cleanly; the `/api/tests` and `/api/test-runs` endpoints and the Test Runner category are simply absent. Add `"com.unity.test-framework": "1.4.6"` to a project's `Packages/manifest.json` to enable them. UnionAir does not declare the dependency itself, because the Test Runner category is disabled by default and the upgrade should stay the project's decision.

- **Breaking:** `timestamp` in `GET /api/editor/logs` entries is now UTC ISO 8601 with a `Z` suffix (`2026-05-16T04:12:00.1234567Z`) instead of an offset-free local time, matching the Test Runner and Profiling APIs. Clients that parsed the previous format as local time must be updated.

- Documented that category enablement is stored in `EditorPrefs`, which Unity scopes to the user and Editor version rather than to the project. Enabling a write category for one project enables it for every project opened with the same Editor. The behavior is unchanged; only `README.md` and `SECURITY.md` were previously silent about it.

### Security

- Closed a cross-site request forgery exposure in the unauthenticated localhost bridge. Requests carrying an `Origin` header are now rejected before routing, responses no longer opt into wildcard CORS, and non-empty request bodies require `Content-Type: application/json`. Origin-free local CLI and integration clients remain compatible, including empty POST requests without a content type.

### Fixed

- `POST /api/assets/reimport` now rejects loaded scenes with a structured `409 Conflict` before calling `AssetDatabase.ImportAsset()`, preventing that reimport from opening Unity's interactive Reload dialog and blocking subsequent UnionAir requests. Recursive folder imports report all loaded scene conflicts in Scene Manager order.
- `POST /api/editor/refresh` and `POST /api/compile` with `refresh: true` now detect external changes to loaded scene files from SHA-256 baselines retained across domain reloads. They refuse to call `AssetDatabase.Refresh()` and report the loaded scenes instead of allowing Unity's interactive Reload dialog to block the API.
- Loaded-scene disk baselines are now bootstrapped through the background-safe Editor update pump after scene restoration on a cold Editor start. Initialization no longer prunes persisted baselines from an incomplete early scene list, readiness and file-read retries are bounded, and a failed scene-open/save baseline records one actionable warning instead of silently leaving the scene untracked.
- `DELETE /api/assets/{guid}` now returns a structured `409 Conflict` before deleting a loaded scene or a folder containing loaded scenes, preventing the open scene from losing its backing asset.
- Request-body fields are now read correctly from pretty-printed JSON. The body reader skipped only spaces and tabs after a field's colon, so a value placed on the following line was reported as absent — every endpoint using `GetString`, `GetInt`, `GetFloat`, or `GetBool` was affected.
- Reading a JSON array is now anchored to its own field. A non-array value such as `"operations": null` previously made the reader scan forward and adopt an unrelated later array in the same body, silently processing the wrong elements.
- `POST /api/playmode/input/set` now reads `value` through the shared array reader, which no longer matches a `"value"` key nested inside another object or inside a string literal. A Vector2 `value` must now hold exactly two numbers; a longer array was previously accepted and silently truncated to its first two elements.
- Screen-coordinate endpoints now reject a `position` or `normalizedPosition` that is present but not an object, such as `"position": null`. The value previously read as an absent field, which mattered most for `POST /api/playmode/input/pointer` with mode `release`, where an absent coordinate means "use the current position" — a malformed coordinate silently released somewhere else instead of failing.

- Prevented mixed player or custom compilation cycles from being classified as Editor cycles and replacing `latest`, and re-evaluated retained records with the conservative target rules at startup.
- Prevented caller-supplied compile ids such as Windows device names from being accepted when their per-id record cannot be persisted safely.
- Made Console NDJSON downloads include the same-session rotated predecessor instead of making entries inaccessible as soon as the active file crossed the rotation threshold.
- Kept compile record retention going past a record that cannot be deleted, instead of abandoning the remaining trim and letting the retained set grow without bound.
- Backed off log rotation by size after a failed attempt. A rotation blocked by a concurrent NDJSON download previously left the file over the threshold, so every following Console message closed, retried, and reopened the log writer.
- Stopped rebuilding `latest` from retained records on every domain reload once a scan has already found no eligible completed Editor cycle. Skipping the rescan no longer keeps a stale non-Editor record as `latest`, the removal of that record is retried on every reload until it succeeds, and a scan interrupted by an I/O error is retried instead of being treated as proof that nothing eligible exists.
- Moved persistence of log rotation state onto the Editor update loop so that `GET /api/editor/logs.ndjson` no longer writes session state as a side effect of a read.
- Stopped a multi-file artifact transfer when a stream ends before its announced length, rather than appending the next file onto a partial record.
- Prevented domain reloads from orphaning an `HttpListener` by retaining listener ownership until bounded thread and queued-response cleanup completes.
- Added up to five bounded retries for transient address-in-use failures during automatic startup, without publishing intermediate failures to the Console or logs API.
- Added up to three delayed recovery attempts for unexpected listener thread exits, with cleanup completed before the bounded lifecycle trace is dumped and concise errors kept separate from the once-per-domain trace.
- Prevented failed profiling restoration from leaving undeletable active sessions or owned Profiler settings behind.
- Moved profiling artifact downloads off the Unity Editor update loop and cached finalized session statistics instead of reparsing NDJSON on every status request.
- Made active NDJSON downloads share-safe and length-bounded, handled background-transfer queue failures, and limited sessions to 64 metrics to bound memory use.
- Active object searches now select the Unity 6000.4 no-sort-parameter API while retaining the established overload on earlier Unity 6 releases, removing new obsolete warnings without raising the minimum supported Unity version.
- Test Runner request filters now use the shared top-level JSON parser, avoiding false matches for filter names embedded inside string values.
- Test-run progress metadata is now persisted at a bounded interval and at lifecycle boundaries instead of performing an atomic disk write for every test callback.
- Unity Test Framework active-run inspection now uses a cached delegate at a bounded polling interval, reports compatibility failures, and safely rejects new runs when concurrency cannot be verified.
- Test execution and discovery now share a correctly created, domain-scoped `TestRunnerApi` ScriptableObject instead of constructing an undisposed instance for each request.
- Test-run source identifiers, public run IDs, nullable JSON string formatting, and the EditorWindow's core category order now each use a single definition to prevent silent drift between API responses and UI rendering.
- Test-run gates now reconcile in both directions after a grace period when Unity Test Framework is positively observed as idle, preventing missed completion callbacks from leaving external or UnionAir runs locked until an Editor restart.
- Latest NUnit XML and metadata now use a recoverable transaction and SHA-256 integrity check, preventing a crash between file replacements from serving one run's XML under another run ID.
- `POST /api/playmode/input/perform` now resolves Keyboard bindings through the virtual device's actual `KeyControl`, preventing top-row numeric paths such as `<Keyboard>/1` from being parsed as numeric `Key` enum values and pressing unrelated keys.

## [0.2.0] - 2026-07-16

### Added

- `PATCH /api/assets/texture-importer/{guid}` — updates texture import settings (`textureType`, `spriteMode`, `pixelsPerUnit`) and calls `SaveAndReimport()`. Required for converting Texture2D assets to Sprite type before using them in object reference curves.
- `POST /api/assets/animation-clips` — creates an AnimationClip asset with optional `frameRate` and `wrapMode`.
- `GET /api/assets/animation-clips/{guid}` — returns clip metadata, all float curves, and all object reference curves (sprite-swap style).
- `POST /api/assets/animation-clips/{guid}/curves` — adds or replaces float curves (`curves`) and object reference curves (`objectReferenceCurves`) in a single call. For Sprite-mode textures, the Sprite sub-asset is loaded automatically via `AssetDatabase.LoadAssetAtPath<Sprite>` to avoid type mismatches with `m_Sprite`.
- `DELETE /api/assets/animation-clips/{guid}/curves` — removes curves by binding. Automatically selects `AnimationUtility.SetObjectReferenceCurve(clip, binding, null)` for PPtr bindings and `clip.SetCurve(..., null)` for float bindings.
- `POST /api/assets/animator-controllers` — creates an AnimatorController asset (includes a default Base Layer).
- `GET /api/assets/animator-controllers/{guid}` — returns the full controller structure: parameters, layers, states (with motion and transitions), and any-state transitions.
- `POST /api/assets/animator-controllers/{guid}/parameters` — adds or replaces a Float/Int/Bool/Trigger parameter with an optional default value.
- `DELETE /api/assets/animator-controllers/{guid}/parameters` — removes a parameter by name.
- `POST /api/assets/animator-controllers/{guid}/layers` — adds a layer with optional weight.
- `POST /api/assets/animator-controllers/{guid}/states` — adds a state with optional motion GUID, speed, and default-state flag.
- `PATCH /api/assets/animator-controllers/{guid}/states` — updates an existing state (rename, motion, speed, default).
- `DELETE /api/assets/animator-controllers/{guid}/states` — removes a state by name.
- `POST /api/assets/animator-controllers/{guid}/transitions` — adds a transition; supports `AnyState` as source and `Exit` as destination; accepts `hasExitTime`, `duration`, `offset`, and a `conditions` array with `If`/`IfNot`/`Greater`/`Less`/`Equals`/`NotEqual` modes.
- `PATCH /api/assets/animator-controllers/{guid}/transitions` — updates an existing transition identified by `from`/`to` names.
- `DELETE /api/assets/animator-controllers/{guid}/transitions` — removes a transition identified by `from`/`to` names.
- `POST /api/gameobjects/instantiate` — instantiates a prefab asset into a loaded scene while preserving the prefab connection, with optional `name`, `parent`, and `scenePath`.
- `GET /api/playmode/input/actions` — lists enabled Unity Input System actions in the running game, including action type and effective binding paths.
- `POST /api/playmode/input/perform` — performs a Button InputAction by name through a UnionAir virtual device during Play mode.
- `POST /api/playmode/input/set` — sets Axis, Vector2, and Stick InputAction values on supported virtual Gamepad controls. Values remain active until another set call or Play mode cleanup.
- `GET /api/playmode/ui/elements` — lists active Unity UI and TextMeshPro UI elements that can be targeted during Play mode.
- `POST /api/playmode/ui/click` — clicks a Unity UI `Button` or `IPointerClickHandler` target during Play mode.
- `POST /api/playmode/ui/text` — sets text on a Unity UI `InputField` or TextMeshPro `TMP_InputField` and optionally submits it during Play mode.
- `POST /api/playmode/ui/scroll` — scrolls a Unity UI `ScrollRect` by wheel delta or normalized position during Play mode.
- `POST /api/playmode/ui/value` — sets a Unity UI `Toggle`, `Slider`, `Dropdown`, or TextMeshPro `TMP_Dropdown` value during Play mode.
- `POST /api/playmode/input/pointer` — simulates a mouse click/press/release/move at a screen coordinate through the virtual mouse, spreading the phases across real player frames so the game's own raycast-based hit detection (`PhysicsRaycaster`, `Mouse.current` polling) reacts like it would to genuine input.
- `POST /api/playmode/screen/hittest` — read-only raycast at a screen coordinate (EventSystem raycasters + `Physics.Raycast` from `Camera.main`) reporting what a pointer click there would hit.
- `GET /api/help` — attribute-generated API manifest for LLMs, MCP bridges, and tools that cannot access the documentation directly
- ASP.NET-style attribute routing with `[UnionAirController]` and `[UnionAirEndpoint]` as the source of truth for routing, help, category state, and the EditorWindow endpoint list
- Custom handler discovery under `/api/custom/...`, managed separately in the UnionAir EditorWindow
- Category-level API enablement metadata and endpoint risk reporting for built-in and custom API discovery
- Multi-scene API support: `GET /api/scenes`, `POST /api/scenes/new`, `POST /api/scenes/open`, `POST /api/scenes/unload`, and `POST /api/scenes/active`.
- Custom controller authors can now reuse UnionAir's scene, object reference, GlobalObjectId, and asset reference resolution through the public `UnionAirReferenceResolver` helper.
- `Documentation~/custom-controllers.md` with setup, request parsing, reference resolution, Play Mode policy, and security guidance for custom API implementers.
- `POST /api/editor/menu-item` can now execute Unity Editor menu items through a disabled-by-default Editor Actions category.
- `GET /api/editor/selection`, `POST /api/editor/selection`, and `POST /api/editor/ping` expose Unity Editor selection and object highlighting operations.
- `POST /api/assets/open` opens project assets in the Unity Editor, and `POST /api/assets/reimport` reimports individual assets.
- `GET /api/assets/scriptableobjects`, `GET /api/assets/scriptableobjects/{guid}`, `POST /api/assets/scriptableobjects`, `PATCH /api/assets/scriptableobjects`, and `DELETE /api/assets/scriptableobjects/{guid}` — full CRUD for ScriptableObject assets using runtime reflection and `SerializedObject`, supporting any project-defined ScriptableObject subclass without package changes.
- `GET /api/editor/menu-items` lists discoverable Unity Editor menu paths for use with `POST /api/editor/menu-item`.
- `GET /api/editor/capture` and `GET /api/editor/capture/image` — capture the current view without specifying a camera. In Play mode, reads the composited GameView render texture via reflection, including Screen Space Overlay Canvas UI; falls back to `ScreenCapture.CaptureScreenshotAsTexture()` if reflection is unavailable. In Edit mode, renders the last active Scene View camera. Both endpoints accept optional `width`, `height`, `format`, and `quality` query parameters.

### Fixed

- ObjectRef body fields now distinguish a malformed scalar value from a missing field and return an actionable 400 error with the required object shape. `GET /api/help` also describes the required `target` shape for `POST /api/playmode/ui/click`.
- `GET /api/editor/logs` now matches the `type` filter case-insensitively and returns 400 for unknown values instead of silently returning every log type.
- `POST /api/assets/reimport` now accepts an existing project-relative `assetPath` even when Unity has not registered its main asset type or GUID yet, and returns the GUID assigned by the import.
- Restored compatibility with earlier Unity 6 releases by using the established `FindObjectsByType` overload and object-based asset-label lookup.
- `POST /api/playmode/ui/value` now reports `"clamped": true` when a Slider value was clamped to `[minValue, maxValue]`, instead of silently returning the adjusted value.
- `POST /api/playmode/ui/click` on a non-clickable child (e.g. a Button's `Text` label) now falls back to the nearest ancestor `IPointerClickHandler` instead of returning 422, matching how a real pointer click bubbles through the raycast.
- `POST /api/playmode/ui/text` and `/value` no longer report `success:true` when the reflective TMP `text`/`value` setter or requested submit events cannot be found; they return 500 instead. A missing `RefreshShownValue` now logs a warning instead of being silently skipped.
- Play Mode UI endpoints now recognize subclasses of `TMP_InputField` and `TMP_Dropdown`: TMP types are resolved once per domain load and matched with `IsInstanceOfType` instead of exact type-name comparison. `GET /api/playmode/ui/elements` also lists such subclasses, reports each element's actual component type in `type`, and no longer scans every scene component when looking for TMP elements (skipped entirely when TextMeshPro is not installed).
- Short type names such as `Slider`, `Toggle`, and `Button` no longer resolve to same-named non-matching classes (e.g. `UnityEngine.UIElements.Slider`). Type resolution for `componentPath` references, component add/remove, ScriptableObject creation, and object reference `assetType` now filters candidates by the required base type and keeps scanning instead of returning the first name match.
- `POST /api/playmode/input/perform` now handles Gamepad trigger Button bindings as trigger axis state instead of button bitfield flags, validates Vector2 values as finite numbers, and preserves held Button records when `tap` is called while the same action is pressed.
- `POST /api/playmode/input/perform` now sends full virtual device state for Button actions instead of delta events against bitfield controls, avoiding Keyboard `Key` delta-state failures.
- `POST /api/assets/animation-clips/{guid}/curves` — `objectReferenceCurves` keys now load `Sprite` sub-assets via `AssetDatabase.LoadAssetAtPath<Sprite>` instead of `LoadMainAssetAtPath`. The previous behavior returned a `Texture2D` for Sprite-mode PNGs, causing a type mismatch in `m_Sprite` that crashed Unity during animation preview.
- `DELETE /api/assets/animation-clips/{guid}/curves` — bindings that match existing object reference (PPtr) curves are now removed with `AnimationUtility.SetObjectReferenceCurve(clip, binding, null)` instead of `clip.SetCurve(..., null)`, which silently did nothing for PPtr bindings.
- Hardened JSON string escaping in API responses to correctly encode control characters and prevent malformed JSON output from string fields.
- Normalized non-finite float values (`NaN`, `Infinity`, `-Infinity`) to `null` in JSON responses to keep numeric fields JSON-compliant.
- Replaced non-ASCII dash and arrow characters in API-visible string literals with ASCII equivalents; on machines whose system code page is not UTF-8 (e.g. Japanese Windows), the compiler could misread the BOM-less source files and emit mojibake in `/api/help` summaries and error messages.

### Changed

- Documented that bodyless POST requests are supported and must be framed with `Content-Length: 0` so Windows `HttpListener` does not reject them with 411 before routing.
- `POST /api/editor/refresh` guidance now requires both asset updating and script compilation to become idle, including reconnecting after a domain reload, before clients make dependent calls.
- `POST /api/playmode/input/perform` and `/set` now accept `Map/Action` identifiers. Bare action names remain supported when unique and return 409 with candidate identifiers when ambiguous.
- `POST /api/assets/animation-clips/{guid}/curves` — `curves` is no longer declared as a required body field; `objectReferenceCurves` alone is now a valid payload.
- `POST /api/playmode/input/perform` is now Button-only with `mode` (`tap`, `press`, `release`) instead of `value`; Axis, Vector2, and Stick values use `POST /api/playmode/input/set`.
- Clarified that `GET /api/editor/capture` and `GET /api/editor/capture/image` resize the captured GameView frame in Play mode instead of re-rendering the GameView at the requested `width` and `height`.
- `PATCH /api/gameobjects/components` can now set and clear serialized object references, including scene GameObjects, Components, and assets such as TextAsset.
- Existing scene, search, GameObject, component, and prefab APIs now accept optional `scenePath` targeting for loaded scenes.
- Scene GameObject and Component APIs now expose `globalObjectId` values and accept them as stable target identifiers.
- GameObject, component, camera, and prefab write APIs now use typed object references (`target`, `parent`, `source`) instead of parallel path and ID fields.
- Serialized component object reference payloads now use typed `hierarchyPath`, `componentPath`, and `globalObjectId` references.
- Endpoint metadata now declares Play Mode safety policy, and write APIs are centrally blocked or require both Editor-side Play Mode scene-change permission and `allowWhilePlaying=true` while the Editor is in Play Mode.
- Play Mode scene-object writes now skip scene dirty marking and Undo registration because they are transient runtime changes.
- Endpoint risk metadata now includes `requestDependent` for APIs whose side effects depend on request parameters.
- Endpoint risk metadata now includes `editorState`, and endpoints can override their category risk when they have a narrower side-effect profile.
- Built-in and custom API endpoint lists in the EditorWindow can now expand and collapse by category.
- `SerializedPropertySerializer` utility extracted from `ComponentWriteHandler` and extended with a read direction for reuse across asset and component property serialization.
- `AssetUtils.EnsureDirectory` extracted from `MaterialWriteHandler` as a shared asset file system utility.

## [0.1.0] - 2026-05-17

### Added

#### Read API

- `GET /api/health` — health check
- `GET /api/scene` — current scene info (name, path, isDirty, rootCount)
- `GET /api/scene/hierarchy` — full GameObject tree with transform data (supports `?depth`, `?compact`, `?limit`, `?path`)
- `GET /api/scene/stats` — scene statistics (object counts, component/tag/layer breakdown)
- `GET /api/gameobjects` — GameObject details with serialized component properties
- `GET /api/editor/status` — Editor state (isPlaying, isPaused, isCompiling, isUpdating)
- `GET /api/editor/logs` — console log capture with type/search/limit filters
- `GET /api/cameras` — camera list with depth, FOV, and path
- `GET /api/cameras/capture` — render camera to base64 image (JPEG/PNG)
- `GET /api/cameras/capture/image` — render camera as binary image stream
- `GET /api/assets` — asset list with path/type/search filters
- `GET /api/assets/{guid}` — asset detail with dependencies and labels
- `GET /api/assets/dependents` — reverse dependency lookup
- `GET /api/search/gameobjects` — multi-criteria GameObject search
- `GET /api/search/asset-refs` — find scene references to an asset

#### Scene Write API (disabled by default)

- `POST /api/gameobjects` — create a new empty GameObject
- `POST /api/gameobjects/primitive` — create a primitive GameObject (Cube, Sphere, Capsule, Cylinder, Plane, Quad)
- `DELETE /api/gameobjects` — delete a GameObject
- `PATCH /api/gameobjects` — update GameObject properties (name, isActive, tag, layer, transform)
- `POST /api/gameobjects/duplicate` — duplicate a GameObject
- `POST /api/gameobjects/reparent` — move a GameObject to a new parent
- `POST /api/gameobjects/batch` — bulk create/update/delete in a single Undo group (HTTP 207)
- `POST /api/gameobjects/components` — add a component to a GameObject
- `DELETE /api/gameobjects/components` — remove a component from a GameObject
- `PATCH /api/gameobjects/components` — update serialized component properties
- `POST /api/scene/save` — save the current scene to disk

#### Asset Write API (disabled by default, separate toggle)

- `POST /api/editor/refresh` — trigger `AssetDatabase.Refresh()`
- `POST /api/assets/prefabs` — create a prefab from a scene GameObject
- `POST /api/assets/prefabs/apply` — apply instance overrides to the prefab asset
- `POST /api/assets/prefabs/revert` — revert a prefab instance to match the asset
- `POST /api/assets/materials` — create a new material
- `PATCH /api/assets/materials` — update material properties (Color, Float, Vector, Texture)
- `DELETE /api/assets/{guid}` — delete an asset and its `.meta` file
- `POST /api/assets/move` — move/rename an asset preserving GUID and references

#### Play Mode API (disabled by default, separate toggle)

- `POST /api/editor/play` — enter play mode
- `POST /api/editor/stop` — exit play mode
- `POST /api/editor/pause` — set or toggle pause state
- `POST /api/editor/step` — advance one frame (requires pause)

#### Infrastructure

- HTTP server via `HttpListener` (no external dependencies), default port 8765
- CORS headers (`Access-Control-Allow-Origin: *`) for cross-origin access
- Per-phase permission gating (Write / Asset Write / Play Mode toggles)
- EditorWindow UI for server control, port configuration, and request log
- Auto-start on Editor load via `[InitializeOnLoad]`
- Graceful shutdown on domain reload; auto-restart after domain reload and play mode exit
- Console log capture with 1000-entry ring buffer (`LogStore`)

<!-- 0.1.0 and 0.2.0 predate this repository being published and were never tagged,
     so they have no release page to link to. -->
[Unreleased]: https://github.com/LeonAkasaka/UnionAir/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/LeonAkasaka/UnionAir/releases/tag/v0.3.0
