# API Reference — Test Runner
**English** | [日本語](testing.ja.md)

Base URL: `http://localhost:<port>/api/` (default port: **8765**). See the [API Reference index](../api-reference.md) for common response and security conventions.

The Test Runner API is present only when `com.unity.test-framework` is installed. The **Test Runner** category is disabled by default and must be enabled in **Window > UnionAir > REST Bridge**.

UnionAir retains only the current run metadata and the latest completed UnionAir run. It does not keep history or case-level JSON results. Download `results.xml` immediately when a durable report is required.

---

## GET /api/tests

Discovers leaf tests and returns a flat, paged list. Discovery is asynchronous inside the Editor; only one discovery request may be active at a time.

Suite nodes — namespaces, classes, and parameterized methods — are not listed, even though they are valid `testNames` and `groupNames` values for [POST /api/test-runs](#post-apitest-runs).

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

Starts one asynchronous EditMode or PlayMode test run and returns `202 Accepted`. Omit every filter to run all tests in the selected mode; see [Filters](#filters) for how the four filter fields select tests.

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

`mode` is required. `profiling` is optional and uses the [Profiling session configuration](profiling.md#post-apiprofilingsessions). Both the Test Runner and Profiling categories must be enabled when it is present.

### Filters

The four filter fields are optional arrays of non-empty strings and reach the Unity Test Framework unchanged. `groupNames` entries are validated as regular expressions before execution; beyond that, no field is checked against the tests that actually exist.

| Field | Matching | Case | Selects suites |
|-------|----------|------|----------------|
| `testNames` | Exact full name; not a regular expression | Sensitive | Yes |
| `groupNames` | Unanchored regular expression over the full name | Per pattern | Yes |
| `categoryNames` | Unanchored regular expression over each category | Per pattern | — |
| `assemblyNames` | Exact assembly name, without `.dll` | Insensitive | — |

Values within one field are combined with OR, and different fields are combined with AND. A value starting with `!` excludes what it matches instead of selecting it. A test that declares no category is matched as `Uncategorized`.

`testNames` and `groupNames` also match suites: a namespace, a class, and a parameterized method are each a node in the test tree, and selecting one runs every test under it. Those names are not returned by [GET /api/tests](#get-apitests), which lists leaf tests only, so a caller filtering by class or namespace derives the name itself. A parameterized method's node name ends immediately before the opening parenthesis and must not include it: `Example.EditorTests.Rounds` selects every case of `Example.EditorTests.Rounds(1,2)`, while `Example.EditorTests.Rounds(` matches nothing. The leaf names below the method carry the argument list, which can contain `.` and `(` of its own.

### Runs that match nothing

A filter that matches no test is not an error. The run completes with `result: "passed"`, `progress.completed: 0`, and an all-zero summary, because nothing ran and so nothing failed. A misspelled assembly, a renamed test assembly, a category that no longer exists, and a `groupNames` pattern that stopped matching all end here.

`result` reports whether anything failed, not whether the filters selected what the caller meant. A client that filters should compare `progress.completed` against the number of tests it expected the filter to select. `progress.total` is not that number; see [GET /api/test-runs/{id}](#get-apitest-runsid).

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

`id` is issued by UnionAir. It is the id every endpoint on this page takes, and it is not the Unity Test Framework's own run identifier — the framework returns that only once the run has been dispatched, which is too late to record the run before starting it. UnionAir keeps the framework's identifier privately, for cancellation.

The endpoint returns `409` while the Editor is playing or changing Play mode, compiling, updating, or already running tests. It returns `503` if the installed Unity Test Framework version does not provide the active-run inspection required to prevent concurrent execution; UnionAir logs this compatibility failure once per domain load. It returns `500` when the run record could not be written, in which case nothing was handed to the Unity Test Framework; see [Editor Activities](activities.md). UnionAir adds no timeout; test hangs and cancellation cleanup follow Unity Test Framework behavior.

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
  "progress": { "completed": 1, "total": 128 },
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

`progress.completed` is the number of test cases that reported a terminal result — the sum of the four `summary` counts. A skipped or inconclusive case is counted even though its body never ran, so a caller confirming that tests actually executed reads `summary.skipped` alongside it. `progress.total` is the size of the test tree for the mode and does not narrow with the filters, so it is an upper bound rather than the number of tests the filters selected — a run filtered to one assembly reaches a terminal state with `completed` well below `total` even when every selected test passed. Compare an expected count against `completed`, not against `total`. See [Runs that match nothing](#runs-that-match-nothing).

Current metadata survives domain reload. If incomplete metadata remains after an Editor restart, UnionAir marks it `aborted` without replacing the previous latest XML.

Per-test progress is served directly from memory and persisted at most twice per second, with immediate persistence at run state changes and before assembly reload. An abrupt process crash can therefore lose up to approximately 0.5 seconds of progress metadata, but does not affect the retained latest result XML.

---

## DELETE /api/test-runs/{id}

Requests cancellation of the active UnionAir run and returns `202` with state `canceling`. Unknown or external run IDs return `404`; completed, already-canceling, or otherwise non-cancelable runs return `409`. A run whose Unity Test Framework handle is unavailable also returns `409`, which happens only when the framework accepted the run without naming it. Cancellation is asynchronous and status should continue to be polled.

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
