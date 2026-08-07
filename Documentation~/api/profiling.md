# API Reference - Profiling
**English** | [日本語](profiling.ja.md)

Base URL: read it from `<project>/.unionair/endpoint.txt` at connection time. The port mode defaults to Automatic, so there is no fixed default port, and the file must be reread after a refused connection. [Check the server](../index.md#2-check-the-server) describes the handshake.

The **Profiling** category is disabled by default. Enable it in **Window > UnionAir > REST Bridge** only for trusted local clients. Captures can be large and memory snapshots can contain project and managed-heap data.

UnionAir profiles the Unity Editor process. Play Mode samples therefore include Editor overhead and are intended for local debugging and regression investigation, not as a substitute for profiling a Development Player on target hardware.

## GET /api/profiling/metrics

Lists counters and markers available to `ProfilerRecorder` in the current Editor. Use the returned `metricId` strings when creating a session.

Query parameters: `search`, `category`, `offset` (default `0`), and `limit` (default `100`, maximum `1000`). Common markers have stable aliases: `mainThreadTime`, `renderThreadTime`, `gcAllocInFrame`, `gcUsedMemory`, `totalUsedMemory`, and `totalReservedMemory`. Other IDs use `<category>:<marker>`.

```json
{
  "schemaVersion": 1,
  "total": 1,
  "offset": 0,
  "limit": 100,
  "metrics": [{
    "metricId": "mainThreadTime",
    "category": "Internal",
    "marker": "Main Thread",
    "unit": "ms",
    "dataType": "Int64",
    "available": true
  }]
}
```

## POST /api/profiling/sessions

Starts one asynchronous profiling session. Only one armed or active session is allowed.

```json
{
  "label": "inventory-scroll",
  "metrics": ["mainThreadTime", "gcAllocInFrame", "gcUsedMemory"],
  "warmupFrames": 60,
  "maxFrames": 600,
  "maxDurationSeconds": 30,
  "captureRaw": false
}
```

All fields are optional. Defaults are the available standard aliases, 60 warmup frames, 600 measured frames, 30 seconds, and no raw capture. `metrics` accepts at most 64 unique metric IDs, `warmupFrames` accepts 0-10000, `maxFrames` 1-100000, and `maxDurationSeconds` greater than 0 through 3600. More than 64 metrics returns `422` to bound recorder overhead and in-memory statistics.

Explicit unavailable metrics return `422`. Another session or an existing external Profiler binary-log configuration returns `409`. Storage at or above 5 GiB returns `507` until completed artifacts are removed.

## GET /api/profiling/sessions

Lists retained session metadata. UnionAir retains the newest 10 profiling sessions subject to the shared 5 GiB profiling-artifact limit.

## GET /api/profiling/sessions/{id}

Returns configuration, source, environment, sampling continuity, metric statistics, and artifacts. States are `armed`, `warming`, `running`, `completed`, `aborted`, or `failed`.

Metric statistics are finalized when the session reaches a terminal state and are then read from cached metadata. While a session is `armed`, `warming`, or `running`, `metrics` is an empty object; poll the lightweight state and sampling fields, then read the finalized statistics after completion. This avoids repeatedly scanning the growing NDJSON stream and disturbing the workload being measured.

```json
{
  "schemaVersion": 1,
  "id": "...",
  "state": "completed",
  "source": { "type": "manual", "testRunId": null },
  "sampling": {
    "capturedFrames": 600,
    "elapsedSeconds": 10.2,
    "segments": 1,
    "domainReloadCount": 0,
    "continuous": true,
    "interruptionReason": null
  },
  "metrics": {
    "mainThreadTime": {
      "unit": "ms",
      "samples": 600,
      "min": 4.1,
      "max": 18.2,
      "mean": 7.3,
      "p50": 7.0,
      "p95": 9.8,
      "p99": 13.4,
      "first": 7.2,
      "last": 7.4,
      "delta": 0.2,
      "nonZeroSamples": 600
    }
  },
  "artifacts": {
    "samples": {
      "projectRelativePath": "Library/UnionAir/Profiling/.../samples.ndjson",
      "url": "/api/profiling/sessions/.../samples.ndjson",
      "sizeBytes": 12345,
      "sha256": "..."
    },
    "profilerRaw": null
  }
}
```

Assembly reloads resume the same session as a new segment and repeat its warmup. Such results set `continuous:false`; agents should not interpret values across the gap as a continuous time series.

If recorder restoration fails after an assembly reload, UnionAir marks the session `failed`, restores owned Profiler settings, and removes the active-session lock. Calling `stop` also repairs a persisted active record whose in-memory recorder state was lost.

## POST /api/profiling/sessions/{id}/stop

Stops and finalizes a session. Calling it for an already finalized session is idempotent and returns its current result.

## DELETE /api/profiling/sessions/{id}

Deletes a completed session and all its artifacts. Active sessions return `409`.

## GET /api/profiling/sessions/{id}/samples.ndjson

Downloads frame samples as `application/x-ndjson`. Metric values follow the order in the session configuration.

This endpoint is available while the session is active. In that case it returns a stable partial snapshot bounded to the file length observed when the download opened; frames appended afterward are available on the next request.

```jsonl
{"segment":1,"frame":1,"segmentFrame":61,"elapsedMs":1016.7,"values":[7.2,0,104857600]}
```

## GET /api/profiling/sessions/{id}/profile.raw

Downloads the Unity Profiler binary log when `captureRaw:true` was used and the session has completed.

NDJSON, `.raw`, and `.snap` response bodies are streamed on a background I/O thread after request validation, so a large download does not occupy the Unity Editor update loop. Client disconnection ends only that response and does not remove the retained artifact.

## Memory snapshots

### POST /api/memory-snapshots

Starts an asynchronous Memory Profiler snapshot with managed objects, native objects, and native allocations. Only one capture may run at a time.

```json
{
  "label": "after-20-load-cycles",
  "profilingSessionId": "optional-related-id",
  "testRunId": "optional-related-id"
}
```

### GET /api/memory-snapshots and GET /api/memory-snapshots/{id}

List captures or return one capture. States are `capturing`, `completed`, or `failed`. The response contains environment metadata, before/after coarse memory counters, and the `.snap` artifact. These counters and snapshot pairs are evidence for investigation; UnionAir does not label a result as a confirmed leak.

UnionAir retains the newest four snapshots subject to the shared 5 GiB limit. A capture interrupted by assembly reload is marked failed, and partial files are not exposed.

### GET /api/memory-snapshots/{id}/snapshot

Downloads the completed `.snap` as `application/octet-stream` for the Unity Memory Profiler or a local analysis tool.

### DELETE /api/memory-snapshots/{id}

Deletes a completed or failed snapshot. A capture in progress returns `409`.

## Test Runner integration

When Unity Test Framework is installed, `POST /api/test-runs` accepts an optional `profiling` object with the same fields as session creation. Both the **Test Runner** and **Profiling** categories must be enabled.

```json
{
  "mode": "playMode",
  "testNames": ["Example.InventoryPerformanceTest"],
  "profiling": {
    "metrics": ["mainThreadTime", "gcAllocInFrame"],
    "warmupFrames": 30,
    "maxFrames": 300
  }
}
```

The test response and status include `profilingSessionId` and `profilingUrl`. The session is armed before execution, begins at `RunStarted`, and finalizes on completion, cancellation, or abort. Profiling status and artifact downloads remain available through the test-run lock; creating another session does not.
