# API Reference
**English** | [日本語](api-reference.ja.md)

Base URL: `http://localhost:<port>/api/`. There is no fixed default port: the port mode defaults to Automatic, so read the concrete URL from `<project>/.unionair/endpoint.txt` at connection time and reread it after a refused connection. [Check the server](index.md#2-check-the-server) describes the handshake.

Responses are returned with `Content-Type: application/json; charset=utf-8` unless an endpoint documents another media type. NUnit result downloads use `application/xml`. Responses do not include CORS opt-in headers. Requests carrying an `Origin` header are rejected with `403`, so browser `fetch` and XMLHttpRequest clients are unsupported by default; local CLI and integration clients must omit `Origin`.
String fields in JSON responses are escaped consistently, including control characters.
Non-finite floating-point values (`NaN`, `Infinity`, `-Infinity`) are emitted as `null` in JSON numeric fields.

Every request with a non-empty body must use `Content-Type: application/json`; media-type parameters such as `charset=utf-8` are accepted. Other or missing content types return `415`. Empty requests do not require a content type.

POST endpoints whose body is optional or unused accept an empty body. Clients must frame an empty POST with `Content-Length: 0`; Windows `HttpListener` may reject a POST that has neither `Content-Length` nor `Transfer-Encoding` with `411 Length Required` before UnionAir receives it. Standard HTTP libraries and `curl -X POST` normally add the zero-length header automatically.

Errors are returned as JSON: `{"error":"<message>"}` with an appropriate HTTP status code. Endpoints in a disabled category normally return `403`. While a test run is active, the test-run lock is evaluated first, so a non-allowed endpoint returns `409` with `activeTestRun` even when its category is disabled; a retry after the run may then return `403`.

A `409` caused by the Editor being busy also carries an `activeActivity` object naming the activity, its source, and the id that owns it, and every endpoint declares what blocks it as `blockedDuring` in `GET /api/help`. See [Editor Activities](api/activities.md).

The machine-readable manifest for all endpoints is available at [`GET /api/help`](api/general.md#get-apihelp).

---

## Categories

Endpoints are grouped into categories that can be enabled or disabled in the UnionAir EditorWindow (**Window > UnionAir > REST Bridge**). Only **Read** is always enabled; every other category is disabled by default.

| Category | ID | Default |
|----------|----|---------|
| Read | `read` | Enabled (cannot be disabled) |
| Scene Write | `sceneWrite` | Disabled |
| Asset Write | `assetWrite` | Disabled |
| Play Mode | `playMode` | Disabled |
| Editor Actions | `editorActions` | Disabled |
| Test Runner | `testRunner` | Disabled; present only with Unity Test Framework |
| Profiling | `profiling` | Disabled |
| Build | `build` | Disabled |
| Custom | `custom` | Per custom category |

---

## Pages

### [General](api/general.md)

`GET /api/help` · `GET /api/health` · Custom Controllers overview · **Object References** (the typed reference shape used by all write and detail APIs)

### [Editor](api/editor.md)

Editor state, logs, selection, cameras, capture, refresh, and menu items:

`GET /api/editor/status` · `GET /api/editor/logs` · `GET /api/editor/logs.ndjson` · `GET|POST /api/editor/selection` · `POST /api/editor/ping` · `GET /api/cameras` · `GET /api/cameras/capture` · `GET /api/cameras/capture/image` · `POST /api/editor/refresh` · `GET /api/editor/menu-items` · `POST /api/editor/menu-item` · `GET /api/editor/capture` · `GET /api/editor/capture/image`

### [Scenes](api/scenes.md)

Scene info, hierarchy, multi-scene management, statistics, and saving:

`GET /api/scene` · `GET /api/scene/hierarchy` · `GET /api/scenes` · `POST /api/scenes/new` · `POST /api/scenes/open` · `POST /api/scenes/unload` · `POST /api/scenes/active` · `GET /api/scene/stats` · `POST /api/scene/save`

### [GameObjects & Components](api/gameobjects.md)

GameObject read, search, create/update/delete, and component operations:

`GET /api/gameobjects` · `GET /api/search/gameobjects` · `POST|DELETE|PATCH /api/gameobjects` · `POST /api/gameobjects/primitive` · `POST /api/gameobjects/instantiate` · `POST /api/gameobjects/duplicate` · `POST /api/gameobjects/reparent` · `POST /api/gameobjects/batch` · `POST|DELETE|PATCH /api/gameobjects/components`

### [Assets](api/assets.md)

Asset browsing, search, prefabs, materials, ScriptableObjects, and importers:

`GET /api/assets` · `GET /api/assets/{guid}` · `GET /api/search/asset-refs` · `GET /api/assets/dependents` · `POST /api/assets/prefabs` (+ `apply`, `revert`) · `POST|PATCH /api/assets/materials` · `DELETE /api/assets/{guid}` · `POST /api/assets/move` · `POST /api/assets/open` · `POST /api/assets/reimport` · ScriptableObject CRUD (`/api/assets/scriptableobjects`) · `PATCH /api/assets/texture-importer/{guid}` · `GET|PATCH /api/assets/audio-importer/{guid}`

### [Animation](api/animation.md)

AnimationClip and AnimatorController authoring:

`POST /api/assets/animation-clips` · `GET /api/assets/animation-clips/{guid}` · `POST|DELETE .../curves` · `POST /api/assets/animator-controllers` · `GET /api/assets/animator-controllers/{guid}` · parameters / layers / states / transitions sub-endpoints

### [Play Mode](api/playmode.md)

Play mode control, Input System simulation, screen queries, and UI interaction:

`POST /api/editor/play` · `POST /api/editor/stop` · `POST /api/editor/pause` · `POST /api/editor/step` · `GET /api/playmode/input/actions` · `POST /api/playmode/input/perform` · `POST /api/playmode/input/set` · `POST /api/playmode/input/pointer` · `GET /api/playmode/input/result` · `POST /api/playmode/screen/hittest` · `GET /api/playmode/ui/elements` · `POST /api/playmode/ui/click` · `POST /api/playmode/ui/text` · `POST /api/playmode/ui/scroll` · `POST /api/playmode/ui/value`

### [Compile](api/compile.md)

Structured script compilation results, including cycles started outside UnionAir:

`POST /api/compile` · `GET /api/compile` · `GET /api/compile/records` · `GET /api/compile/{id}`

### [Test Runner](api/testing.md)

Unity Test Framework discovery, asynchronous execution, monitoring, cancellation, and NUnit XML download:

`GET /api/tests` · `POST /api/test-runs` · `GET /api/test-runs/{id}` · `DELETE /api/test-runs/{id}` · `GET /api/test-runs/{id}/results.xml`

### [Profiling](api/profiling.md)

ProfilerRecorder metrics, NDJSON samples, Profiler raw captures, Memory Profiler snapshots, and Test Runner attachment:

`GET /api/profiling/metrics` · `POST|GET /api/profiling/sessions` · `GET|DELETE /api/profiling/sessions/{id}` · `POST .../{id}/stop` · `GET .../{id}/samples.ndjson` · `GET .../{id}/profile.raw` · `POST|GET /api/memory-snapshots` · `GET|DELETE /api/memory-snapshots/{id}` · `GET .../{id}/snapshot`

### [Editor Activities](api/activities.md)

The shared vocabulary for "the Editor is busy with X": what the activities are, how a `409` names one, and what `blockedDuring` means.

### [Build](api/build.md)

Build configuration, platform module availability, persistent settings changes, build target switching, and player builds:

`GET|PATCH /api/build/settings` · `GET /api/build/targets` · `POST /api/build/scenes` · `POST|GET /api/build/target` · `GET /api/build/target/{id}` · `POST /api/builds` · `GET /api/builds` · `GET /api/builds/{id}` · `DELETE /api/builds/{id}`

---

## Related Documentation

- [Getting Started](index.md) — setup, EditorWindow guide, lifecycle
- [Custom Controllers](custom-controllers.md) — adding application-side endpoints under `/api/custom/...`
