# API Reference — Editor Activities
**English** | [日本語](activities.ja.md)

Base URL: read it from `<project>/.unionair/endpoint.txt` at connection time. The port mode defaults to Automatic, so there is no fixed default port, and the file must be reread after a refused connection. [Check the server](../index.md#2-check-the-server) describes the handshake. See the [API Reference index](../api-reference.md) for response conventions and category/security notes.

This page is not an endpoint group. It describes the single vocabulary UnionAir uses for "the Unity Editor is busy with X", which appears in `GET /api/help`, in `GET /api/editor/status`, and in every `409` that means *retry later* rather than *your request was wrong*.

---

## The activities

| Activity | Meaning | Identified by |
|----------|---------|---------------|
| `buildTargetSwitch` | The active build target is being switched, with the reimport and domain reload it causes | The switch record; see [Build](build.md#post-apibuildtarget) |
| `build` | A player build is queued or running | The build record |
| `testRun` | A Unity Test Framework run is active | The run record, for runs UnionAir started |
| `playMode` | The Editor is in Play mode, or entering or leaving it | Nothing; observed from the Editor |
| `compile` | A script compilation is queued or running | The compile record, when UnionAir is tracking one; otherwise nothing |
| `assetUpdate` | The Editor is importing or refreshing assets | Nothing; observed from the Editor |

The order above is the **priority order**. When more than one is running, UnionAir reports and blames the one nearest the top. That is deliberate: a build runs its own compilation, and a test run drives Play mode, so blaming the inner activity would tell a client to wait for the wrong thing and to retry far too early.

Activities come from two places. `build`, `buildTargetSwitch`, and `testRun` are **declared**: a UnionAir service starts them, or adopts one an external tool started, and their identity survives a domain reload. `playMode` and `assetUpdate` are **observed** from `EditorApplication` at request time — Unity already tracks them, and a second copy could only be wrong. An observed activity reports `source: null` and `id: null`, because there is nothing to name.

`compile` is **both**. It is declared while UnionAir tracks a cycle, and observed from `EditorApplication.isCompiling` otherwise — during the moment between Unity starting a cycle and UnionAir adopting it, and again in the tail after a cycle has been recorded but before the domain reload begins. In those windows it reports `source: null` and `id: null` like an observed activity, because there is genuinely no record to point at. `GET /api/compile` is the fallback: it answers with `current` and `latest` regardless.

---

## Reading what the Editor is busy with

`GET /api/editor/status` reports it:

```json
{
  "settled": false,
  "activeActivity": { "activity": "testRun", "source": "unionAir", "id": "41a31ce1-7921-448c-9ca3-21d0b14d3094" }
}
```

`activeActivity` is `null` when the Editor is idle.

| Field | Type | Description |
|-------|------|-------------|
| `activity` | string | One of the activity names above |
| `source` | string \| null | `unionAir` when UnionAir started it, `external` when it was adopted, `build` for a build's own compilation, `null` for an observed activity |
| `id` | string \| null | Record id that owns the activity, or `null` when there is none to poll |

An adopted external activity reports `id: null` rather than an empty string. There is no UnionAir record behind it, and reporting an empty id would invite a client to poll a record that was never created.

---

## Being rejected by one

A request blocked by an activity returns `409` with `activeActivity`:

```json
{
  "error": "This endpoint cannot be used while a script compilation is active.",
  "activeActivity": { "activity": "compile", "source": "unionAir", "id": "c-20260802-093318-a1b2c3" }
}
```

This is not an error to retry immediately. Poll the activity that owns the Editor — `GET /api/compile/{id}` here — and reissue the request once it settles.

Some rejections carry an additional object describing the same activity in the vocabulary of the endpoint that is blocked. These are kept for compatibility and say nothing `activeActivity` does not:

- `activeTestRun` on any endpoint blocked by a test run.
- `activeCompile` on `POST /api/compile` when a compilation is already running.

---

## What blocks each endpoint

`GET /api/help` reports `blockedDuring` per endpoint and per category:

```json
{
  "method": "POST",
  "path": "/api/editor/play",
  "category": "playMode",
  "playModePolicy": "allowed",
  "testRunPolicy": "blocked",
  "blockedDuring": ["buildTargetSwitch", "build", "testRun", "compile"]
}
```

`blockedDuring` is the complete list, in priority order. It includes the activities enforced by `playModePolicy` and `testRunPolicy`, so a client reads one array instead of reconstructing the answer from three fields.

Most of it is declared per **category**, because the conflict is usually a property of what a category does rather than of an individual route: Scene Write, Asset Write, Editor Actions, and Play Mode are all rejected during a build and during a build target switch, since both read the project from disk while they run and a write racing either produces output matching nothing the caller can inspect. Individual endpoints add to that — `POST /api/editor/play` is also rejected during a compilation, because Unity reloads the domain when the cycle finishes and discards the mode change with it.

`POST /api/editor/stop`, `pause`, and `step` are deliberately **not** blocked during a compilation. Stopping a running game is exactly what a client needs to be able to do while something else is in flight.

---

## Enforcement is not the same as reporting

`playModePolicy` and `testRunPolicy` remain the controls that decide those two activities, and they behave differently from a plain activity check:

- The test-run gate is evaluated **before** the category check, so an endpoint blocked by a test run returns `409` even when its category is disabled. A retry after the run may then return `403`.
- Play mode supports a per-request opt-in (`allowWhilePlaying`) that no activity mask can express, and it is evaluated **after** the category check.

Everything else — `compile`, `assetUpdate`, `build`, `buildTargetSwitch` — is enforced by one generic stage after the Play mode check. The metadata is unified even though the enforcement is not, so `blockedDuring` stays the single thing a client reads.

---

## Surviving domain reloads and crashes

Declared activities keep their identity in Unity's `SessionState`, which survives a domain reload but is cleared when the Editor process restarts. That difference is what makes crash recovery work, in both directions:

- **A record with no activity** belongs to a process that died. Its owning service finalizes it on the next initialization.
- **An activity with no record** is debris. Nothing would ever close it — `SessionState` outlives every reload — so the Editor would report itself busy, and reject every endpoint that declared a conflict, for the rest of the session. It is released on the next initialization with a Console warning rather than trusted. Only an activity UnionAir owns is judged this way: an adopted test run has no record by design, and is reconciled instead by watching whether the Unity Test Framework is still running one.

Both directions are checked because either one alone leaves a way to get stuck. This is the pattern `InputReplayService` already used, now shared.

### An operation UnionAir starts does not begin until its record is durable

A request that starts a tracked operation — `POST /api/compile`, `POST /api/builds`, `POST /api/build/target`, `POST /api/test-runs` — persists its record **before** opening the activity, and answers `500` without starting anything when that write fails. The write is retried once immediately, because a virus scanner or the search indexer holding the destination for a moment is the common transient cause and clears by itself; a full disk or an unwritable directory does not, and is reported rather than waited on.

A test run is the case where honoring that meant giving up the Unity Test Framework's run identifier: the framework returns it only once the run has been dispatched, so a record keyed by it could never be written first. UnionAir issues its own id and keeps the framework's privately, as the handle cancellation needs.

Activity UnionAir merely **adopts** cannot be refused — a compilation an IDE triggered is already running — so it is recorded best-effort. Its terminal path releases the activity regardless of whether any write succeeded.

Practically: a compilation, a test run, or a build interrupted by an Editor crash resolves to `aborted` rather than staying active forever, and a client polling by id learns that instead of waiting.

---

## Related Documentation

- [Editor API](editor.md) — `GET /api/editor/status`
- [Compile API](compile.md) — compile records, including the ones a build owns
- [API Reference index](../api-reference.md)
