# API Reference — Compile
**English** | [日本語](compile.ja.md)

Base URL: `http://localhost:<port>/api/` (default port: **8765**). See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

UnionAir records every script compilation cycle as a structured result with per-message `file`, `line`, `column`, and `code`. Cycles started outside UnionAir are recorded too — saving a file in an IDE and letting Unity auto-refresh on focus is the most common way a project recompiles.

The read endpoints below are in the **Read** category and are therefore available by default.

---

## POST /api/compile

Requests a script compilation and returns `202` with the id to poll.

> Can be called only when the Asset Write category is enabled.
> The endpoint risk is `assetUpdate`.

### Request

```json
{
  "refresh": true,
  "clean": false,
  "requestId": "my-run-1"
}
```

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `refresh` | No | `true` | Import pending asset changes before compiling |
| `clean` | No | `false` | Clear the build cache and rebuild every assembly |
| `requestId` | No | ― | Caller-supplied id; letters, digits, hyphens, and underscores, at most 64 characters; Windows device names such as `CON`, `NUL`, `COM1`, and `LPT1` are rejected |

Leave `refresh` enabled unless the files were already imported: a newly written `.cs` file belongs to no assembly until Unity imports it, so compiling without a refresh would not see it. Refreshing starts a cycle by itself when scripts changed, and UnionAir requests one explicitly only when it does not — which is what makes an `upToDate` result observable.

`clean` maps to `RequestScriptCompilationOptions.CleanBuildCache` and rebuilds everything, which can take minutes.

Supplying `requestId` makes the request recoverable. If the response is lost, poll `GET /api/compile/{requestId}` instead of issuing a second request.

