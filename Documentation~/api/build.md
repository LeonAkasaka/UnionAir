# API Reference — Build
**English** | [日本語](build.ja.md)

Base URL: `http://localhost:<port>/api/` (default port: **8765**). See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

These endpoints report how the project is configured to build: which target is active, which scenes are enabled and what build index each one gets, which scripting backend and define symbols apply, and which platform modules the Editor actually has installed.

A client needs this to interpret a compilation result. A change that compiles in the Editor can still fail for a player target, because the scripting backend, stripping level, and define symbols differ per target — and none of that is legible from the project directory. `ProjectSettings/ProjectSettings.asset` stores scripting settings in an internal layout keyed by platform id, the active build target is per-user Editor state, and which modules are installed is a property of the *Editor*, not of the project, so no file in the project records it at all.

> These endpoints belong to the **Build** category, which is **disabled by default**. The category risk is `executableOutput`, because it is the category that will also produce player builds. The two endpoints on this page are themselves read-only and report `risk: ["readOnly"]` in `GET /api/help`.

Both endpoints are allowed in Play mode and during a test run. They read configuration and cannot disturb either, and a client diagnosing a failing run is exactly when it needs them.

---

## GET /api/build/settings

Returns the build configuration.

| Query | Default | Description |
|-------|---------|-------------|
| `namedBuildTarget` | active | Named build target whose scripting settings are reported, for example `Standalone`, `Android`, `WebGL`, or `Server`. Case-insensitive |

Only the `scripting` object depends on `namedBuildTarget`. Everything else describes the Editor's current state and does not change with it.

### Response

