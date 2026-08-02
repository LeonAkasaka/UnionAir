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

## Related Documentation

- [Compile API](compile.md) — structured compilation results these settings explain
- [API Reference index](../api-reference.md)
