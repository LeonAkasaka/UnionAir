# UnionAir — Unity REST Bridge
**English** | [日本語](index.ja.md)

UnionAir is an Editor-only package that exposes the state of the Unity Editor externally as a simple **REST API**.  
It allows any HTTP-capable client—such as LLM MCP bridges, development bots, and CI tools—to retrieve information from Unity.

---

## Setup

### 1. Install the package

Open **Window > Package Manager**, click **+**, choose **Install package from git URL...**, and enter:

```
https://github.com/LeonAkasaka/UnionAir.git
```

Alternatively, add the dependency to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "com.leonakasaka.unionair": "https://github.com/LeonAkasaka/UnionAir.git"
  }
}
```

If the package is placed in the project's `Packages/com.leonakasaka.unionair/` folder instead, Unity detects it automatically as an embedded package.

The Test Runner API is optional. It appears only when the project has the Unity Test Framework package (`com.unity.test-framework`) **1.4.0 or later** installed, and its **Test Runner** category remains disabled until explicitly enabled in the UnionAir EditorWindow. Unity 2022.3 and 2023.1 default to older versions (1.1.33 and 1.3.9), where the API is absent; see [Supported versions](../README.md#supported-versions).

The **Profiling** category is also disabled by default. It provides AI-oriented metric summaries, frame-level NDJSON, Unity Profiler raw captures, and Memory Profiler snapshots. See the [Profiling API](api/profiling.md).

Reading compilation results needs no setup — `GET /api/compile` is in the always-enabled **Read** category and records compilations started from an IDE as well as through the API. Requesting one with `POST /api/compile` requires the **Asset Write** category. If you are automating the write-compile-fix cycle, read [The Compile-and-Fix Loop](api/compile.md#the-compile-and-fix-loop) first: it covers how to terminate correctly when a successful compilation does *not* reload the domain, which is the usual cause of a client hanging.

### 2. Check the server

When you open the Unity Editor, the REST server starts automatically (default port: **8765**).

```
Window > UnionAir > REST Bridge
```

Open the EditorWindow from the menu above and check the server status.

### 3. Verify operation

```bash
curl http://localhost:8765/api/health
# => {"status":"ok","unityVersion":"6000.3.5f2"}
```

---

## Quick Start

### Get the scene hierarchy

```bash
curl http://localhost:8765/api/scene/hierarchy
```

```json
{
  "scene": "SampleScene",
  "objects": [
    {
      "name": "Main Camera",
      "path": "Main Camera",
      "isActive": true,
      "tag": "MainCamera",
      "layer": 0,
      "transform": {
        "position": { "x": 0, "y": 1, "z": -10 },
        "rotation": { "x": 0, "y": 0,  "z": 0  },
        "scale":    { "x": 1, "y": 1,  "z": 1  }
      },
      "children": []
    }
  ]
}
```

### Check the components of a specific GameObject

```bash
curl --get "http://localhost:8765/api/gameobjects" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'
```

### Search the asset list

```bash
# All Texture2D
curl "http://localhost:8765/api/assets?type=Texture2D"

# Filter by path
curl "http://localhost:8765/api/assets?path=Assets/UI"
```

---

## Using the EditorWindow

| Item | Description |
|------|------|
| **Status** | Displays the server running state and port number |
| **Port** | The server listening port (can only be changed while stopped) |
| **Auto Start on Load** | Whether to start the server automatically when the Editor starts |
| **Diagnostic Lifecycle Logging** | Streams detailed listener lifecycle events to the Console; disabled by default |
| **Start / Stop / Restart** | Manual control of the server |
| **Request Log** | Log of received requests (latest 100 entries) |

---

## Documentation

- [API Reference](api-reference.md) — Detailed specifications for all endpoints
- [Custom Controllers](custom-controllers.md) — Extension guide for application-side UnionAir APIs

---

## Lifecycle

- **When the Editor starts**: The server starts automatically via `[InitializeOnLoad]`
- **During Domain reload**: Closes the listener, waits briefly for its background thread, closes queued responses, aborts listener-owned in-flight or deferred connections, and then restarts automatically after the reload
- **In Play Mode**: The server continues running. If it was stopped after Exit Play Mode, it restarts automatically

If an automatic start encounters a transient address-in-use error immediately after a reload, UnionAir makes one initial attempt followed by up to five retries over approximately four seconds. Intermediate address-in-use failures are retained only in the lifecycle trace and do not pollute the Console or `/api/editor/logs`. Other startup failures always produce a concise Console error. If the listener thread exits unexpectedly, UnionAir completes listener cleanup before dumping the diagnostic trace and schedules up to three delayed recovery attempts per domain. Further unexpected exits stop automatic recovery and produce a concise error instead of entering an unbounded restart loop. UnionAir silently retains a bounded lifecycle trace across domain reloads and automatically dumps it once per domain when startup or cleanup fails. Enable **Diagnostic Lifecycle Logging** to stream the same process, reload generation, listener cleanup, thread, and native socket details during normal operation.

A deferred handler owns its response lifetime. Closing the listener aborts any deferred connection that remains active during shutdown, so deferred handlers must tolerate response writes failing after a reload or server stop.
