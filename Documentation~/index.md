# UnionAir — Unity REST Bridge
**English** | [日本語](index.ja.md)

UnionAir is an Editor-only package that exposes the state of the Unity Editor externally as a simple **REST API**.  
It allows any HTTP-capable client—such as LLM MCP bridges, development bots, and CI tools—to retrieve information from Unity.

---

## Setup

### 1. Install the package

Open **Window > Package Manager**, click **+**, choose **Install package from git URL...**, and enter:

```
https://github.com/LeonAkasaka/UnionAir.git#v0.3.0
```

Alternatively, add the dependency to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "com.leonakasaka.unionair": "https://github.com/LeonAkasaka/UnionAir.git#v0.3.0"
  }
}
```

If the package is placed in the project's `Packages/com.leonakasaka.unionair/` folder instead, Unity detects it automatically as an embedded package.

The Test Runner API is optional. It appears only when the project has the Unity Test Framework package (`com.unity.test-framework`) **1.4.0 or later** installed, and its **Test Runner** category remains disabled until explicitly enabled in the UnionAir EditorWindow. Unity 2022.3 and 2023.1 default to older versions (1.1.33 and 1.3.9), where the API is absent; see [Supported versions](../README.md#supported-versions).

The **Profiling** category is also disabled by default. It provides AI-oriented metric summaries, frame-level NDJSON, Unity Profiler raw captures, and Memory Profiler snapshots. See the [Profiling API](api/profiling.md).

Reading compilation results needs no setup — `GET /api/compile` is in the always-enabled **Read** category and records compilations started from an IDE as well as through the API. Requesting one with `POST /api/compile` requires the **Asset Write** category. If you are automating the write-compile-fix cycle, read [The Compile-and-Fix Loop](api/compile.md#the-compile-and-fix-loop) first: it covers how to terminate correctly when a successful compilation does *not* reload the domain, which is the usual cause of a client hanging.

### 2. Check the server

When you open the Unity Editor, the REST server starts automatically. The default port mode is
**Automatic**; a free concrete loopback port is selected and published through the discovery file.

```
Window > UnionAir > REST Bridge
```

Open the EditorWindow from the menu above and check the server status.

The active API Base URL is also published to `<project>/.unionair/endpoint.txt` after a successful
start. Read and trim the file, call `{baseUrl}health` with a short timeout, verify that its
`projectPath` matches `<project>`, and then call `{baseUrl}help?detail=full`. Treat a missing file,
failed health check, or project mismatch as no validated server; a hard process crash can leave
stale discovery state that now points at another project's Editor.

### Project settings

Schema-backed EditorWindow controls update a working configuration and automatically save the
complete `<project>/.unionair/settings.json` after every change. The strict v1 document is:

```json
{
  "schemaVersion": 1,
  "server": { "port": 0, "autoStart": true },
  "api": { "enabledCategories": [], "customHandlers": false },
  "playMode": { "allowSceneChanges": false }
}
```

Every field is required. Built-in category IDs are bare; custom category IDs use `custom:<id>`.
`read` must not be listed because it is always enabled. Unknown or duplicate fields and categories,
wrong types, unsupported schemas, invalid ports, and a custom category without `customHandlers:true`
invalidate the entire document. Invalid settings disable auto-start and fail closed to Read only;
the first UI change repairs the file from those safe values.

A valid file supplies project values before the auto-start decision. The Built-in API category
checkboxes, **Custom Handlers > Enable Custom Handlers**, and the Play Mode scene-change checkbox
are the authoritative controls and write their values directly to the file. There is no separate
local-approval layer. **Disable All Sensitive APIs...** clears every optional category, disables
custom handlers, and denies Play Mode scene changes while preserving the port and auto-start.
When the file is absent, the legacy EditorPrefs/default behavior is unchanged until the first
schema-backed UI change. That change migrates the current effective values to a full v1 document
and saves it immediately. UI changes take effect in memory first and are written atomically as
UTF-8 without a BOM. Failed writes remain pending and retry automatically. Domain reloads restore
the working document from SessionState and do not reread disk; external edits are loaded on the
next Editor process start. Diagnostic lifecycle logging remains in EditorPrefs and does not create
the project file.

These settings prevent accidental operations and limit UnionAir's exposed routes; they are not an
authentication boundary, sandbox, or tamper defense. The file is not signed. A process that can
modify the project can edit it or add Editor code with the same Unity-process permissions. Treat
the project and every local API client as trusted, and use OS or environment isolation when a real
security boundary is required.

### 3. Verify operation

```bash
BASE_URL="$(tr -d '\r\n' < .unionair/endpoint.txt)"
curl "${BASE_URL}health"
# => {"status":"ok","unityVersion":"6000.3.5f2","projectPath":"C:\\Work\\MyProject"}
```

---

## Quick Start

### Get the scene hierarchy

```bash
curl "${BASE_URL}scene/hierarchy"
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
curl --get "${BASE_URL}gameobjects" \
  --data-urlencode 'target={"type":"hierarchyPath","value":"Main Camera"}'
```

### Search the asset list

```bash
# All Texture2D
curl "${BASE_URL}assets?type=Texture2D"

# Filter by path
curl "${BASE_URL}assets?path=Assets/UI"
```

---

## Using the EditorWindow

| Item | Description |
|------|------|
| **Status** | Displays the server running state and port number |
| **Port Mode** | Automatic (default) or Fixed; Fixed accepts `1..65535` while stopped |
| **Auto Start on Load** | Whether to start the server automatically when the Editor starts |
| **Disable All Sensitive APIs...** | Clears optional API categories, Custom Handlers, and Play Mode scene changes without changing server settings |
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

Every successful start atomically replaces `.unionair/endpoint.txt`. Clean stop, replacement start,
assembly reload, Editor exit, and an observed unexpected listener stop remove the URL only when the
file still belongs to that server instance. Runtime discovery and temporary files are ignored by
`.unionair/.gitignore`; clients should reread the file after a refused connection.

Fixed mode retries the same configured port up to five times over approximately four seconds after a transient address-in-use failure. Automatic mode first tries the concrete port retained across the reload; if that port is still in use, it waits 0.1 seconds and tries it once more before moving to fresh candidates. It then probes up to eight distinct fresh ports immediately, skipping a candidate-specific listener rejection such as a conflicting URL reservation. A failed probe allocation or listener-thread start aborts the attempt with one concise error. Intermediate address-in-use failures are retained only in the lifecycle trace and do not pollute the Console or `/api/editor/logs`. If the listener thread exits unexpectedly, UnionAir completes listener cleanup before dumping the diagnostic trace and schedules up to three delayed recovery attempts per domain. Further unexpected exits stop automatic recovery and produce a concise error instead of entering an unbounded restart loop. UnionAir silently retains a bounded lifecycle trace across domain reloads and automatically dumps it once per domain when startup or cleanup fails. Enable **Diagnostic Lifecycle Logging** to stream the same process, reload generation, listener cleanup, thread, and native socket details during normal operation.

A deferred handler owns its response lifetime. Closing the listener aborts any deferred connection that remains active during shutdown, so deferred handlers must tolerate response writes failing after a reload or server stop.
