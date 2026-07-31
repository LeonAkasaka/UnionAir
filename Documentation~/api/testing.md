# API Reference — Test Runner
**English** | [日本語](testing.ja.md)

Base URL: `http://localhost:<port>/api/` (default port: **8765**). See the [API Reference index](../api-reference.md) for common response and security conventions.

The Test Runner API is present only when `com.unity.test-framework` is installed. The **Test Runner** category is disabled by default and must be enabled in **Window > UnionAir > REST Bridge**.

UnionAir retains only the current run metadata and the latest completed UnionAir run. It does not keep history or case-level JSON results. Download `results.xml` immediately when a durable report is required.

---

## GET /api/tests

Discovers leaf tests and returns a flat, paged list. Discovery is asynchronous inside the Editor; only one discovery request may be active at a time.

### Query Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `mode` | yes | — | `editMode` or `playMode` |
| `search` | no | — | Case-insensitive substring of name, full name, or unique name |
| `assembly` | no | — | Exact test assembly name, without `.dll` |
| `category` | no | — | Exact NUnit category, case-insensitive |
| `offset` | no | `0` | Non-negative result offset |
| `limit` | no | `100` | Page size from 1 to 1000 |

### Response

```json
{
  "mode": "editMode",
  "total": 1,
  "offset": 0,
  "limit": 100,
  "tests": [{
    "name": "SavesAsset",
    "fullName": "Example.EditorTests.SavesAsset",
    "uniqueName": "Example.EditorTests.dll/Example/[Example.EditorTests.SavesAsset]",
    "assembly": "Example.EditorTests",
    "categories": ["Smoke"],
    "runState": "Runnable",
    "description": "",
    "skipReason": ""
  }]
}
```

Invalid parameters return `400`. Concurrent discovery returns `409`. A pending discovery interrupted by assembly reload is completed with `409` when possible.

---

## POST /api/test-runs

Starts one asynchronous EditMode or PlayMode test run and returns `202 Accepted`. Omit every filter to run all tests in the selected mode. Filter fields use Unity Test Framework `Filter` semantics; fields are combined by that framework.

### Request

```json
{
  "mode": "editMode",
  "testNames": ["Example.EditorTests.SavesAsset"],
  "groupNames": ["^Example\\."],
  "categoryNames": ["Smoke"],
  "assemblyNames": ["Example.EditorTests"],
  "profiling": {
    "metrics": ["mainThreadTime", "gcAllocInFrame"],
    "warmupFrames": 30,
    "maxFrames": 300
  }
}
```

`mode` is required. The four filters are optional arrays of non-empty strings. `groupNames` entries are regular expressions and are validated before execution. `profiling` is optional and uses the [Profiling session configuration](profiling.md#post-apiprofilingsessions). Both the Test Runner and Profiling categories must be enabled when it is present.

### Response — 202

```json
{
  "id": "0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352",
  "state": "queued",
  "statusUrl": "/api/test-runs/0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352",
  "resultUrl": "/api/test-runs/0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352/results.xml",
  "profilingSessionId": "8c3fe76a-4a6a-4dd9-a678-02b628ce5d12",
  "profilingUrl": "/api/profiling/sessions/8c3fe76a-4a6a-4dd9-a678-02b628ce5d12"
}
```

The endpoint returns `409` while the Editor is playing or changing Play mode, compiling, updating, or already running tests. It returns `503` if the installed Unity Test Framework version does not provide the active-run inspection required to prevent concurrent execution; UnionAir logs this compatibility failure once per domain load. UnionAir adds no timeout; test hangs and cancellation cleanup follow Unity Test Framework behavior.

---

## GET /api/test-runs/{id}

Returns the current run or the latest completed UnionAir run. Older IDs return `404`.

```json
{
  "id": "0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352",
  "state": "completed",
  "result": "passed",
  "mode": "editMode",
  "filters": {
    "testNames": ["Example.EditorTests.SavesAsset"],
    "groupNames": [],
    "categoryNames": [],
    "assemblyNames": []
  },
  "startedAt": "2026-07-18T05:00:00.0000000Z",
  "finishedAt": "2026-07-18T05:00:01.0000000Z",
  "currentTest": null,
  "progress": { "completed": 1, "total": 1 },
  "summary": {
    "passed": 1,
    "failed": 0,
    "skipped": 0,
    "inconclusive": 0,
    "duration": 0.15,
    "assertCount": 1
  },
  "resultFileAvailable": true,
  "resultUrl": "/api/test-runs/0dc6f2b8-9c31-4da0-82df-7dc8fb0dc352/results.xml",
  "profilingSessionId": null,
  "profilingUrl": null
}
```

`state` is `queued`, `running`, `canceling`, `completed`, or `aborted`. Before completion, `result` is `null`; afterwards it is `passed`, `failed`, `skipped`, `inconclusive`, `canceled`, or `aborted`. Timestamps are UTC ISO 8601 strings.

Current metadata survives domain reload. If incomplete metadata remains after an Editor restart, UnionAir marks it `aborted` without replacing the previous latest XML.

Per-test progress is served directly from memory and persisted at most twice per second, with immediate persistence at run state changes and before assembly reload. An abrupt process crash can therefore lose up to approximately 0.5 seconds of progress metadata, but does not affect the retained latest result XML.

---

## DELETE /api/test-runs/{id}

Requests cancellation of the active UnionAir run and returns `202` with state `canceling`. Unknown or external run IDs return `404`; completed, already-canceling, or otherwise non-cancelable runs return `409`. Cancellation is asynchronous and status should continue to be polled.

---

## GET /api/test-runs/{id}/results.xml

Downloads the complete NUnit XML saved by Unity Test Framework for the latest completed UnionAir run.

- Content type: `application/xml; charset=utf-8`
- Content disposition: `attachment; filename="TestResults-{id}.xml"`
- Active run ID: `409`
- Older, external, aborted-without-XML, or unavailable result: `404`

UnionAir writes `Library/UnionAir/TestRuns/latest.xml` through a recoverable transaction using temporary files, previous-file backups, a pending marker, and a SHA-256 integrity check. On startup, an interrupted transaction is either recognized as fully committed or rolled back to the previous XML and metadata. The filesystem path is an internal detail and is not exposed through the API. While a new run is active, the previous latest XML remains downloadable. A successful `RunFinished` save replaces it and makes the previous ID return `404`; an aborted run without XML leaves the previous result intact.

---

## Concurrency During Test Runs

Any Unity Test Framework run, including one started in the Test Runner Window, locks UnionAir operations. Only these requests remain available:

- `GET /api/health`, `GET /api/help`, `GET /api/editor/status`, and `GET /api/editor/logs`
- run status, result XML, and cancel endpoints above
- origin-free `OPTIONS` (requests carrying `Origin` are rejected with `403` before this lock is evaluated)

Every other built-in or custom endpoint returns `409` with the active run source and the UnionAir run ID when applicable. This lock is evaluated before category enablement, so it takes precedence over the `403` normally returned by a disabled category. External runs are observable through editor status and logs, but cannot be canceled and their results are not retained by UnionAir. If a completion callback is missed, UnionAir releases a stale gate only after Unity Test Framework has been positively observed as idle for a grace period; a stale UnionAir run is marked `aborted`.
