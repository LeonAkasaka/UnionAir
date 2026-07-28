# UnionAir — Unity REST Bridge

**English** | [日本語](README.ja.md)

> **⚠️ Experimental**
> This package is an experimental, pre-beta prototype. There are **no guarantees** of backward compatibility, versioning stability, or behavior. Any API may change or be removed without notice.

UnionAir exposes Unity Editor state as a simple **REST API** over HTTP, making it easy to integrate with LLM MCP bridges, development bots, CI tooling, or any HTTP client.

## Requirements

- Unity **6000.0** or later
- Unity Test Framework (optional; required only for the Test Runner API)

## Installation

### Via Package Manager (Git URL)

1. Open **Window > Package Manager**.
2. Click **+** and choose **Install package from git URL...**
3. Enter:

```
https://github.com/LeonAkasaka/UnionAir.git
```

### Via manifest.json

Add the dependency to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.leonakasaka.unionair": "https://github.com/LeonAkasaka/UnionAir.git"
  }
}
```

## Setup

1. The package auto-starts an HTTP server when the Unity Editor loads.
2. Open **Window > UnionAir > REST Bridge** to view status and configure the port.

## Default Port

`8765` — configurable via the EditorWindow.

## Endpoints

| Group | Scope | Security |
|-------|-------|----------|
| **Read** | Scene hierarchy, loaded scenes, GameObjects, assets, cameras, logs, search, compilation results | Always enabled |
| **Scene Write** | Create/open/unload scenes, create/update/delete GameObjects and components | Disabled by default |
| **Asset Write** | Prefabs, materials, asset files, AssetDatabase refresh, compilation requests | Disabled by default |
| **Play Mode** | Enter/exit/pause/step play mode, Input System actions, and Canvas UI interaction | Disabled by default |
| **Editor Actions** | Selection, object ping, asset open, and Unity Editor menu item execution | Disabled by default |
| **Test Runner** | Discover, execute, monitor, cancel, and download results for EditMode and PlayMode tests | Disabled by default; available when Unity Test Framework is installed |
| **Profiling** | ProfilerRecorder metrics, NDJSON samples, Profiler raw captures, and memory snapshots | Disabled by default |

> Compilation results are structured, with `severity`, `code`, project-relative `file`, `line`, and `column` per diagnostic, and survive the domain reload that a successful compilation triggers. Compilations started from an IDE are recorded too. See **[The Compile-and-Fix Loop](Documentation~/api/compile.md#the-compile-and-fix-loop)**.
> Unity Console logs are retained across domain reloads and support an incremental `since` cursor.
> Edit mode write operations are Undo-able in the Unity Editor (Ctrl+Z).
> Scene GameObjects and Components include `globalObjectId` values in read responses and can be targeted with typed object references in write requests.
> Write APIs declare Play Mode safety in `GET /api/help`; persistent scene/asset changes are blocked during Play Mode, while selected scene-object changes require both the Editor setting and `allowWhilePlaying=true`.
> See **[API Reference](Documentation~/api-reference.md)** for the full endpoint list and request/response details.

## Security

Read this before enabling any write category:

- The server binds to **`localhost` only** — it is not reachable from other machines on the network.
- There is **no authentication**. Any process running on the same machine can call every enabled endpoint.
- Responses include `Access-Control-Allow-Origin: *`, so **any web page open in a browser on the same machine** can also call the API and read the responses (scene hierarchy, assets, logs, screenshots).
- Only the **Read** category is enabled by default. The Scene Write, Asset Write, Play Mode, Editor Actions, Test Runner, and Profiling categories are opt-in; enabling them exposes state-changing operations and diagnostic artifacts — including arbitrary project test code, heap snapshots, Unity Editor menu execution, and asset deletion — to any local client or browser origin. Enable them only when every local client (and browser tab) is trusted.

## Quick Example

```bash
# Health check
curl http://localhost:8765/api/health

# Scene hierarchy
curl http://localhost:8765/api/scene/hierarchy

# Loaded scenes
curl http://localhost:8765/api/scenes

# Specific GameObject
curl --get "http://localhost:8765/api/gameobjects" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'

# All assets of type Texture2D
curl "http://localhost:8765/api/assets?type=Texture2D"

# Create a new empty GameObject (requires the Scene Write category to be enabled)
curl -X POST http://localhost:8765/api/gameobjects \
  -H "Content-Type: application/json" \
  -d '{"name":"MyObject","parent":{"type":"hierarchyPath","value":"Canvas"}}'
```

## MCP Bridge

To use with an LLM MCP client, run a separate Node.js MCP bridge that calls these REST endpoints. The Unity package itself has no MCP dependency.

## Documentation

- **[Getting Started](Documentation~/index.md)** — Setup, EditorWindow guide, lifecycle
- **[API Reference](Documentation~/api-reference.md)** — Full endpoint reference with request/response examples
- **[Custom Controllers](Documentation~/custom-controllers.md)** — Extension guide for application-side UnionAir APIs

## Known Limitations

- No automated tests or CI yet.
- Request-body JSON parsing is a lightweight custom reader; deeply nested or unusual JSON bodies may hit edge cases.
- JSON response serialization is hand-written per endpoint; a shared serializer is a planned refactor.
- The wildcard CORS policy (`Access-Control-Allow-Origin: *`) may be tightened in a future release.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Development conventions live in [AGENTS.md](AGENTS.md).

## License

[MIT](LICENSE)
