# UnionAir — Unity REST Bridge

**English** | [日本語](README.ja.md)

> **⚠️ Experimental**
> This package is an experimental, pre-beta prototype. There are **no guarantees** of backward compatibility, versioning stability, or behavior. Any API may change or be removed without notice.

UnionAir exposes Unity Editor state as a simple **REST API** over HTTP, making it easy to integrate with AI assistants, development bots, CI tooling, or any HTTP client.

## Design Philosophy

UnionAir assumes its client already has direct access to the project directory. General-purpose file reading and writing is not something this package needs to provide, and it does not try to. What it exposes are operations the Editor itself defines, and the artifacts they produce.

What a client cannot get from the filesystem is the Editor's own behavior. A Unity project's state is not only the files on disk — it is also what the Editor holds after import: GUID resolution, serialized references, the loaded scene graph, the asset database, the domain that scripts live in. Editing a `.unity` or `.asset` file by hand means reproducing the Editor's rules from the outside, and that reproduction is where correctness is lost.

An endpoint earns its place here when going through the Editor is materially better than editing the files:

- **Unity's rules and validation, without reimplementing them.** `POST /api/assets/move` delegates to `AssetDatabase.MoveAsset`, which preserves the asset's GUID and the serialized references that resolve through it — though neither route updates paths a project holds as plain strings. `DELETE /api/assets/{guid}` removes the `.meta` with the asset and refuses when the target is a loaded scene. Careful file operations can reach the same results; getting there means the client carrying Unity's rules itself, and being right about them every time.
- **State that exists only inside the Editor.** Play mode, selection, the Console, compilation results, profiler samples, the loaded scene list. There is no file to read.
- **Operations defined by Unity's own semantics.** Prefab apply/revert, supported `SerializedObject` writes to project-defined ScriptableObjects, animation curves that must resolve a Sprite sub-asset rather than its backing Texture2D. Expressing these as YAML edits means reimplementing Unity's serializer.
- **Feedback that closes a loop.** Compilation diagnostics survive the domain reload they trigger, so a write-compile-fix cycle can terminate. See [The Compile-and-Fix Loop](Documentation~/api/compile.md#the-compile-and-fix-loop).
- **Undo integration where Unity offers it.** Scene edits — creating, updating, and deleting GameObjects and components — are registered with `Undo`, so a human at the Editor can reverse them with Ctrl+Z. Asset writes do not uniformly support Undo and should not be assumed to be reversible.

Meeting one of these is necessary, not sufficient. That a Unity Editor API exists is not itself a reason to expose it. An endpoint also has to answer a real client need with a contract worth depending on — one that keeps its meaning across Unity versions and fits the rest of the API — rather than being added because the underlying method was there to wrap.

Work that plain file operations already handle correctly is deliberately out of scope. Editing C# source, reading `manifest.json`, walking the project tree — a client should do those directly. An endpoint for them would only put HTTP between the client and a job it can already do.

## Requirements

- Unity **2022.3** or later
- Unity Test Framework **1.4.0** or later (optional; required only for the Test Runner API)
- Input System `com.unity.inputsystem` (optional; required only for Play Mode input actions and input replay). Unity must also have **Active Input Handling** set to *Input System Package* or *Both*.

### Supported versions

| Unity | Support level |
| --- | --- |
| **6000.0 LTS and later** | **Fully supported.** Primary target; all features are developed and verified here. Baseline is 6000.0.80f1. |
| **2022.3 LTS** | **Supported.** Builds and core behavior are verified (2022.3.62f2). |
| 2023.x | Best effort. Expected to work and shares the same code paths, but is not verified on every change. |

Unity 6 is the primary target. 2022.3 LTS is kept working deliberately; issues reported against it are accepted.

#### Test Runner and Unity Test Framework

The Test Runner API is delivered as a separate assembly that is compiled only when
`com.unity.test-framework` **1.4.0 or later** is present. Version 1.4.0 introduced the
result-saving and run-cancellation APIs that UnionAir depends on.

Unity 6000.0 ships a new enough version by default. Unity 2022.3 and 2023.1 do **not** —
they default to 1.1.33 and 1.3.9 respectively. On those versions the Test Runner assembly is
skipped entirely, so **no compile errors occur**, but the `/api/tests` and `/api/test-runs`
endpoints and the Test Runner category are absent.

To use the Test Runner API on 2022.3 or 2023.x, request a compatible version in your
project's `Packages/manifest.json`:

```json
"dependencies": {
  "com.unity.test-framework": "1.4.6"
}
```

UnionAir does not declare this dependency itself, because the Test Runner category is
disabled by default and upgrading the package should stay the project's decision.

## Installation

### Via Package Manager (Git URL)

1. Open **Window > Package Manager**.
2. Click **+** and choose **Install package from git URL...**
3. Enter:

```
https://github.com/LeonAkasaka/UnionAir.git#v0.3.0
```

### Via manifest.json

Add the dependency to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.leonakasaka.unionair": "https://github.com/LeonAkasaka/UnionAir.git#v0.3.0"
  }
}
```

Pin the tag. `main` is kept releasable but runs ahead of it, and a UPM Git URL takes a fixed ref only — there is no version range syntax.

## Setup

1. The package auto-starts an HTTP server when the Unity Editor loads.
2. Open **Window > UnionAir > REST Bridge** to view status and configure the port.

## Port Selection

The default is **Automatic**. UnionAir resolves a free loopback port, retains it across domain reloads
in the same Editor process, and publishes the concrete URL through `.unionair/endpoint.txt`. After a
transient address-in-use result, the retained port gets one delayed retry before UnionAir selects a
fresh port. Select **Fixed** in the EditorWindow when a script or CI environment needs a stable port
in the range `1..65535`.

## Endpoints

| Group | Scope | Security |
|-------|-------|----------|
| **Read** | Scene hierarchy, loaded scenes, GameObjects, assets, cameras, logs, search, compilation results | Always enabled |
| **Scene Write** | Create/open/unload scenes, create/update/delete GameObjects and components | Disabled by default |
| **Asset Write** | Prefabs, materials, asset files, AssetDatabase refresh, compilation requests | Disabled by default |
| **Play Mode** | Enter/exit/pause/step play mode, Input System actions, and Canvas UI interaction | Disabled by default |
| **Editor Actions** | Selection, object ping, asset open, and Unity Editor menu item execution | Disabled by default |
| **Test Runner** | Discover, execute, monitor, cancel, and download results for EditMode and PlayMode tests | Disabled by default; available when Unity Test Framework 1.4.0+ is installed |
| **Profiling** | ProfilerRecorder metrics, NDJSON samples, Profiler raw captures, and memory snapshots | Disabled by default |
| **Build** | Build configuration read and write, installed platform modules, build target switching, and in-process player builds with persisted reports | Disabled by default |

> Compilation results are structured, with `severity`, `code`, project-relative `file`, `line`, and `column` per diagnostic, and survive the domain reload that a successful compilation triggers. Compilations started from an IDE are recorded too. See **[The Compile-and-Fix Loop](Documentation~/api/compile.md#the-compile-and-fix-loop)**.
> Unity Console logs are retained across domain reloads and support an incremental `since` cursor.
> Scene edits made in Edit mode are registered with Undo and can be reversed in the Unity Editor (Ctrl+Z). Asset writes do not uniformly support Undo and should not be assumed to be reversible.
> Scene GameObjects and Components include `globalObjectId` values in read responses and can be targeted with typed object references in write requests.
> Write APIs declare Play Mode safety in `GET /api/help`; persistent scene/asset changes are blocked during Play Mode, while selected scene-object changes require both the Editor setting and `allowWhilePlaying=true`.
> See **[API Reference](Documentation~/api-reference.md)** for the full endpoint list and request/response details.

## Security

Read this before enabling any write category:

- The server binds to **`localhost` only** — it is not reachable from other machines on the network.
- There is **no authentication**. Any process running on the same machine can call every enabled endpoint.
- Requests carrying an `Origin` header are rejected before routing, and responses do not opt into CORS. Browser `fetch` and XMLHttpRequest clients are therefore unsupported by default; local CLI and integration clients that omit `Origin` continue to work.
- Requests with a non-empty body must use `Content-Type: application/json`. Empty POST requests remain valid without a content type.
- Only the **Read** category is enabled by default. The Scene Write, Asset Write, Play Mode, Editor Actions, Test Runner, Profiling, and Build categories are opt-in; enabling them exposes state-changing operations and diagnostic artifacts — including arbitrary project test code, heap snapshots, Unity Editor menu execution, and asset deletion — to any local process. Enable them only when every local client is trusted.
- The **Build** category carries the `executableOutput` and `assetUpdate` risks. Enabling it lets any local process change build settings that are written to `ProjectSettings/` and shared with everyone who works on the project, and start a player build, which runs the project's build scripts and writes a runnable program to `Builds/UnionAir/` in the project directory. A build also occupies the Unity main thread for a minute or more, during which UnionAir answers nothing at all.
- Category enablement is **not per-project**. It lives in `EditorPrefs`, which Unity scopes to the user account and Editor version, so a category enabled for one project stays enabled for every project opened with the same Editor.

## API Discovery

After the listener starts, UnionAir atomically publishes its active API Base URL to
`<project>/.unionair/endpoint.txt` as one UTF-8 line with a trailing slash. Clients should read and
trim that file, call `{baseUrl}health`, verify that its `projectPath` matches the project directory
containing the file, and then call `{baseUrl}help?detail=full`. The file is advisory: a hard Editor
crash can leave it behind, so a failed health check or project mismatch means the client must treat
it as stale. Clean stops and observed listener failures remove the current instance's file.

UnionAir maintains `.unionair/.gitignore` so `endpoint.txt` and atomic-write temporary files do not
create Git changes. Project configuration may share the same directory; `settings.json` is not
ignored.

## Quick Example

```bash
BASE_URL="$(tr -d '\r\n' < .unionair/endpoint.txt)"

