# API Reference — Compile
**English** | [日本語](compile.ja.md)

Base URL: `http://localhost:<port>/api/` (default port: **8765**). See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

UnionAir records every script compilation cycle as a structured result with per-message `file`, `line`, `column`, and `code`. Cycles started outside UnionAir are recorded too — saving a file in an IDE and letting Unity auto-refresh on focus is the most common way a project recompiles.

The read endpoints below are in the **Read** category and are therefore available by default.

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
| `target` | string | `editor`, `player`, or `other`, from the assembly output directory |
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
| `completed` | `upToDate` | Nothing needed compiling |
| `completed` | `failed` | At least one error was reported |
| `aborted` | `aborted` | The cycle started but never reported a result |
| `aborted` | `notStarted` | Compilation was requested but no cycle ever started |

`aborted` covers cancelling compilation from the Editor progress bar, a forced domain reload, and quitting mid-cycle. A request that never produces a cycle — for example when an `.asmdef` is malformed — resolves to `notStarted` with an `error` message.

### Domain Reloads

Unity reloads the assembly domain only when the **whole** build succeeds, and the reload stops the UnionAir server for its duration.

- A `failed` cycle does **not** reload. The server stays up and the result is readable on the same connection. This is the fast path when fixing errors.
- A `succeeded` cycle usually reloads, but not always — Play mode, locked assembly reloading, and cycles with nothing to load all suppress it. Do not treat `succeeded` as a promise that a reload will happen.
- An `upToDate` cycle never reloads.

Because one failing assembly suppresses the reload for the entire cycle, a failing script in `Assets` also prevents newly compiled package code from loading.

See [`GET /api/editor/status`](editor.md#detecting-domain-reloads) for `lifecycleGeneration`, which confirms a reload completed after a dropped connection.

### Example

```bash
curl http://localhost:8765/api/compile
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

## Related Documentation

- [Editor API](editor.md) — `lifecycleGeneration`, `settled`, and Console logs
- [API Reference index](../api-reference.md)