When `refresh` is `true`, the same [loaded-scene external-change guard](editor.md#loaded-scene-conflict--409) as `POST /api/editor/refresh` runs before a compile record is created. Save or unload every reported scene explicitly before retrying. Set `refresh: false` only when the required file changes have already been imported.

### Response — 202

```json
{
  "id": "c-20260728-040030-67c0fd",
  "state": "queued",
  "source": "unionAir",
  "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
  "lifecycleGenerationAtRequest": 6,
  "statusUrl": "/api/compile/c-20260728-040030-67c0fd"
}
```

The record is persisted and this response is sent **before** any compilation work begins. Refreshing and compiling block the Unity main thread and can end in a domain reload, which would otherwise drop the connection before the caller learned the id it needs to poll.

### Status Codes

`409` with an `activeCompile` object when a compilation is already running:

```json
{
  "error": "A script compilation is already active.",
  "activeCompile": { "id": "c-20260728-041549-2194f1", "source": "unionAir", "state": "queued" }
}
```

This is the expected answer to losing a race with an IDE-triggered compilation, not a failure. Switch to polling `GET /api/compile` rather than retrying the request.

`409` with an `existingCompile` object when `requestId` was already used within the retained window; the body contains the full existing record.

With `refresh: true`, `409` with `code: "loaded_scene_external_change_blocked"` when a loaded scene changed externally. The response has the same `loadedScenes` fields and recovery procedure documented for [`POST /api/editor/refresh`](editor.md#loaded-scene-conflict--409). No compile record is created by this preflight rejection.

The guard runs again immediately before the scheduled refresh. If a scene changes during that small interval after the `202` response, the retained compile record resolves to `state: "aborted"` and `result: "notStarted"` with the conflicting scene paths in `error`; `AssetDatabase.Refresh()` is not called.

`409` when the Editor is entering or in Play mode, or while assets are updating. `400` when `requestId` contains unsupported characters or is a reserved Windows device name.

```bash
curl -X POST http://localhost:8765/api/compile \
  -H "Content-Type: application/json" \
  -d '{"refresh":true}'
```

---

## GET /api/compile

Returns the in-flight compilation as `current` and the most recently completed **Editor** compilation as `latest`. Either may be `null`.

Both are returned in one response because a polling client needs them in the same snapshot: a cycle that finishes between two separate requests would otherwise be missed as it moves from `current` to `latest`.

### Response

```json
{
  "current": null,
  "latest": {
    "id": "c-20260728-040030-67c0fd",
    "source": "external",
    "state": "completed",
    "result": "failed",
    "target": "editor",
    "sessionId": "f40cbf3fc3224a97b5b7ac7aa3b1ea38",
    "requestedAt": "2026-07-28T04:00:30.8656327Z",
    "startedAt": "2026-07-28T04:00:30.8656327Z",
    "finishedAt": "2026-07-28T04:00:32.0433942Z",
    "durationSeconds": 1.1777615,
    "lifecycleGenerationAtRequest": 6,
    "lifecycleGenerationAtFinish": 6,
    "errorCount": 1,
    "warningCount": 0,
    "assemblies": [
      {
        "name": "Assembly-CSharp",
        "path": "Library/ScriptAssemblies/Assembly-CSharp.dll",
        "outputDirectory": "Library/ScriptAssemblies",
        "compiled": true,
        "errorCount": 1,
        "warningCount": 0
      }
    ],
    "unchangedAssemblyCount": 71,
    "messages": [
      {
        "severity": "error",
        "code": "CS0103",
        "file": "Assets/Scratch/Player.cs",
        "line": 9,
        "column": 19,
        "assembly": "Assembly-CSharp",
        "message": "The name 'bar' does not exist in the current context",
        "raw": "Assets\\Scratch\\Player.cs(9,19): error CS0103: The name 'bar' does not exist in the current context"
      }
    ],
    "messagesTruncated": false,
    "error": null
  }
}
```

| Field | Type | Description |
|-----------|-----|------|
| `id` | string | Identifier for this cycle |
| `source` | string | `unionAir` when requested through the API, `external` otherwise |
| `state` | string | `queued`, `running`, `completed`, or `aborted` |
| `result` | string \| null | See the result table below; `null` while the cycle is active |
| `target` | string | `editor` only when every compiled output is an Editor assembly, `player` when every output is a player assembly, otherwise `other` |
| `sessionId` | string | Editor process the record belongs to |
| `durationSeconds` | number | Time between `startedAt` and `finishedAt` |
| `lifecycleGenerationAtRequest` | number | `lifecycleGeneration` when the cycle was recorded |
| `lifecycleGenerationAtFinish` | number | `lifecycleGeneration` when the cycle reported its result |
| `errorCount` / `warningCount` | number | True totals, even when `messages` is truncated |
| `assemblies` | array | Assemblies that were actually compiled |
| `unchangedAssemblyCount` | number | Assemblies Unity reported as not needing compilation |
| `messages` | array | Diagnostics, errors first, then by file and line |
| `messagesTruncated` | bool | Whether more than 200 diagnostics were produced |
| `error` | string \| null | Why the cycle was aborted, when applicable |

### Message Fields

| Field | Type | Description |
|-----------|-----|------|
| `severity` | string | `error`, `warning`, or `info`, taken from the compiler message type |
| `code` | string \| null | Diagnostic code such as `CS0103` or `UNT0001`; `null` when the message carries none |
| `file` | string \| null | Project-relative path with forward slashes; `null` for build-system diagnostics |
| `line` / `column` | number \| null | 1-based position; `null` when the diagnostic has no source position |
| `message` | string | Text with Unity's position and code prefix removed |
| `raw` | string | Original message exactly as the compiler reported it |

> `severity` comes from the compiler message type, never from the message text, because the words "error" and "warning" are localized while the code token is not.
> Individual messages are capped at 4000 characters and the list at 200 entries.

### State and Result

| `state` | `result` | Meaning |
|---------|----------|---------|
| `queued` | `null` | Compilation was requested but has not started |
| `running` | `null` | Compilation is in progress |
| `completed` | `succeeded` | At least one assembly compiled with no errors |
| `completed` | `upToDate` | Unity reported zero compiled assemblies; this also occurs for some removal-only cycles |
| `completed` | `failed` | At least one error was reported |
| `aborted` | `aborted` | The cycle started but never reported a result |
| `aborted` | `notStarted` | Compilation was requested but no cycle ever started |

`aborted` covers cancelling compilation from the Editor progress bar, a forced domain reload, and quitting mid-cycle. A request that never produces a cycle — for example when an `.asmdef` is malformed — resolves to `notStarted` with an `error` message.

### Domain Reloads

Unity reloads the assembly domain only when the **whole** build succeeds, and the reload stops the UnionAir server for its duration.

- A `failed` cycle does **not** reload. The server stays up and the result is readable on the same connection. This is the fast path when fixing errors.
- A `succeeded` cycle usually reloads, but not always — Play mode, locked assembly reloading, and cycles with nothing to load all suppress it. Do not treat `succeeded` as a promise that a reload will happen.
- An `upToDate` cycle can still reload when the cycle removes an assembly, such as deleting the last user script. Like `succeeded`, it does not predict reload behavior.

Because one failing assembly suppresses the reload for the entire cycle, a failing script in `Assets` also prevents newly compiled package code from loading.

See [`GET /api/editor/status`](editor.md#detecting-domain-reloads) for `lifecycleGeneration`, which confirms a reload completed after a dropped connection.

### Example

```bash
curl http://localhost:8765/api/compile
```

---

## GET /api/compile/records

Lists retained terminal compilation records as bounded, newest-first summaries. The active `current` record is not included; read it from `GET /api/compile`. Use each summary's `statusUrl` to retrieve the complete record.

| Query | Default | Description |
|-------|---------|-------------|
| `offset` | `0` | Non-negative offset within the filtered results |
| `limit` | `20` | Page size from 1 to 100 |
| `target` | all | Exact `editor`, `player`, or `other` filter, case-insensitive |
| `source` | all | Exact `unionAir` or `external` filter, case-insensitive |
| `state` | all | Exact `completed` or `aborted` filter, case-insensitive |

Filters are applied before pagination. Records are ordered by `finishedAt` descending, then `requestedAt` descending, then `id` descending, so repeated requests over unchanged history are deterministic. `total` is the number of filtered records before pagination and `hasMore` reports whether another record follows the returned page.

```json
{
  "total": 1,
  "offset": 0,
  "limit": 20,
  "hasMore": false,
  "records": [
    {
      "id": "c-20260728-040030-67c0fd",
      "source": "external",
      "state": "completed",
      "result": "succeeded",
      "target": "player",
      "requestedAt": "2026-07-28T04:00:30.0000000Z",
      "startedAt": "2026-07-28T04:00:30.1000000Z",
      "finishedAt": "2026-07-28T04:00:34.0000000Z",
      "durationSeconds": 3.9,
      "errorCount": 0,
      "warningCount": 0,
      "statusUrl": "/api/compile/c-20260728-040030-67c0fd"
    }
  ]
}
```

Invalid filter or pagination values return `400`. An empty history returns `total: 0` and an empty `records` array.

```bash
curl "http://localhost:8765/api/compile/records?target=player&offset=0&limit=20"
```

---

## GET /api/compile/{id}

Returns one retained compilation record.

Use this rather than `latest` to confirm that a **specific** cycle finished. A compilation started from an IDE can replace `latest` at any moment, so `latest` answers "did a compilation finish", not "did mine finish".

UnionAir retains the 20 most recent records under `Library/UnionAir/Compile/records`. Evicted or unknown ids return `404`. An id containing anything other than letters, digits, hyphens, and underscores returns `400`.

The response body is the same record object shown above, without the `current`/`latest` wrapper.

```bash
curl http://localhost:8765/api/compile/c-20260728-040030-67c0fd
```

---

## The Compile-and-Fix Loop

The loop an automated client runs is: write a `.cs` file, request a compilation, read the diagnostics, fix, repeat.

```
1. Write the file.
2. POST /api/compile                 -> 202 { id, lifecycleGenerationAtRequest }
3. Poll GET /api/compile/{id} until state is "completed" or "aborted".
4. result == "failed"    -> fix using messages[].file / line / column, go to 2
   result == "succeeded" -> done
   result == "upToDate"  -> done
   state  == "aborted"   -> report error; do not retry blindly
```

A cycle typically settles in a few seconds.

### Terminating Correctly

This is where an automated client is most likely to hang. Neither `succeeded` nor `upToDate` predicts whether a domain reload will happen. Play mode and locked assembly reloading can suppress it, while a removal-only cycle can report zero compiled assemblies and still reload. Nothing in the Unity API lets UnionAir predict which. A client that waits unconditionally for `lifecycleGeneration` to advance will wait forever whenever no reload occurs.

Terminate like this instead:

1. `failed` — **done. Never wait for a reload**; a failed whole build does not reload.
2. `succeeded` or `upToDate`, and the server still answers with `settled: true` — the compile result is done; proceed without waiting pre-emptively.
3. If the connection **drops** after either successful result — reconnect and wait for `lifecycleGeneration` to exceed `lifecycleGenerationAtRequest`, confirming a reload completed rather than the Editor crashing.
4. **Give every wait an explicit timeout.** No step above should poll without a bound.

> `settled` is a snapshot, not a guarantee. Compilation clears `isCompiling` slightly before the native domain reload begins, so a client can legitimately observe `settled: true` immediately before losing the connection. That is why step 3 exists and why timeouts are required.

### Tolerate Dropped Connections Everywhere

Treat a refused connection as a normal condition on **every** request, not only after requesting a compilation. Someone saving a file in an IDE can trigger a compilation and a domain reload at any moment, and the server stops for the duration of every reload.

During compilation's synchronous tail Unity blocks its main thread. `EditorApplication.update` stalls with it, and UnionAir dispatches queued HTTP requests from that same loop, so requests are answered late. Multi-second latency is expected and is not a failure signal.

### One Failure Blocks Everything

Unity reloads the assembly domain only when the whole build succeeds. A single failing script in `Assets` therefore prevents *all* newly compiled code from loading, including packages that compiled successfully. When a change to package code appears not to have taken effect, check `GET /api/compile` for an unrelated failure before looking anywhere else.

### Losing a Race

If a compilation started elsewhere while the request was in flight, `POST /api/compile` returns `409` with an `activeCompile` object. That is the correct answer, not an error to retry: switch to polling `GET /api/compile` and treat the active cycle as the one to wait on.

---

## Related Documentation

- [Editor API](editor.md) — `lifecycleGeneration`, `settled`, and Console logs
- [API Reference index](../api-reference.md)