# Health check
curl "${BASE_URL}health"

# Scene hierarchy
curl "${BASE_URL}scene/hierarchy"

# Loaded scenes
curl "${BASE_URL}scenes"

# Specific GameObject
curl --get "${BASE_URL}gameobjects" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'

# All assets of type Texture2D
curl "${BASE_URL}assets?type=Texture2D"

# Create a new empty GameObject (requires the Scene Write category to be enabled)
curl -X POST "${BASE_URL}gameobjects" \
  -H "Content-Type: application/json" \
  -d '{"name":"MyObject","parent":{"type":"hierarchyPath","value":"Canvas"}}'
```

## AI Integration

UnionAir does not provide a dedicated MCP server, and none is currently planned. AI clients can discover the available operations through `GET /api/help` and call the REST endpoints directly. If a client needs a different integration surface, use a thin wrapper around the help API and REST endpoints, or client-side features such as skills. AI-specific integration remains outside the Unity package.

## Documentation

- **[Getting Started](Documentation~/index.md)** — Setup, EditorWindow guide, lifecycle
- **[API Reference](Documentation~/api-reference.md)** — Full endpoint reference with request/response examples
- **[Custom Controllers](Documentation~/custom-controllers.md)** — Extension guide for application-side UnionAir APIs

## Known Limitations

- Automated coverage is limited to the EditMode tests in `Tests/Editor`, which exercise only Editor-independent logic; compilation, domain reloads, Play mode, and the HTTP server are verified by hand, and there is no CI. See [Tests](CONTRIBUTING.md#tests) for how to run them.
- Request-body JSON parsing is a lightweight custom reader; deeply nested or unusual JSON bodies may hit edge cases.
- JSON response serialization is hand-written per endpoint; a shared serializer is a planned refactor.
- Browser-originated `fetch` and XMLHttpRequest clients are not supported; there is currently no configurable Origin allowlist.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Development conventions live in [AGENTS.md](AGENTS.md).

## License

[MIT](LICENSE)