```json
{
  "activeBuildTarget": "StandaloneWindows64",
  "activeBuildTargetGroup": "Standalone",
  "activeNamedBuildTarget": "Standalone",
  "selectedBuildTargetGroup": "Standalone",
  "standaloneBuildSubtarget": "Player",
  "activeBuildTargetInstalled": true,
  "scenes": [
    {
      "path": "Assets/Scenes/SampleScene.unity",
      "guid": "99c9720ab356a0642a771bea13969a05",
      "enabled": true,
      "buildIndex": 0
    }
  ],
  "sceneCount": 1,
  "enabledSceneCount": 1,
  "scripting": {
    "namedBuildTarget": "Standalone",
    "scriptingBackend": "Mono2x",
    "apiCompatibilityLevel": "NET_Standard_2_0",
    "il2CppCompilerConfiguration": "Release",
    "managedStrippingLevel": "Disabled",
    "defineSymbolsRaw": "",
    "defineSymbols": []
  },
  "options": {
    "development": false,
    "allowDebugging": false,
    "connectProfiler": false,
    "buildWithDeepProfilingSupport": false,
    "waitForManagedDebugger": false
  },
  "player": {
    "productName": "TestUnity6",
    "companyName": "DefaultCompany",
    "bundleVersion": "0.1.0",
    "unityVersion": "6000.0.80f1"
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `activeBuildTarget` | string | `BuildTarget` the Editor currently builds for |
| `activeBuildTargetGroup` | string | `BuildTargetGroup` of the active target |
| `activeNamedBuildTarget` | string | Named build target the active configuration resolves to |
| `selectedBuildTargetGroup` | string | Group selected in the Build Settings window; can differ from the active one while a switch is pending |
| `standaloneBuildSubtarget` | string | `Player` or `Server` for Standalone targets |
| `activeBuildTargetInstalled` | bool | Whether the platform module for the active target is installed. `false` means a build request will fail |
| `scenes` | array | `EditorBuildSettings.scenes` in list order, including disabled entries |
| `sceneCount` / `enabledSceneCount` | number | Total entries and entries that will ship |
| `scripting` | object | Scripting settings for the requested named build target |
| `options` | object | Build flags from `EditorUserBuildSettings` |
| `player` | object | Product identity from `PlayerSettings`, plus the Editor version |

### Scene Fields

| Field | Type | Description |
|-------|------|-------------|
| `path` | string | Project-relative scene asset path |
| `guid` | string | Scene asset GUID |
| `enabled` | bool | Whether the entry is checked in Build Settings |
| `buildIndex` | number \| null | Index the scene will have at runtime, or `null` when the entry is disabled |

`buildIndex` counts **enabled entries only**. A disabled entry occupies a row in the list but no build index, so its position in `scenes` is not what `SceneManager.LoadScene(int)` will load. Reporting `null` rather than the list position is the point of this field.

### Scripting Fields

| Field | Type | Description |
|-------|------|-------------|
| `namedBuildTarget` | string | Named build target these values were read for |
| `scriptingBackend` | string | `Mono2x` or `IL2CPP` |
| `apiCompatibilityLevel` | string | .NET profile, such as `NET_Standard_2_0` |
| `il2CppCompilerConfiguration` | string | `Debug`, `Release`, or `Master` |
| `managedStrippingLevel` | string | `Disabled`, `Low`, `Medium`, or `High`; reported as `Minimal` on Editors that name it so |
| `defineSymbolsRaw` | string | Unity's stored define symbol string, verbatim |
| `defineSymbols` | array | The same symbols split and trimmed |

Both forms are returned because Unity stores whatever the Inspector was given. Empty entries and stray whitespace occur in real projects; `defineSymbols` drops them, and `defineSymbolsRaw` is what a write would have to preserve.

These are the project's **own** define symbols. Unity's built-in `UNITY_*` symbols are not included — they are not stored in project settings and cannot be changed.

A value that this Editor cannot report for the requested target — which happens when the platform module is missing — is returned as an empty string rather than failing the whole response. That case is precisely the one a client asks about before requesting a build, so the rest of the answer is worth more than the error.

### Status Codes

`400` when `namedBuildTarget` is not a name this Editor defines. The message lists the names that are valid on this Editor:

```json
{
  "error": "Unknown namedBuildTarget 'Bogus'. Known values for this Editor: Android, EmbeddedLinux, LinuxHeadlessSimulation, Nintendo Switch, PS4, PS5, QNX, Server, Standalone, VisionOS, WebGL, Windows Store Apps, XboxOne, iPhone, tvOS."
}
```

The set differs per Editor version and per installed modules, which is why the endpoint reports it instead of documenting a fixed list. Names containing spaces must be URL-encoded.

```bash
curl http://localhost:8765/api/build/settings
curl "http://localhost:8765/api/build/settings?namedBuildTarget=Android"
```

---

## GET /api/build/targets

Lists the build targets this Unity installation defines, and whether each one's platform module is installed.

| Query | Default | Description |
|-------|---------|-------------|
| `installed` | `false` | `true` to list only targets whose module is installed |

`total` and `installedCount` always describe the full catalog, so a filtered response still reports how much was filtered out.

### Response

```json
{
  "activeBuildTarget": "StandaloneWindows64",
  "total": 21,
  "installedCount": 2,
  "installedOnly": false,
  "targets": [
    {
      "buildTarget": "Android",
      "buildTargetGroup": "Android",
      "namedBuildTarget": "Android",
      "installed": false,
      "isActive": false
    },
    {
      "buildTarget": "StandaloneWindows64",
      "buildTargetGroup": "Standalone",
      "namedBuildTarget": "Standalone",
      "installed": true,
      "isActive": true
    }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `buildTarget` | string | `BuildTarget` enum name |
| `buildTargetGroup` | string | `BuildTargetGroup` the target belongs to |
| `namedBuildTarget` | string | Name accepted by `GET /api/build/settings?namedBuildTarget=` |
| `installed` | bool | Whether the Editor has the module needed to build it |
| `isActive` | bool | Whether this is the active build target |

The catalog is read from the Editor at request time rather than hard-coded, so targets Unity adds in a later version appear without a UnionAir change, and targets Unity retires disappear. Retired targets are omitted: Unity keeps them in the enum as deprecated members, and offering a name that cannot be built would be worse than not listing it.

Several targets share one group and one named build target — `StandaloneWindows64`, `StandaloneWindows`, `StandaloneLinux64`, and `StandaloneOSX` are all `Standalone` — so scripting settings are shared between them while module availability is not.

`Server` appears as a named build target in `GET /api/build/settings` but has no row here, because it is a subtarget of Standalone rather than a build target of its own. `standaloneBuildSubtarget` in the settings response is what selects it.

```bash
curl http://localhost:8765/api/build/targets
curl "http://localhost:8765/api/build/targets?installed=true"
```

---

## PATCH /api/build/settings

Changes scripting settings and build flags for one named build target.

> Requires the Build category to be enabled. Rejected during Play mode, a test run, a compilation, an asset import, a build, and a build target switch.

### These changes are permanent, and not all in the same way

The API says so explicitly rather than leaving it to be discovered from a Git diff. Each change reports its own `persistence`:

| `persistence` | File | Who it affects |
|---------------|------|----------------|
| `project` | `ProjectSettings/ProjectSettings.asset` | Everyone who works on the project; appears as a Git diff |
| `project` | `ProjectSettings/EditorBuildSettings.asset` | Same, for the build scene list |
| `user` | `Library/EditorUserBuildSettings.asset` | Only this machine and user; not shared, not committed |

Scripting settings and the scene list are project-wide. Build flags such as `development` are per-user. Presenting the two as one kind of "setting" would be the misleading part, so the response distinguishes them per change.

### Request

```json
{
  "namedBuildTarget": "Standalone",
  "addDefineSymbols": ["UNIONAIR_SAMPLE"],
  "development": true
}
```

| Field | Persistence | Description |
|-------|-------------|-------------|
| `namedBuildTarget` | ― | Target the scripting settings apply to; defaults to the active one |
| `scriptingBackend` | project | `Mono2x` or `IL2CPP` |
| `apiCompatibilityLevel` | project | .NET profile |
| `managedStrippingLevel` | project | Stripping level |
| `il2CppCompilerConfiguration` | project | IL2CPP compiler configuration |
| `defineSymbols` | project | Replaces the whole list |
| `addDefineSymbols` | project | Adds symbols, keeping the rest |
| `removeDefineSymbols` | project | Removes symbols, keeping the rest |
| `development` | user | Development build |
| `allowDebugging` | user | Script debugging |
| `connectProfiler` | user | Autoconnect Profiler |
| `buildWithDeepProfilingSupport` | user | Deep profiling support |
| `waitForManagedDebugger` | user | Wait for the managed debugger |

At least one setting is required; a request that changes nothing returns `400` naming the fields it accepts.

`defineSymbols` cannot be combined with `addDefineSymbols` or `removeDefineSymbols`. The two express different intents about the same list, and applying both would make the result depend on an ordering the request never stated.

Each define symbol must start with a letter or underscore and contain only letters, digits, and underscores. Unity stores whatever it is given and fails later, at compile time, with an error that never mentions the setting — so a bad symbol is rejected here, where the message can name it. The stored string is rewritten semicolon-separated, which normalizes a list a project accumulated with stray commas or spaces.

Enum values are matched case-insensitively. An unknown value returns `400` **listing the names valid on this Editor**, because the valid set differs per version.

### Validation happens before any write

Every value is validated first. A request naming one bad enum value changes nothing at all — the only partial-failure outcome that needs no explanation.

That includes types. A field that is present but is not the kind of value it should be — `"development": "true"` rather than `true` — returns `400` naming the field. Ignoring it would break the acknowledgement rule below: the caller set something and would get no outcome for it.

### Partial failure is reported, never rolled back

Past validation, a change can still fail if Unity refuses it. When that happens the earlier changes are **not** undone: undoing them could fail too, leaving a third state matching neither what was asked for nor what was there. Instead:

- every change reports its own `outcome`: `applied`, `unchanged`, or `failed`;
- the status is `207` when any change failed, and `200` otherwise;
- `settings` carries the **resulting** state, so the caller reads the truth instead of inferring it.

A caller that needs all-or-nothing should issue one change per request.

`unchanged` is reported rather than skipped silently: a caller that set a value and got no acknowledgement cannot tell a no-op apart from a dropped field.

### Response

```json
{
  "changes": [
    {
      "setting": "defineSymbols",
      "outcome": "applied",
      "persistence": "project",
      "file": "ProjectSettings/ProjectSettings.asset",
      "previous": null,
      "value": "UNIONAIR_SAMPLE",
      "error": null
    },
    {
      "setting": "development",
      "outcome": "applied",
      "persistence": "user",
      "file": "Library/EditorUserBuildSettings.asset",
      "previous": "false",
      "value": "true",
      "error": null
    }
  ],
  "persistent": true,
  "compilationExpected": true,
  "lifecycleGeneration": 13,
  "note": "Changes are permanent. ...",
  "settings": { }
}
```

`settings` is the same object `GET /api/build/settings` returns, read after the changes were applied.

### Compilation and the domain reload

Changing the scripting backend, the API compatibility level, or the define symbols triggers a compilation and a domain reload. `compilationExpected` reports whether this request did. Build flags and the stripping level do not: the compiler does not read them.

The response is written before the reload can begin. Unity **queues** the recompile rather than running it inline, so the settings can be applied and their real outcomes reported without racing the reload — which is why this endpoint applies its changes synchronously rather than deferring them the way `POST /api/compile` must.

Afterwards the cycle is observable through the Compile API. It is recorded with `source: "external"`, because Unity started it rather than UnionAir:

```
1. PATCH /api/build/settings   -> 200 { compilationExpected: true, lifecycleGeneration: 13 }
2. Poll GET /api/editor/status, tolerating a dropped connection.
3. lifecycleGeneration > 13 and settled == true  -> the reload completed.
4. GET /api/compile            -> the cycle the change triggered.
```

```bash
curl -X PATCH http://localhost:8765/api/build/settings \
  -H "Content-Type: application/json" \
  -d '{"addDefineSymbols":["UNIONAIR_SAMPLE"]}'
```

---

## POST /api/build/scenes

Replaces the build scene list.

The list is a **replacement**, not a patch. Order decides the build index every enabled scene gets, so a partial update would have to express intent about position as well as membership, and any shorthand for that would be guessing.

### Request

```json
{
  "scenes": [
    { "path": "Assets/Scenes/SampleScene.unity", "enabled": true },
    "Assets/Scenes/Menu.unity"
  ]
}
```

Each element is either a scene path string — enabled by default — or an object with `path` and an optional `enabled`. A present `enabled` that is not a boolean returns `400`; defaulting it to `true` would ship a scene the caller asked to exclude. `scenes` is required; an empty array clears the list, which is a legitimate thing to ask for. A build with no scenes is rejected later by `POST /api/builds`, where the message can say why.

Every path must end in `.unity`, must not repeat (Unity assigns one build index per scene), and must name an **imported** asset. The AssetDatabase check matters: Unity accepts a build settings entry pointing at nothing and only fails at build time.

### Response

Same shape as `PATCH /api/build/settings`, with one `scenes` change. `previous` and `value` list each path with `+` for enabled and `-` for disabled, so a diff is readable at a glance:

```json
{
  "changes": [
    {
      "setting": "scenes",
      "outcome": "applied",
      "persistence": "project",
      "file": "ProjectSettings/EditorBuildSettings.asset",
      "previous": "Assets/Scenes/SampleScene.unity+",
      "value": "Assets/Scenes/SampleScene.unity+;Assets/Scenes/Menu.unity+",
      "error": null
    }
  ],
  "persistent": true,
  "compilationExpected": false,
  "lifecycleGeneration": 13,
  "settings": { }
}
```

```bash
curl -X POST http://localhost:8765/api/build/scenes \
  -H "Content-Type: application/json" \
  -d '{"scenes":["Assets/Scenes/SampleScene.unity"]}'
```

---

## POST /api/builds

Requests a player build for the **active** build target and returns `202` with the id to poll.

> Requires the Build category to be enabled. The endpoint risk is `executableOutput`.

### The API does not answer while a build runs

A build occupies the Unity main thread for its whole duration, and UnionAir dispatches HTTP requests from `EditorApplication.update` on that same thread. **No request is served until the build finishes** — not even `GET /api/builds/{id}`. A Windows player build was measured at roughly 72 seconds on the machine this was developed against, and 22–34 seconds with a warm cache.

Set client timeouts accordingly, and treat a refused or hanging connection during a build as expected rather than as a failure. This is why the record is persisted and the `202` sent **before** the build starts: a response written afterwards would arrive on a connection the caller had already given up on.

For the same reason there is **no live progress and no cancellation**. Neither is achievable in process — no callback can run while the main thread is inside `BuildPipeline.BuildPlayer`, and Unity exposes no player-build cancellation API at all. An out-of-process build service that could offer them is out of scope.

### Request

```json
{
  "requestId": "nightly-1",
  "development": true,
  "allowDebugging": true
}
```

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `requestId` | No | generated | Caller-supplied id; same character rules as `POST /api/compile` |
| `development` | No | project | Development build |
| `allowDebugging` | No | project | Script debugging |
| `connectProfiler` | No | project | Autoconnect Profiler |
| `deepProfiling` | No | project | Deep profiling support |
| `waitForPlayerConnection` | No | `false` | Wait for a player connection on start |
| `clean` | No | `false` | Clear the build cache first |
| `strictMode` | No | `false` | Fail the build on any error |

**Only these options are accepted.** The output location is never taken from the request, and neither is the build target — switching targets is a lifecycle operation of its own, not a build parameter.

Omitted options fall back to what the project's Build Settings window currently has selected, so a build requested with an empty body is the build a person would get by pressing Build. An override applies to that one build and is **not** written back to the project. `clean` and `strictMode` never inherit from the project, because neither is a persisted project setting.

`allowDebugging`, `connectProfiler`, `deepProfiling`, and `waitForPlayerConnection` require `development: true` and return `400` otherwise. Unity's own Build Settings window disables them without a development build, and `BuildPipeline` drops them silently — producing a build that is quietly not the one that was asked for.

Supplying `requestId` makes the request recoverable. Because the connection drops for the whole build, losing the `202` is a realistic outcome rather than a rare one; poll `GET /api/builds/{requestId}` instead of issuing a second request.

### Response — 202

```json
{
  "id": "b-20260802-101530-3f9ac1",
  "state": "queued",
  "buildTarget": "StandaloneWindows64",
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "lifecycleGenerationAtRequest": 9,
  "statusUrl": "/api/builds/b-20260802-101530-3f9ac1",
  "note": "The build occupies the Unity main thread. UnionAir answers no request, including this status URL, until it finishes..."
}
```

### Status Codes

`409` with `code: "loaded_scene_unsaved_blocked"` when **any loaded scene has unsaved changes**:

```json
{
  "error": "Cannot build while loaded scenes have unsaved changes. BuildPipeline.BuildPlayer reads scenes from disk, so the build would not contain them. Save the reported scenes explicitly before retrying.",
  "code": "loaded_scene_unsaved_blocked",
  "loadedScenes": [
    { "path": "Assets/Scenes/SampleScene.unity", "name": "SampleScene", "isDirty": true, "isActive": true, "reason": "unsaved" }
  ]
}
```

`BuildPipeline.BuildPlayer` reads scenes from their saved assets and, unlike the Build Settings window, does not prompt when called from script. A scene edited through the API but not saved would be silently excluded and the build would report **success for content that does not match the Editor** — the worst failure mode there is for an automated client. Scenes are never saved implicitly: writing a person's unsaved work to disk as a side effect of a build request is a larger surprise than a rejection. `reason` is `unsavedNewScene` for a scene that was never saved anywhere and therefore has no path to save back to.

This is a different check from the [loaded-scene external-change guard](editor.md#loaded-scene-conflict--409), which compares a loaded scene against the file on disk. Both can be true at once, and neither implies the other.

`409` with an `existingBuild` object when `requestId` was already used within the retained window; the body contains the full existing record.

`409` with an `activeBuild` object when a build is already queued or running.

`409` when the platform module for the active build target is not installed, naming the target and pointing at `GET /api/build/targets`.

`409` while a compilation, an asset import, or a build target switch is active, carrying `activeActivity`. See [Editor Activities](activities.md).

`400` when no enabled scenes are configured in Build Settings, when `requestId` is malformed, when a debug option was requested without `development`, or when an option is present but is not a JSON boolean. A present-but-wrong-typed option is rejected rather than ignored: falling back to the project default would produce a build the caller did not ask for with nothing in the response saying so.

`500` when the build record could not be written, in which case **no build was started**. Nothing is served while a build runs, so the id in the `202` is the caller's only handle; starting a minute of work whose result could not be reported would be worse than refusing it. The write is retried once immediately before the request fails.

```bash
curl -X POST http://localhost:8765/api/builds \
  -H "Content-Type: application/json" \
  -d '{"requestId":"nightly-1"}'
```

---

## GET /api/builds

Returns the in-flight build as `current`, the retained record summaries, and how much disk the artifacts occupy.

```json
{
  "current": null,
  "total": 1,
  "storage": {
    "root": "Builds/UnionAir",
    "totalBytes": 140839956,
    "artifactCount": 1,
    "maxArtifactCount": 3,
    "maxTotalBytes": 2147483648,
    "retainedRecords": 20
  },
  "records": [
    {
      "id": "b-20260802-101530-3f9ac1",
      "state": "completed",
      "result": "succeeded",
      "buildTarget": "StandaloneWindows64",
      "requestedAt": "2026-08-02T09:41:12.0200139Z",
      "finishedAt": "2026-08-02T09:41:34.2200139Z",
      "durationSeconds": 22.2,
      "outputDirectory": "Builds/UnionAir/b-20260802-101530-3f9ac1",
      "outputBytes": 140838619,
      "outputAvailable": true,
      "compileId": "c-20260802-094112-9d15d8",
      "error": null,
      "statusUrl": "/api/builds/b-20260802-101530-3f9ac1"
    }
  ]
}
```

`storage` exists because the artifact is invisible to git by design (see below), and therefore invisible to most of the ways a person notices disk filling up. Discoverability comes from the API instead: the record carries the output path and byte size, this endpoint reports the total, `DELETE` reclaims it, and retention trims automatically.

---

## GET /api/builds/{id}

Returns one retained build record, including the snapshotted build report.

```json
{
  "id": "b-20260802-101530-3f9ac1",
  "source": "unionAir",
  "state": "completed",
  "result": "succeeded",
  "buildTarget": "StandaloneWindows64",
  "buildTargetGroup": "Standalone",
  "namedBuildTarget": "Standalone",
  "requestedAt": "2026-08-02T09:41:12.0200139Z",
  "startedAt": "2026-08-02T09:41:12.0250139Z",
  "finishedAt": "2026-08-02T09:41:34.2200139Z",
  "durationSeconds": 22.2,
  "lifecycleGenerationAtRequest": 10,
  "lifecycleGenerationAtFinish": 10,
  "options": { "development": true, "allowDebugging": true, "connectProfiler": false, "deepProfiling": false, "waitForPlayerConnection": false, "clean": false, "strictMode": false },
  "scenes": ["Assets/Scenes/SampleScene.unity"],
  "compileId": "c-20260802-094112-9d15d8",
  "outputDirectory": "Builds/UnionAir/b-20260802-101530-3f9ac1",
  "outputPath": "Builds/UnionAir/b-20260802-101530-3f9ac1/TestUnity6.exe",
  "outputBytes": 140838619,
  "outputAvailable": true,
  "reportPath": "Builds/UnionAir/b-20260802-101530-3f9ac1/report.json",
  "report": {
    "result": "succeeded",
    "platform": "StandaloneWindows64",
    "platformGroup": "Standalone",
    "outputPath": "C:/Projects/Game/Builds/UnionAir/b-20260802-101530-3f9ac1/TestUnity6.exe",
    "startedAt": "2026-08-02T09:41:12.0606497Z",
    "endedAt": "2026-08-02T09:41:34.0563850Z",
    "totalTimeSeconds": 21.99,
    "totalSizeBytes": 140838619,
    "totalErrors": 0,
    "totalWarnings": 0,
    "messages": [],
    "messagesTruncated": false
  },
  "error": null,
  "statusUrl": "/api/builds/b-20260802-101530-3f9ac1"
}
```

### State and Result

| `state` | `result` | Meaning |
|---------|----------|---------|
| `queued` | ― | The build was accepted but has not started |
| `running` | ― | The build is in progress; nothing else is being served |
| `completed` | `succeeded` | Unity reported a successful build |
| `failed` | `failed` | Unity reported a failed build; see `report.messages` |
| `failed` | `cancelled` | The build was cancelled inside Unity |
| `aborted` | `aborted` / `notStarted` | The Editor reloaded, quit, or crashed before the build reported a result |

`report` is a **snapshot**, not a live object. `BuildReport` is a Unity object backed by native state that a domain reload discards, so it is copied into plain fields the moment `BuildPipeline.BuildPlayer` returns; the record then stays readable for as long as it is retained. `messages` carries errors and warnings only — a successful build reports thousands of informational entries, and the record is written to disk and returned in full on every poll. It is capped at 200 entries, with `messagesTruncated` reporting the cut.

`compileId` names the compile record for the **player compilation this build ran**. That cycle is recorded with `source: "build"` rather than adopted as an unrelated external compilation; see [Compilations a build owns](compile.md#compilations-a-build-owns).

`report.startedAt` and `report.endedAt` come from Unity and are normalized to UTC. `report.outputPath` is the absolute path Unity reported; `outputPath` on the record is the project-relative one.

`outputAvailable` is computed when the record is read, because retention removes the output long before the record. A record with `outputAvailable: false` still reports what the build produced.

---

## DELETE /api/builds/{id}

Deletes a build record and its artifact directory.

```json
{
  "deleted": "b-20260802-101530-3f9ac1",
  "reclaimedBytes": 99727454,
  "outputAvailable": false,
  "totalBytes": 140839956
}
```

Returns `404` for an id that is not retained. `outputAvailable: true` in the response means the directory could not be fully removed — normally because a file in it is open.

Returns `409` with an `activeBuild` object for a build that is still queued or running. Deleting the record a queued build is waiting for would leave its deferred start with nothing to run, so nothing would release the build activity and every conflicting endpoint would stay blocked for the rest of the Editor session. Wait for the build to reach a terminal state, then delete it.

---

## POST /api/build/target

Switches the active build target and returns `202` with the id to poll.

> Requires the Build category to be enabled. The endpoint risk is `executableOutput`.

### This is a lifecycle operation, not a settings write

Switching targets reimports **every asset** for the new platform, recompiles, and ends in a domain reload. On a large project that is minutes, not seconds, and UnionAir answers nothing for most of it — the same unavailability a build causes, for longer. That is why it is a tracked activity with a durable record rather than a field on `PATCH /api/build/settings`.

The record is the point. It survives the domain reload the switch causes, so a client whose connection dropped can come back and read the outcome rather than inferring it from the active target. Unity reports nothing across the reload, so on the far side UnionAir resolves the record by comparing the active target against the one that was requested.

### Request

```json
{ "buildTarget": "StandaloneWindows64", "requestId": "ci-1" }
```

| Field | Required | Description |
|-------|----------|-------------|
| `buildTarget` | Yes | A `BuildTarget` name reported by [`GET /api/build/targets`](#get-apibuildtargets) |
| `requestId` | No | Caller-supplied id; same character rules as `POST /api/compile` |

### Response — 202

```json
{
  "id": "t-20260802-103012-5ba71c",
  "state": "queued",
  "requestedTarget": "StandaloneWindows64",
  "previousTarget": "StandaloneWindows",
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "lifecycleGenerationAtRequest": 16,
  "statusUrl": "/api/build/target/t-20260802-103012-5ba71c",
  "note": "Switching reimports every asset for the new platform..."
}
```

### Status Codes

`409` with `code: "platform_module_not_installed"` when the target's module is missing:

```json
{
  "error": "The platform module for 'Android' is not installed in this Unity Editor. Install it through the Unity Hub for this Editor version, then retry.",
  "code": "platform_module_not_installed",
  "buildTarget": "Android",
  "installedTargets": ["StandaloneWindows", "StandaloneWindows64"]
}
```

This is reported as its own condition rather than as a generic failure, because the fix — installing the module from the Unity Hub — is not something a switch-failed message would ever suggest. `installedTargets` names what the Editor *can* switch to.

`200` with `state: "unchanged"` when the requested target is already active. Nothing is reimported and no record is created.

`409` with an `activeSwitch` object when a switch is already running, and `409` with `existingSwitch` on a replayed `requestId`.

`400` for a missing or unknown `buildTarget`, or a malformed `requestId`.

`500` when the switch record could not be written, in which case **no switch was started**. The record is the only thing that survives the domain reload the switch causes, so a switch begun without one could never report its outcome.

`409` while a compilation, an asset import, or a build is active, carrying `activeActivity`.

```bash
curl -X POST http://localhost:8765/api/build/target \
  -H "Content-Type: application/json" \
  -d '{"buildTarget":"StandaloneWindows64"}'
```

---

## GET /api/build/target

Returns the active target, the in-flight switch as `current`, and the retained switch records.

```json
{
  "activeBuildTarget": "StandaloneWindows64",
  "activeBuildTargetGroup": "Standalone",
  "current": null,
  "total": 2,
  "records": [
    {
      "id": "t-20260802-103012-5ba71c",
      "source": "unionAir",
      "state": "completed",
      "requestedTarget": "StandaloneWindows64",
      "requestedTargetGroup": "Standalone",
      "requestedNamedBuildTarget": "Standalone",
      "previousTarget": "StandaloneWindows",
      "activeTarget": "StandaloneWindows64",
      "durationSeconds": 3.3,
      "lifecycleGenerationAtRequest": 18,
      "lifecycleGenerationAtFinish": 18,
      "error": null,
      "statusUrl": "/api/build/target/t-20260802-103012-5ba71c"
    }
  ]
}
```

`GET /api/build/target/{id}` returns one record. UnionAir retains the 20 most recent.

### State

| `state` | Meaning |
|---------|---------|
| `queued` | Accepted; the switch has not started |
| `switching` | Reimporting and recompiling; the Editor is unavailable |
| `completed` | The active target is the requested one |
| `failed` | Unity refused the switch, or reloaded without performing it |
| `aborted` | The Editor was closed or restarted mid-switch |

A queued switch whose start was discarded by an unrelated domain reload resolves to `failed` rather than staying queued. The deferred start lives in the domain that accepted the request, so a reload before it runs — an IDE save is enough — leaves nothing to run it.

`lifecycleGenerationAtRequest` and `lifecycleGenerationAtFinish` differ when the switch crossed a domain reload, and match when Unity completed it inline. Both happen: a switch **within** a build target group — `StandaloneWindows64` to `StandaloneWindows`, say — often needs no reload at all, while a switch between groups does.

### Polling through the gap

```
1. POST /api/build/target        -> 202 { id, lifecycleGenerationAtRequest }
2. Poll GET /api/build/target/{id}, treating a refused connection as expected.
3. state == "completed"          -> done; the new target is active.
   state == "failed"             -> read 'error'; the active target did not change.
   state == "aborted"            -> the Editor died; check GET /api/build/target.
4. Give the wait an explicit timeout. A large project can take minutes.
```

---

## Artifact storage and retention

Player output and its `report.json` live together in `Builds/UnionAir/{id}/`, under the project root.

`Library/` is deliberately **not** used, even though compile records, profiling artifacts, and memory snapshots live in `Library/UnionAir/`. Unity regenerates `Library/` whenever it decides to, which would either destroy a hundred-megabyte artifact silently or orphan it from the record that names it. Build output is a user-facing deliverable rather than an Editor-internal diagnostic, and it belongs where a person would look for it.

**Git exclusion is made deterministic.** A `.gitignore` containing `*` is written into the directory when it is created — at creation rather than at completion, so a build that fails partway through does not leave committable output behind. UnionAir cannot rely on the consuming project's `.gitignore`: Unity's standard template excludes `/[Bb]uilds/`, but not every project uses it, so git visibility would vary per project and could not be documented as behavior. The measured output is also close enough to GitHub's 100 MiB file limit that an accidental commit is a serious hazard rather than an untidy one.

| Cap | Value |
|-----|-------|
| Retained artifact directories | 3 |
| Total artifact size | 2 GiB |
| Retained records | 20 |

Artifacts are trimmed oldest-first when either cap is exceeded, protecting the build that just finished. The caps are set independently of the 5 GB profiling quota, which would retain roughly fifty builds.

Records are small and outlive the artifacts, so a client asking about an older build learns what it produced instead of getting a `404`.

The record is also written as `report.json` inside the artifact directory, so the directory explains itself to a person who finds it without the API.

### After a build, loaded scenes stay tracked

`BuildPipeline.BuildPlayer` opens the build scenes itself and raises `sceneClosed` for the loaded scene without a matching `sceneOpened`, which drops the disk baseline the [external-change guard](editor.md#loaded-scene-conflict--409) keeps. Left alone, every later `POST /api/editor/refresh` or `POST /api/compile` would report the scene as `untracked` until someone saved or reopened it by hand — breaking the build-then-compile loop for no real reason.

UnionAir captures the baselines before the build and restores them afterwards, but **only for scenes whose file is byte-identical to what it was**. A scene changed on disk while the build ran still fails that comparison, stays untracked, and still trips the guard.

---

## Related Documentation

- [Compile API](compile.md) — structured compilation results these settings explain
- [API Reference index](../api-reference.md)
