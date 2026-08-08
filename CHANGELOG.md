# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Upgrade Notes

- `GET /api/assets/animator-controllers/{guid}` reports `motion.guid` as `null` for a state whose motion is a blend tree, where it previously reported the controller's own GUID. Read `motion.type` to tell the two kinds apart. A client that passed those GUIDs to `GET /api/assets/animation-clips/{guid}` was already receiving a `400`; a client that used them to identify the controller was relying on an accident.
- `DELETE /api/assets/animation-clips/{guid}/curves` answers `400` for a `property` that names no binding on the clip, where it previously answered `200` and listed that name under `removed`. This affects a client that sent the name it wrote rather than the name `GET` returns -- `localPosition.y` instead of `m_LocalPosition.y` -- which is the spelling this endpoint's own documentation used as its example. No such request ever removed a curve, so nothing that worked stops working; what changes is that the failure is now visible to a client that reads the status code rather than the body. Send the serialized property name that `GET /api/assets/animation-clips/{guid}` reports.

### Changed

- `GET /api/assets/animator-controllers/{guid}` now says what each motion is, and describes blend trees instead of pointing at them. A `Motion` is either an `AnimationClip` or a `BlendTree`, and the previous response serialized both as `{guid, name}` with the GUID taken from the containing asset -- so a blend tree reported the controller's own GUID, and nothing in the response distinguished it from a clip. Following that GUID reached a `400` from the clip endpoint, which was the only way to discover the motion's kind and cost one request per state. Every motion now carries a `type` of `AnimationClip`, `BlendTree`, or `Unknown`; a blend tree carries a `null` GUID, because no GUID addresses a sub-asset, and its `blendType`, blend parameters, thresholds, and children are serialized inline, recursively, so a nested tree is described like any other. Nesting is serialized to a depth of 10, and a tree at that depth is marked `truncated` with no `children` rather than being presented as childless. `Unknown` is a forward-compatibility branch for a `Motion` subclass this version does not describe, and reports a GUID only when the motion is the main asset at its path, so an unknown sub-asset cannot report its container's GUID the way a blend tree used to; a motion whose asset was deleted still reports `null`, since Unity resolves a missing reference before its type can be examined.
- A clip motion now reports `assetPath` and `clipsAtPath` alongside its GUID, because the GUID is not always as precise as it looks. A clip imported from a model file lives inside that file, so the GUID identifies the file rather than the clip -- which is the ordinary case, since a character's animation set usually arrives as `.fbx` rather than as `.anim` assets. When `clipsAtPath` is greater than 1 the GUID cannot address one take, and the client can now see that rather than discovering it by fetching the wrong clip. Addressing an individual clip inside an imported file remains out of reach and belongs with the ModelImporter work.

### Added

- Added typed AudioImporter inspection and updates through `GET|PATCH /api/assets/audio-importer/{guid}`. Responses distinguish stored inherited defaults from Unity's platform-effective settings, publish the Editor's platform/codec compatibility catalog, and include final AudioClip metadata. Writes strictly preflight global fields, default sample settings, and atomic platform override creation/update/removal before one `SaveAndReimport`; unchanged requests skip the reimport, and completed imports return structured warning/error diagnostics. Preload policy is modelled inside default/platform sample settings to match Unity 6.

### Fixed

- Consecutive writes through the API accumulated into a single undo entry, so one Ctrl+Z took back all of them. Every scene write path named its undo group with `Undo.SetCurrentGroupName` but never opened one -- that call renames the current group, and `Undo.IncrementCurrentGroup` appeared nowhere in the package. `Undo.GetCurrentGroup` therefore returned the group the previous request was already in, and `Undo.CollapseUndoOperations` merged that request into this one. A hand edit was never affected, because Unity advances the group after a human interaction with the Editor; nothing advances it between two HTTP-triggered main-thread callbacks, which is exactly the case UnionAir is driven in. Measured: adding a component to one object and then to another, then a single Ctrl+Z, removed both. Each write path now opens its own group through a shared `UndoGroups.Begin`, so one request is one undo entry, and a batch request remains the single entry it was always meant to be.
- `DELETE /api/assets/animation-clips/{guid}/curves` removed nothing and reported that it had. It removed float curves with `AnimationClip.SetCurve(path, type, property, null)`, which does not remove a binding -- only the `AnimationUtility` form does -- and it appended every requested binding to `removed` unconditionally, so the response described the request rather than the result. A client deleting a curve received `{"removed":["localPosition.y"],"errors":[]}` and a clip that still held every curve it started with. Object reference curves were already removed correctly and are now covered by the same reporting.
- The same endpoint now addresses bindings by the serialized property name that `GET` returns. Adding and removing go through different Unity APIs, so the name a client writes is not always the name the clip stores: `POST .../curves` writes through `AnimationClip.SetCurve`, which expands a Transform vector property into all of its components -- a curve on `localPosition.y` is stored as `m_LocalPosition.x`, `.y`, and `.z`, with the untouched components pinned to the property's default value -- while removal goes through `AnimationUtility.SetEditorCurve`, which addresses exactly one binding. The expansion belongs to `SetCurve` rather than to the shorthand; the serialized name expands the same way. A `property` that matches nothing is reported in `errors` together with the names that are bound at that path and type, so the correct one can be read off the failure, and `removed` now lists only bindings that were present before the call and absent after it. A request where nothing was removed and at least one binding failed answers `400` rather than `200`, matching how the add endpoint already reports a wholly failed request.
- `POST` and `DELETE /api/assets/animation-clips/{guid}/curves` rejected a fully qualified type name for everything except five UI types. `type` accepted `Transform` but answered `Unknown type: UnityEngine.Transform`, while `UnityEngine.UI.Image` worked -- because the five UI names were written into a hand-maintained switch by hand and the fallback beneath it prepended `UnityEngine.` to whatever it was given, turning an already-qualified name into `UnityEngine.UnityEngine.Transform`. The documentation offered `UnityEngine.UI.Image` as its example of the form, so the spelling that looked recommended was the one that failed. These endpoints now share `ObjectRefUtils.ResolveType`, which every other endpoint already used, with `UnityEngine.Object` required as the base type: the shared resolver falls back to matching on simple name across every loaded assembly, and without that requirement `Image`, `Slider`, `Button`, and `Text` resolve to `UnityEngine.UIElements` and `System.Net.Mime` types instead of the `UnityEngine.UI` ones. Every spelling the switch accepted still resolves to the same type, and a type that is not a `UnityEngine.Object` is now reported as `Unknown type` rather than accepted.
- `ObjectRefUtils.ResolveType` threw on an empty type name instead of answering `null`, because `Assembly.GetType` rejects an empty string. No caller could reach it, since each rejected an empty name first; the animation curve endpoints default `type` rather than requiring it, so `"type": ""` reached the resolver and would have answered `500` with the exception text in the body. It answers `Unknown type` like any other name that resolves to nothing.

### Documentation

- The animation API reference now states which animation writes the Editor can undo. AnimatorController structure writes are undoable and always were -- the `UnityEditor.Animations` editing APIs register their own undo, so UnionAir adds none and a request that adds a state is taken back by one Ctrl+Z. AnimationClip curve writes are not, by choice: they are saved to disk before the response is sent, so a `200` means the file already changed and recovery belongs to version control; registering undo would revert the asset in memory while the file kept the written content until some later unrelated save. Asset creation is not undoable in Unity itself. Measured on 6000.0.80f1 rather than inferred, including the case that an earlier draft of #64 had backwards.

## [0.5.1] - 2026-08-08

### Fixed

- **Copy as curl** now uses the Base URL captured with the request log entry instead of substituting port `8765` when the server is stopped. Commands therefore preserve the actual host and port that received each request, including Automatic port assignments.
- Every API reference page opened by naming `8765` as the default port, which the same documentation set contradicts: the configured port has defaulted to Automatic since 0.4.0, and the Getting Started guide describes reading `.unionair/endpoint.txt` instead. A client implementer starting from an API page was told a fixed port and had no reason to look for the discovery file when the connection was refused. Every page now gives the Base URL shape without a port and names the discovery file, with the full handshake stated once in the API Reference index that each page already links to. The shell examples on `api/editor.md`, `api/build.md`, `api/compile.md`, and `api/gameobjects.md` use `${BASE_URL}` rather than a hardcoded port, and the `GET /api/help` response sample shows an ephemeral port so it no longer reads as a default.
- The `GET /api/help` documentation on `api/general.md` had fallen behind the manifest it describes. Its response sample still reported `"version": "0.2.0"` while the endpoint returns the version from `package.json`; the `build` category, added in 0.4.0, was missing from both the category constants and the `category` query filter; the `executableOutput` risk, added alongside it, was missing from the risk values; and `blockedDuring`, which the same release added to every category and endpoint item, was absent from both the response sample and the field table, as was `testRunPolicy`. The endpoint's behavior is unchanged; only the documentation was wrong.

## [0.5.0] - 2026-08-07

### Upgrade Notes

- Custom controllers receive `UnionAirRequest` and `UnionAirResponse` instead of `System.Net.HttpListenerRequest` and `HttpListenerResponse`. A controller that only passes `ctx.Request` and `ctx.Response` to `RequestBodyReader` and `RestResponse` compiles unchanged. A controller that declares its own helper typed against the framework types must change those parameter types. The `HttpListenerResponse` overloads of `RestResponse.Send`, `SendBinary`, `SendError`, and `SendNotFound` are removed rather than retained as obsolete.

### Changed

- The handler-facing request and response are now UnionAir's own abstract types, implemented by adapters over `HttpListener`. Both are `abstract class` with an `internal` constructor rather than interfaces, so members can be added later without breaking anyone, while `InternalsVisibleTo` still lets the test assemblies supply substitutes. The transport is no longer part of the contract, and a handler can no longer reach a stream that nothing else can observe: the request body stream is internal to `RequestBodyReader`, which is what makes a single cached read the rule rather than a convention.
- `RestRouter.Handle` now takes a request and a response instead of an `HttpListenerContext`, and the server wraps each dequeued context once before dispatching it.

### Added

- The Request Log gained a **Details** action per row, opening a window that shows the request line, headers, and body above the response status, content type, duration, and body. Pressing it for another entry updates the open window rather than adding one. Bodies can be copied or saved, and the request can be copied as an executable `curl` command with the active Base URL and the `Content-Type` the API requires already filled in; the command is not offered for an entry whose request body was truncated. It emits `curl.exe` rather than `curl`, because in Windows PowerShell 5.1 the bare name is an alias for `Invoke-WebRequest`. The arrow beside the button chooses the shell to quote for -- bash, PowerShell 7, or Windows PowerShell 5.1 -- and the choice is remembered in `EditorPrefs`, next to the window's tab and foldout state; which shell someone pastes into belongs to their machine rather than to the project, so it is deliberately not part of `.unionair/settings.json`. No single form works everywhere: bash and PowerShell 7 both take a single-quoted argument verbatim but write a literal single quote differently, while Windows PowerShell 5.1 cannot be served by ordinary argument syntax at all -- it passes a value containing `\"` to the process unquoted, so the body splits at its first space, and it strips delimiter quotes written by hand. Its command therefore uses the `--%` stop-parsing token, after which Windows' own command-line rules apply and a double quote reached through `n` backslashes is written with `2n+1` of them. A binary response is described by content type and size rather than rendered, and an oversized body is displayed truncated with the whole content still available through copy and save. A window restored after a domain reload reports that the record is gone instead of rendering stale content. What is displayed is produced by a formatter kept apart from the windows, so the layout can be rearranged without changing any of it.
- Every HTTP exchange is now recorded in a bounded in-memory store: request line, headers, and body; response status, content type, body, and duration. The entry is opened when the request is dequeued and closed by the response itself, so a deferred response -- an artifact download, a replayed input sequence -- reports the duration through to its actual close rather than to the handler's return. Capture happens through the response's own output stream rather than by hooking `RestResponse`, so a custom handler that writes to the stream directly is recorded like any other. The store retains 200 exchanges, caps request bodies at 64 KB and response bodies at 256 KB, and records truncation instead of presenting a partial body as whole. Binary responses are measured rather than buffered, since screenshots and artifact downloads run to megabytes. Entries are held for the current Editor session and are lost on a domain reload.
- `RestRouter` gained tests. The framework request and response types are sealed with no public constructor, so nothing that accepted them could be exercised without a live server, and the suite had been shaped around that limitation since the beginning: handlers were tested through extracted pure helpers, and `RequestBodyReaderTests` documented that its request overloads were unreachable. Origin rejection, the `OPTIONS` answer, the not-found response, and the method-not-allowed response are now covered directly, as are the request overloads of `RequestBodyReader` including the single-read guarantee. The gates that depend on Editor state -- the Play Mode opt-in, the test-run rejection, and the disabled-category response -- still require that state to be arranged and remain uncovered.

## [0.4.0] - 2026-08-05

### Upgrade Notes

- Console-log `timestamp` values are now UTC ISO 8601 with a `Z` suffix. Clients that interpreted the previous offset-free values as local time must update their parser.
- The configured port now defaults to Automatic. Clients should read `.unionair/endpoint.txt` at connection time and reread it after a refused connection instead of assuming port `8765`.
- A valid `.unionair/settings.json` takes precedence over EditorPrefs and directly controls the exposed API surface; there is no separate local-approval step. The file is not ignored by `.unionair/.gitignore`, so a category enabled in a committed document is enabled for everyone who opens that project. Review it before sharing a project, and use **Disable All Sensitive APIs...** to clear every optional category, custom handlers, and Play Mode scene changes. When the file is absent the legacy EditorPrefs behavior is unchanged, and an invalid document fails closed rather than partially applying values.

### Added

- Added project-local API discovery through `.unionair/endpoint.txt`. A successful listener start atomically publishes the concrete Base URL; clean and observed stop paths remove only the current instance's value, startup clears crash debris, and `.unionair/.gitignore` keeps the runtime file and replacement temporaries out of Git. `GET /api/health` now reports `projectPath`, so clients can reject stale discovery that points at another project's Editor before discovering routes through `GET /api/help?detail=full`. Endpoint publication retries one transient atomic replacement and remains independent of best-effort ignore-file maintenance.
- Added strict versioned project configuration through `.unionair/settings.json`. Schema-backed EditorWindow controls update an in-memory working document and atomically save the complete UTF-8-without-BOM v1 file after every change; failed writes remain dirty and retry automatically. Domain reloads restore the working document through SessionState instead of rereading external edits. Built-in categories, custom handlers, and Play Mode scene changes use one authoritative set of UI controls, with a convenience action that disables every sensitive API while preserving server settings. The file remains visible to Git while runtime discovery files stay ignored.

- `POST /api/test-runs` now returns `500` without handing anything to the Unity Test Framework when the run record cannot be written, bringing the Test Runner in line with compilation, builds, and target switches. It previously dispatched the run first and wrote the record afterwards, so a failed write escaped as an unhandled exception with a real run already in flight and the armed profiling session orphaned. Attaching a profiling session, which writes to disk the same way, moved ahead of the dispatch for the same reason.
- The run id in `POST /api/test-runs` is now issued by UnionAir rather than taken from the Unity Test Framework. The framework returns its own id only once the run has been dispatched, which is exactly what made recording the run first impossible; its id is kept privately as the handle cancellation needs. The value is still an opaque GUID string and no response changed shape, so clients see no difference. `DELETE /api/test-runs/{id}` gains one `409` case, for a run the framework accepted without naming.
- A test-run activity left open with no live UnionAir record behind it is now released at initialization, with a Console warning, using the shared predicate the other three services adopted. Nothing else would have closed it: `SessionState` outlives every domain reload, the Test Framework poll that would otherwise recover it does nothing when the framework cannot be inspected, and the test-run gate is evaluated before the category check, so a stuck one refused nearly the whole API for the rest of the Editor session. The check is scoped to activities UnionAir owns, because a run started from the Test Runner window is adopted with no record at all and would otherwise be released while still running.
- The matching crash net now tests that the open activity is the one this service opened, not merely that some activity is open. An external run adopted after a UnionAir record was lost used to protect that record indefinitely, leaving a run nothing could finish.
- Test-run records are written through the shared store that retries once and reports failure, rather than one that throws. A failed write now schedules its own retry, so the last-chance flush before a domain reload still runs — without it, a run that finished but could not be stored came back as `aborted` after the reload.

- Added build target switching. `POST /api/build/target` switches the active target and returns `202` with the id to poll; `GET /api/build/target` and `GET /api/build/target/{id}` read the in-flight switch and the retained records. It is modelled as a lifecycle operation rather than a settings write, because it reimports every asset for the new platform, recompiles, and ends in a domain reload.
- The switch record survives that reload. Unity reports nothing across it, so UnionAir resolves the record on the far side by comparing the active target against the one requested — the only piece of evidence that outlives the reload.
- A missing platform module is reported as `409` with `code: "platform_module_not_installed"` and the targets that are installed, rather than as a generic switch failure. The fix is installing the module from the Unity Hub, which nothing about a failure message would suggest.
- Requesting the target that is already active returns `200` with `state: "unchanged"` and creates no record.
- `POST /api/build/target` returns `500` without starting anything when the switch record cannot be written. The record is the only thing that survives the domain reload the switch causes, so a switch begun without one could never report its outcome and would leave an activity nothing closes.
- A queued switch resolves to `failed` rather than staying queued forever when an unrelated domain reload discards its deferred start.
- The stale-activity check for a target switch tested the record's active state with the wrong polarity, so a terminal record could be accepted as the owner of a still-set flag. It now uses the shared, tested predicate.
- A queued or running target switch now makes `GET /api/editor/status` report `settled: false`. It previously reported `settled: true` alongside an `activeActivity` naming `buildTargetSwitch`.

- Added persistent build settings changes. `PATCH /api/build/settings` changes the scripting backend, API compatibility level, stripping level, IL2CPP compiler configuration, define symbols, and build flags for a named build target; `POST /api/build/scenes` replaces the build scene list. This is what makes the original motivation of the umbrella issue achievable — evaluating the effect of a settings change on compilation.
- Both endpoints state persistence explicitly per change. `project` persistence writes `ProjectSettings/ProjectSettings.asset` or `ProjectSettings/EditorBuildSettings.asset`, which appear as Git diffs and reach everyone working on the project; `user` persistence writes the per-user `Library/EditorUserBuildSettings.asset`. Presenting build flags and scripting settings as one kind of "setting" would have been the misleading part.
- Every value is validated before anything is written, so an invalid request changes nothing. Past that point a failed change is reported rather than rolled back — undoing earlier writes could fail too and leave a third state — so each change carries `applied`, `unchanged`, or `failed`, the status is `207` when any failed, and the response includes the resulting settings.
- Define symbols are validated as identifiers. Unity stores whatever it is given and fails later, at compile time, with an error that never mentions the setting.
- A change that triggers compilation reports `compilationExpected` and returns before the domain reload; the cycle is then observable through `GET /api/compile`.
- A field that is present but is not the type it should be returns `400` naming the field, rather than being dropped silently. `"development": "true"` previously applied nothing and reported no outcome for it, contradicting both the pre-write validation and the acknowledgement rule. A scene entry's `enabled` is checked the same way, where defaulting to `true` would have shipped a scene the caller asked to exclude.

- Added player builds to the Build category. `POST /api/builds` requests a build for the active target and returns `202` with the id to poll; `GET /api/builds`, `GET /api/builds/{id}`, and `DELETE /api/builds/{id}` read, enumerate, and reclaim them. A build is the only check that covers what compilation and EditMode tests do not: player-target assemblies, stripping, scripting backend, and platform-specific define symbols.
- A build occupies the Unity main thread, so **UnionAir answers no request while one runs** — measured at roughly 72 seconds for a Windows player, 22–34 seconds warm. The record is persisted and the `202` sent before the build starts, and the response says so, so a client can set its timeouts. Live progress and cancellation are not offered because neither is achievable in process.
- Build output and its `report.json` are written to `Builds/UnionAir/{id}/` under the project root, with a generated `.gitignore` containing `*`. `Library/` is deliberately not used: Unity regenerates it, which would either destroy a hundred-megabyte artifact silently or orphan it from its record. Retention trims oldest-first under a 3-directory, 2 GiB cap set independently of the profiling quota, and `GET /api/builds` reports total usage so disk consumption stays visible despite the git exclusion.
- The completed `BuildReport` is snapshotted into a durable DTO the moment `BuildPipeline.BuildPlayer` returns, because the report is a Unity object a domain reload discards. Records reach `queued`, `running`, `completed`, `failed`, or `aborted`, are written atomically, and survive a domain reload or an Editor crash.
- `POST /api/builds` rejects a request with `409` when any loaded scene has unsaved changes. `BuildPipeline.BuildPlayer` reads scenes from disk and does not prompt from script, so an unsaved scene would be silently excluded and the build would report success for content that does not match the Editor. Scenes are never saved implicitly.
- A replayed `requestId` returns `409` with the existing record rather than starting a second build. This matters more than for compilation: the connection drops for the whole build, so losing the `202` is a realistic outcome.
- `GET /api/editor/status` now reports `buildState` and `buildId`, and a queued or running build makes `settled` false.
- `POST /api/builds` returns `500` without starting anything when the build record cannot be written, and `DELETE /api/builds/{id}` returns `409` for a build that is still queued or running. Deleting the record a queued build waits on would leave nothing to release its activity, and the project would report itself busy for the rest of the Editor session.
- `POST /api/builds` rejects an option that is present but is not a JSON boolean, instead of silently falling back to the project default and producing a build the caller did not ask for.
- A build always reaches a terminal state and always releases its activity. The tail of the run was outside the exception handler, so anything thrown there escaped without committing the record — and unlike a compilation or a target switch, a build causes no domain reload for the recovery pass to run in.
- A queued build now resolves rather than waiting forever when its deferred start never runs — a domain reload between the `202` and the start discards the callback with the domain. `CompileService` already guarded its queued window this way; the build service had no watchdog.

- Added a single coordination point for mutually exclusive Editor activities — compilation, test runs, Play mode, asset updating, builds, and build target switches. UnionAir previously tracked these with two independent `SessionState` gates plus ad-hoc checks inside individual handlers, which made a third one untestable. Declared activities keep durable identity across domain reloads; Play mode and asset updating are observed from `EditorApplication` rather than mirrored.
- `GET /api/editor/status` now reports `activeActivity` with the activity name, its source, and the id that owns it. When several activities are running it reports the most exclusive one, so a client is told to wait for the build rather than for the compilation the build is running.
- A `409` caused by the Editor being busy now carries an `activeActivity` object. The shipped `activeTestRun` and `activeCompile` objects are unchanged and still present.
- Endpoints and categories now declare `blockedDuring` in `GET /api/help` — the complete list of activities during which the endpoint is rejected, including the ones enforced by `playModePolicy` and `testRunPolicy`, so a client reads one array instead of reconstructing it from three fields.
- Added `Documentation~/api/activities.md` describing the activity vocabulary, the rejection shape, and why enforcement of Play mode and test runs stays separate from the generic gate.
- An operation UnionAir starts now persists its record **before** opening its activity, and answers `500` without starting anything when that write fails. `InputReplayService` already worked this way; `CompileService` did not, and the new build endpoints had copied the wrong precedent. The write is retried once immediately, because a scanner or indexer briefly holding the destination is the common transient cause and clears by itself, while a full disk does not. Activity UnionAir merely adopts — a compilation an IDE triggered — cannot be refused and stays best-effort.
- The "is this activity debris" predicate is one shared, tested function rather than a copy per service. The three hand-written copies had already drifted: one omitted the terminal-record test and one inverted it, so a terminal record could be accepted as the owner of a still-set flag and the release would never happen.
- Activity coordination now recovers in both directions. The existing crash net finds a record claiming to run with no activity open; the new mirror check finds an activity open with no live record and releases it with a Console warning. Nothing else could close it — `SessionState` outlives every domain reload — so the Editor would otherwise report itself busy, and reject every endpoint declaring a conflict, for the rest of the session.

- Added a disabled-by-default **Build** category with read-only build configuration endpoints. `GET /api/build/settings` reports the active build target, the build scene list with the build index Unity actually assigns each entry, the scripting backend, API compatibility level, stripping level, and define symbols for a named build target, plus the development and debugging flags a build would use. `GET /api/build/targets` lists the build targets this Editor defines and whether each platform module is installed — which is a property of the Editor rather than the project, so nothing in the project directory reports it.
- Added the `executableOutput` endpoint risk for endpoints that produce runnable output rather than project files, and reported it as the risk of the Build category. No existing risk flag expressed it.
- Added `GET /api/compile/records` to enumerate retained terminal compilation summaries in deterministic newest-first order, with bounded offset/limit pagination and target, source, and state filters.

### Changed

- **Breaking:** The configured server port now defaults to Automatic (`0`) instead of fixed port `8765`. Automatic mode resolves a concrete loopback port before starting `HttpListener`, retains that assignment across domain reloads in the same Editor process when possible, and republishes `.unionair/endpoint.txt` when a conflict forces reassignment. Fixed ports `1..65535` remain available through the EditorWindow.
- **Breaking:** A valid `.unionair/settings.json` takes precedence over EditorPrefs for port, auto-start, categories, custom handlers, and Play Mode scene changes. Its API values directly control the exposed routes; there is no separate local-approval layer. An invalid document is never partially applied: auto-start is disabled, only Read remains enabled, custom handlers are disabled, and Play Mode scene changes are denied. Projects without the file retain the previous EditorPrefs/default behavior.

- **Breaking:** a script compilation started by a player build is now recorded with `source: "build"` and a `buildId` naming the build, instead of being adopted as an unrelated `external` cycle. `GET /api/compile/records?source=` accepts `build` alongside `unionAir` and `external`, and every compile record now carries a `buildId` field that is `null` for cycles no build owns. A client that treated `source` as a two-value field must be updated.
- `POST /api/editor/play` is now rejected with `409` while a script compilation is active. Entering Play mode during a compilation loses the request: Unity reloads the domain when the cycle finishes and discards the mode change with it. `POST /api/editor/stop`, `pause`, and `step` are deliberately unaffected — stopping a running game is exactly what a client needs to be able to do while something else is in flight.
- **Breaking:** `POST /api/compile` now returns `500` when the compile record cannot be written, instead of starting a compilation whose result it could not report. The response promises an id to poll and a compilation ends in a domain reload that discards the in-memory copy, so a record that never reached disk makes that promise unkeepable.
- The `409` returned by `POST /api/compile` while assets are updating now reads "This endpoint cannot be used while the Unity Editor is updating assets." and is produced by the shared activity gate rather than by a check inside the handler. The status code and the condition are unchanged.
- Scene Write, Asset Write, Editor Actions, and Play Mode endpoints now declare that they are blocked during a build and during a build target switch. Neither activity exists yet, so no current behavior changes; the declaration is what the later build endpoints rely on.

- Added a Design Philosophy section to `README.md` stating why UnionAir mediates Editor operations rather than file access, what qualifies an endpoint, and what is deliberately left to direct file operations. `AGENTS.md` and `CONTRIBUTING.md` now refer to it as the criterion for adding an endpoint and for judging feature requests.
- Corrected the Undo note in `README.md`: scene edits are registered with `Undo`, while asset writes do not uniformly support it and should not be assumed to be reversible. The previous wording claimed Undo support for all Edit mode writes. The behavior is unchanged; only the documentation was wrong.
- Documented the Test Runner filter contract in `Documentation~/api/testing.md`: the matching rule, case sensitivity, and suite behavior of each of the four filter fields, the OR-within-field and AND-across-fields combination, and the `!` exclusion prefix. A filter that matches no test is now documented as completing with `result: "passed"` and `progress.completed: 0`, because nothing ran and so nothing failed — a client that filters compares an expected count against `progress.completed`. `progress.total` is the size of the whole test tree for the mode and never narrows with the filters; the previous response example showed it equal to `completed`, which read as the selected count. `GET /api/tests` is now documented as listing leaf tests only, so suite names valid in `testNames` and `groupNames` do not appear there. The behavior is unchanged; only the documentation was wrong.

### Fixed

- Automatic port startup now contains probe-allocation exceptions, counts only distinct ports against its eight-candidate budget, skips port-specific listener rejections, and gives a retained port one delayed retry before reassignment. Fatal listener failures still stop immediately and produce one concise error instead of escaping through the Editor update callback.
- Project settings no longer make their EditorWindow controls read-only. Port mode and fixed-port values remain editable while the listener is running, save immediately, and apply to the listener on Restart; category, custom-handler, and Play Mode safety settings are likewise editable without manual Save or Reload actions. Duplicate local-approval controls were removed: the existing Built-in API and Custom Handlers checkboxes are authoritative. Documentation now states explicitly that enablement prevents accidental operations and limits API exposure, but does not defend against malicious Editor code or settings tampering.
- Project settings now commit a valid `settings.json` even when best-effort `.gitignore` maintenance fails, and report the ignore failure separately. Fixed-port editing waits for the completed field value, route enablement changes reuse the existing discovery snapshot instead of rescanning every loaded assembly, and custom category checkboxes remain subordinate to the Custom Handlers master switch rather than enabling it implicitly.

- A player build no longer leaves loaded scenes reported as externally changed. `BuildPipeline.BuildPlayer` raises `sceneClosed` for the loaded scene without a matching `sceneOpened`, which dropped the disk baseline and made every later refresh or compile request return `409` with `reason: "untracked"` until the scene was saved or reopened by hand. Baselines are now captured before the build and restored afterwards, but only for scenes whose file is byte-identical — a scene changed on disk during the build still trips the guard.
- Classified Unity 6 Bee Player compilation outputs under `Library/Bee/PlayerScriptAssemblies` as `player`, while preserving conservative handling of mixed and unrelated output directories.

## [0.3.0] - 2026-08-01

### Added

- `POST /api/editor/play` now accepts an optional `inputs` list that schedules frame-accurate input, replayed from the first Play mode frame. `frame` means the frame the game observes the input on — the frame where `wasPressedThisFrame` is true inside the game's `Update()` — not the frame an event was queued. Events sharing a frame are merged into a single state snapshot per virtual device, so chords reach the game as simultaneous presses. Without `inputs` the endpoint is unchanged; with it the response is `202` and carries the replay id. Requires the `com.unity.inputsystem` package.
- Added `GET /api/playmode/input/result` in the always-enabled Read category, reporting the current or most recently finished replay. Each event carries the frame it was actually observed on rather than the frame requested, so a client can prove the schedule held instead of assuming it. The endpoint doubles as the completion signal: it is how a caller knows when inspecting the game's state is meaningful. A failed event is recorded and the replay continues, so `failedCount` matters even when `state` is `completed`.
- The whole `inputs` list is validated before Play mode is entered, and a single invalid entry returns `400` naming its index without arming a replay or leaving Edit mode. Entering Play mode causes a domain reload and the response is sent before the replay runs, so a problem found later could not be reported at all. There is no cap on list length or frame range; `POST /api/editor/stop` ends a runaway replay.
- `POST /api/playmode/input/{perform,set,pointer}` now return `409` while a replay is active. They call `InputSystem.Update()` synchronously, which would flush the replay's queued events outside the player loop and destroy its frame timing.

- Added a Compile API in the always-enabled Read category. `GET /api/compile` returns the in-flight cycle as `current` and the most recently completed Editor cycle as `latest`, and `GET /api/compile/{id}` returns one of the 20 most recently retained records. Diagnostics are structured with `severity`, `code`, project-relative `file`, `line`, and `column` instead of requiring callers to parse Console text.
- Added `POST /api/compile` in the Asset Write category, which requests a compilation and returns `202` with the id to poll. The record is persisted and the response sent before any compilation work starts, because refreshing and compiling block the Editor and can end in a domain reload that drops the connection. An optional caller-supplied `requestId` makes a lost response recoverable, and `409` responses carry `activeCompile` or `existingCompile` so a caller knows to poll rather than retry.
- `GET /api/editor/status` now reports `compileState`, `compileId`, and `compileSource` for an in-flight compilation, mirroring the existing test-run fields.
- Script compilations started outside UnionAir, such as an IDE save followed by Unity's focus auto-refresh, are adopted and recorded with `source: "external"`.
- Compile records distinguish `succeeded` from `upToDate`, so a caller can tell "compiled with no errors" from "Unity reported zero compiled assemblies." Neither result promises whether a domain reload follows; removal-only cycles can report `upToDate` and still reload. Cancelled and interrupted cycles resolve to `aborted` rather than remaining active.

- Unity Console logs are now retained across assembly domain reloads. Every entry is mirrored to an append-only NDJSON file under `Library/UnionAir/Logs`, and the in-memory ring buffer is rehydrated from it after each reload.
- `GET /api/editor/logs` now returns a monotonic `sequence` per entry plus `sessionId`, `oldestSequence`, `latestSequence`, `truncated`, and `hasMore`, and accepts an exclusive `since` cursor so callers can fetch only new entries. The cursor is applied before the `type` and `search` filters, so `truncated` reports lost entries rather than filtered ones.
- `GET /api/editor/status` now reports `sessionId`, `lifecycleGeneration`, `settled`, and `hasCompileErrors`. `lifecycleGeneration` increments on every domain reload, letting a client whose connection dropped confirm that a reload completed rather than that the Editor crashed.

- Added `GET /api/editor/logs.ndjson`, which downloads the retained raw logs for the current Editor session including entries already evicted from the in-memory buffer. Across a size rotation it concatenates the same-session predecessor and active file in oldest-first order; at most these two files are retained.

- Added a disabled-by-default Profiling API for AI-oriented `ProfilerRecorder` discovery and sessions, versioned JSON statistics, frame-level NDJSON, optional Unity Profiler raw captures, and downloadable Memory Profiler snapshots.
- Profiling sessions can be attached atomically to UnionAir EditMode and PlayMode Test Runner runs and report discontinuous segments across assembly reloads.
- Profiling and memory artifacts are stored under `Library/UnionAir`, include project-relative paths, sizes, and SHA-256 hashes, and use bounded count and shared-size retention.

- Added an optional, disabled-by-default Test Runner API when Unity Test Framework is installed: leaf-test discovery, asynchronous EditMode/PlayMode execution, current/latest status, cancellation, and complete NUnit XML download.
- Test Runner results retain only the current run metadata and latest completed UnionAir result. Complete NUnit XML is atomically stored under `Library/UnionAir/TestRuns` and exposed through HTTP for callers that need durable history.
- `GET /api/editor/status` now reports active Unity Test Framework runs and whether they were started by UnionAir or an external tool.
- Endpoint metadata now declares whether each route is allowed during a test run. Active UnionAir and external runs block all endpoints except health, help, editor status/logs, run status/result/cancel, and CORS preflight.
- `POST /api/playmode/input/set` now supports one-shot Mouse scroll deltas through `<Mouse>/scroll`, `<Mouse>/scroll/x`, and `<Mouse>/scroll/y` bindings while preserving the virtual Mouse position and held buttons.

- Added EditMode tests under `Tests/Editor` covering compiler-message parsing, path normalization, compile result and target decisions, retained-record safety, log cursor arithmetic, rotation selection, and bounded multi-file streaming. The test assembly is not compiled in a consumer project unless the project adds `com.leonakasaka.unionair` to `testables` in its manifest.

### Changed

- Lowered the minimum supported Editor version from `6000.0` to `2022.3`. Unity 6000.0 LTS and later remains the primary target and the only version every change is verified against; 2022.3 LTS is now a supported version whose builds and core behavior are verified; 2023.x is best effort. Nothing in the package needed a Unity version guard — the entire codebase already compiled on 2022.3 apart from the Test Runner assembly.
- The Test Runner assembly now requires `com.unity.test-framework` **1.4.0 or later** rather than merely requiring the package to be present. UnionAir uses `TestRunnerApi.RegisterTestCallback`, `SaveResultToFile`, and `CancelTestRun`, all of which were added in 1.4.0. Unity 2022.3 and 2023.1 default to 1.1.33 and 1.3.9, where those APIs do not exist and the assembly previously failed to compile with three `CS0117` errors. The assembly is now skipped on versions below 1.4.0, so such projects compile cleanly; the `/api/tests` and `/api/test-runs` endpoints and the Test Runner category are simply absent. Add `"com.unity.test-framework": "1.4.6"` to a project's `Packages/manifest.json` to enable them. UnionAir does not declare the dependency itself, because the Test Runner category is disabled by default and the upgrade should stay the project's decision.

- **Breaking:** `timestamp` in `GET /api/editor/logs` entries is now UTC ISO 8601 with a `Z` suffix (`2026-05-16T04:12:00.1234567Z`) instead of an offset-free local time, matching the Test Runner and Profiling APIs. Clients that parsed the previous format as local time must be updated.

- Documented that category enablement is stored in `EditorPrefs`, which Unity scopes to the user and Editor version rather than to the project. Enabling a write category for one project enables it for every project opened with the same Editor. The behavior is unchanged; only `README.md` and `SECURITY.md` were previously silent about it.

### Security

- Closed a cross-site request forgery exposure in the unauthenticated localhost bridge. Requests carrying an `Origin` header are now rejected before routing, responses no longer opt into wildcard CORS, and non-empty request bodies require `Content-Type: application/json`. Origin-free local CLI and integration clients remain compatible, including empty POST requests without a content type.

### Fixed

- `POST /api/assets/reimport` now rejects loaded scenes with a structured `409 Conflict` before calling `AssetDatabase.ImportAsset()`, preventing that reimport from opening Unity's interactive Reload dialog and blocking subsequent UnionAir requests. Recursive folder imports report all loaded scene conflicts in Scene Manager order.
- `POST /api/editor/refresh` and `POST /api/compile` with `refresh: true` now detect external changes to loaded scene files from SHA-256 baselines retained across domain reloads. They refuse to call `AssetDatabase.Refresh()` and report the loaded scenes instead of allowing Unity's interactive Reload dialog to block the API.
- Loaded-scene disk baselines are now bootstrapped through the background-safe Editor update pump after scene restoration on a cold Editor start. Initialization no longer prunes persisted baselines from an incomplete early scene list, readiness and file-read retries are bounded, and a failed scene-open/save baseline records one actionable warning instead of silently leaving the scene untracked.
- `DELETE /api/assets/{guid}` now returns a structured `409 Conflict` before deleting a loaded scene or a folder containing loaded scenes, preventing the open scene from losing its backing asset.
- Request-body fields are now read correctly from pretty-printed JSON. The body reader skipped only spaces and tabs after a field's colon, so a value placed on the following line was reported as absent — every endpoint using `GetString`, `GetInt`, `GetFloat`, or `GetBool` was affected.
- Reading a JSON array is now anchored to its own field. A non-array value such as `"operations": null` previously made the reader scan forward and adopt an unrelated later array in the same body, silently processing the wrong elements.
- `POST /api/playmode/input/set` now reads `value` through the shared array reader, which no longer matches a `"value"` key nested inside another object or inside a string literal. A Vector2 `value` must now hold exactly two numbers; a longer array was previously accepted and silently truncated to its first two elements.
- Screen-coordinate endpoints now reject a `position` or `normalizedPosition` that is present but not an object, such as `"position": null`. The value previously read as an absent field, which mattered most for `POST /api/playmode/input/pointer` with mode `release`, where an absent coordinate means "use the current position" — a malformed coordinate silently released somewhere else instead of failing.

- Prevented mixed player or custom compilation cycles from being classified as Editor cycles and replacing `latest`, and re-evaluated retained records with the conservative target rules at startup.
- Prevented caller-supplied compile ids such as Windows device names from being accepted when their per-id record cannot be persisted safely.
- Made Console NDJSON downloads include the same-session rotated predecessor instead of making entries inaccessible as soon as the active file crossed the rotation threshold.
- Kept compile record retention going past a record that cannot be deleted, instead of abandoning the remaining trim and letting the retained set grow without bound.
- Backed off log rotation by size after a failed attempt. A rotation blocked by a concurrent NDJSON download previously left the file over the threshold, so every following Console message closed, retried, and reopened the log writer.
- Stopped rebuilding `latest` from retained records on every domain reload once a scan has already found no eligible completed Editor cycle. Skipping the rescan no longer keeps a stale non-Editor record as `latest`, the removal of that record is retried on every reload until it succeeds, and a scan interrupted by an I/O error is retried instead of being treated as proof that nothing eligible exists.
- Moved persistence of log rotation state onto the Editor update loop so that `GET /api/editor/logs.ndjson` no longer writes session state as a side effect of a read.
- Stopped a multi-file artifact transfer when a stream ends before its announced length, rather than appending the next file onto a partial record.
- Prevented domain reloads from orphaning an `HttpListener` by retaining listener ownership until bounded thread and queued-response cleanup completes.
- Added up to five bounded retries for transient address-in-use failures during automatic startup, without publishing intermediate failures to the Console or logs API.
- Added up to three delayed recovery attempts for unexpected listener thread exits, with cleanup completed before the bounded lifecycle trace is dumped and concise errors kept separate from the once-per-domain trace.
- Prevented failed profiling restoration from leaving undeletable active sessions or owned Profiler settings behind.
- Moved profiling artifact downloads off the Unity Editor update loop and cached finalized session statistics instead of reparsing NDJSON on every status request.
- Made active NDJSON downloads share-safe and length-bounded, handled background-transfer queue failures, and limited sessions to 64 metrics to bound memory use.
- Active object searches now select the Unity 6000.4 no-sort-parameter API while retaining the established overload on earlier Unity 6 releases, removing new obsolete warnings without raising the minimum supported Unity version.
- Test Runner request filters now use the shared top-level JSON parser, avoiding false matches for filter names embedded inside string values.
- Test-run progress metadata is now persisted at a bounded interval and at lifecycle boundaries instead of performing an atomic disk write for every test callback.
- Unity Test Framework active-run inspection now uses a cached delegate at a bounded polling interval, reports compatibility failures, and safely rejects new runs when concurrency cannot be verified.
- Test execution and discovery now share a correctly created, domain-scoped `TestRunnerApi` ScriptableObject instead of constructing an undisposed instance for each request.
- Test-run source identifiers, public run IDs, nullable JSON string formatting, and the EditorWindow's core category order now each use a single definition to prevent silent drift between API responses and UI rendering.
- Test-run gates now reconcile in both directions after a grace period when Unity Test Framework is positively observed as idle, preventing missed completion callbacks from leaving external or UnionAir runs locked until an Editor restart.
- Latest NUnit XML and metadata now use a recoverable transaction and SHA-256 integrity check, preventing a crash between file replacements from serving one run's XML under another run ID.
- `POST /api/playmode/input/perform` now resolves Keyboard bindings through the virtual device's actual `KeyControl`, preventing top-row numeric paths such as `<Keyboard>/1` from being parsed as numeric `Key` enum values and pressing unrelated keys.

## [0.2.0] - 2026-07-16

### Added

- `PATCH /api/assets/texture-importer/{guid}` — updates texture import settings (`textureType`, `spriteMode`, `pixelsPerUnit`) and calls `SaveAndReimport()`. Required for converting Texture2D assets to Sprite type before using them in object reference curves.
- `POST /api/assets/animation-clips` — creates an AnimationClip asset with optional `frameRate` and `wrapMode`.
- `GET /api/assets/animation-clips/{guid}` — returns clip metadata, all float curves, and all object reference curves (sprite-swap style).
- `POST /api/assets/animation-clips/{guid}/curves` — adds or replaces float curves (`curves`) and object reference curves (`objectReferenceCurves`) in a single call. For Sprite-mode textures, the Sprite sub-asset is loaded automatically via `AssetDatabase.LoadAssetAtPath<Sprite>` to avoid type mismatches with `m_Sprite`.
- `DELETE /api/assets/animation-clips/{guid}/curves` — removes curves by binding. Automatically selects `AnimationUtility.SetObjectReferenceCurve(clip, binding, null)` for PPtr bindings and `clip.SetCurve(..., null)` for float bindings.
- `POST /api/assets/animator-controllers` — creates an AnimatorController asset (includes a default Base Layer).
- `GET /api/assets/animator-controllers/{guid}` — returns the full controller structure: parameters, layers, states (with motion and transitions), and any-state transitions.
- `POST /api/assets/animator-controllers/{guid}/parameters` — adds or replaces a Float/Int/Bool/Trigger parameter with an optional default value.
- `DELETE /api/assets/animator-controllers/{guid}/parameters` — removes a parameter by name.
- `POST /api/assets/animator-controllers/{guid}/layers` — adds a layer with optional weight.
- `POST /api/assets/animator-controllers/{guid}/states` — adds a state with optional motion GUID, speed, and default-state flag.
- `PATCH /api/assets/animator-controllers/{guid}/states` — updates an existing state (rename, motion, speed, default).
- `DELETE /api/assets/animator-controllers/{guid}/states` — removes a state by name.
- `POST /api/assets/animator-controllers/{guid}/transitions` — adds a transition; supports `AnyState` as source and `Exit` as destination; accepts `hasExitTime`, `duration`, `offset`, and a `conditions` array with `If`/`IfNot`/`Greater`/`Less`/`Equals`/`NotEqual` modes.
- `PATCH /api/assets/animator-controllers/{guid}/transitions` — updates an existing transition identified by `from`/`to` names.
- `DELETE /api/assets/animator-controllers/{guid}/transitions` — removes a transition identified by `from`/`to` names.
- `POST /api/gameobjects/instantiate` — instantiates a prefab asset into a loaded scene while preserving the prefab connection, with optional `name`, `parent`, and `scenePath`.
- `GET /api/playmode/input/actions` — lists enabled Unity Input System actions in the running game, including action type and effective binding paths.
- `POST /api/playmode/input/perform` — performs a Button InputAction by name through a UnionAir virtual device during Play mode.
- `POST /api/playmode/input/set` — sets Axis, Vector2, and Stick InputAction values on supported virtual Gamepad controls. Values remain active until another set call or Play mode cleanup.
- `GET /api/playmode/ui/elements` — lists active Unity UI and TextMeshPro UI elements that can be targeted during Play mode.
- `POST /api/playmode/ui/click` — clicks a Unity UI `Button` or `IPointerClickHandler` target during Play mode.
- `POST /api/playmode/ui/text` — sets text on a Unity UI `InputField` or TextMeshPro `TMP_InputField` and optionally submits it during Play mode.
- `POST /api/playmode/ui/scroll` — scrolls a Unity UI `ScrollRect` by wheel delta or normalized position during Play mode.
- `POST /api/playmode/ui/value` — sets a Unity UI `Toggle`, `Slider`, `Dropdown`, or TextMeshPro `TMP_Dropdown` value during Play mode.
- `POST /api/playmode/input/pointer` — simulates a mouse click/press/release/move at a screen coordinate through the virtual mouse, spreading the phases across real player frames so the game's own raycast-based hit detection (`PhysicsRaycaster`, `Mouse.current` polling) reacts like it would to genuine input.
- `POST /api/playmode/screen/hittest` — read-only raycast at a screen coordinate (EventSystem raycasters + `Physics.Raycast` from `Camera.main`) reporting what a pointer click there would hit.
- `GET /api/help` — attribute-generated API manifest for LLMs, MCP bridges, and tools that cannot access the documentation directly
- ASP.NET-style attribute routing with `[UnionAirController]` and `[UnionAirEndpoint]` as the source of truth for routing, help, category state, and the EditorWindow endpoint list
- Custom handler discovery under `/api/custom/...`, managed separately in the UnionAir EditorWindow
- Category-level API enablement metadata and endpoint risk reporting for built-in and custom API discovery
- Multi-scene API support: `GET /api/scenes`, `POST /api/scenes/new`, `POST /api/scenes/open`, `POST /api/scenes/unload`, and `POST /api/scenes/active`.
- Custom controller authors can now reuse UnionAir's scene, object reference, GlobalObjectId, and asset reference resolution through the public `UnionAirReferenceResolver` helper.
- `Documentation~/custom-controllers.md` with setup, request parsing, reference resolution, Play Mode policy, and security guidance for custom API implementers.
- `POST /api/editor/menu-item` can now execute Unity Editor menu items through a disabled-by-default Editor Actions category.
- `GET /api/editor/selection`, `POST /api/editor/selection`, and `POST /api/editor/ping` expose Unity Editor selection and object highlighting operations.
- `POST /api/assets/open` opens project assets in the Unity Editor, and `POST /api/assets/reimport` reimports individual assets.
- `GET /api/assets/scriptableobjects`, `GET /api/assets/scriptableobjects/{guid}`, `POST /api/assets/scriptableobjects`, `PATCH /api/assets/scriptableobjects`, and `DELETE /api/assets/scriptableobjects/{guid}` — full CRUD for ScriptableObject assets using runtime reflection and `SerializedObject`, supporting any project-defined ScriptableObject subclass without package changes.
- `GET /api/editor/menu-items` lists discoverable Unity Editor menu paths for use with `POST /api/editor/menu-item`.
- `GET /api/editor/capture` and `GET /api/editor/capture/image` — capture the current view without specifying a camera. In Play mode, reads the composited GameView render texture via reflection, including Screen Space Overlay Canvas UI; falls back to `ScreenCapture.CaptureScreenshotAsTexture()` if reflection is unavailable. In Edit mode, renders the last active Scene View camera. Both endpoints accept optional `width`, `height`, `format`, and `quality` query parameters.

### Fixed

- ObjectRef body fields now distinguish a malformed scalar value from a missing field and return an actionable 400 error with the required object shape. `GET /api/help` also describes the required `target` shape for `POST /api/playmode/ui/click`.
- `GET /api/editor/logs` now matches the `type` filter case-insensitively and returns 400 for unknown values instead of silently returning every log type.
- `POST /api/assets/reimport` now accepts an existing project-relative `assetPath` even when Unity has not registered its main asset type or GUID yet, and returns the GUID assigned by the import.
- Restored compatibility with earlier Unity 6 releases by using the established `FindObjectsByType` overload and object-based asset-label lookup.
- `POST /api/playmode/ui/value` now reports `"clamped": true` when a Slider value was clamped to `[minValue, maxValue]`, instead of silently returning the adjusted value.
- `POST /api/playmode/ui/click` on a non-clickable child (e.g. a Button's `Text` label) now falls back to the nearest ancestor `IPointerClickHandler` instead of returning 422, matching how a real pointer click bubbles through the raycast.
- `POST /api/playmode/ui/text` and `/value` no longer report `success:true` when the reflective TMP `text`/`value` setter or requested submit events cannot be found; they return 500 instead. A missing `RefreshShownValue` now logs a warning instead of being silently skipped.
- Play Mode UI endpoints now recognize subclasses of `TMP_InputField` and `TMP_Dropdown`: TMP types are resolved once per domain load and matched with `IsInstanceOfType` instead of exact type-name comparison. `GET /api/playmode/ui/elements` also lists such subclasses, reports each element's actual component type in `type`, and no longer scans every scene component when looking for TMP elements (skipped entirely when TextMeshPro is not installed).
- Short type names such as `Slider`, `Toggle`, and `Button` no longer resolve to same-named non-matching classes (e.g. `UnityEngine.UIElements.Slider`). Type resolution for `componentPath` references, component add/remove, ScriptableObject creation, and object reference `assetType` now filters candidates by the required base type and keeps scanning instead of returning the first name match.
- `POST /api/playmode/input/perform` now handles Gamepad trigger Button bindings as trigger axis state instead of button bitfield flags, validates Vector2 values as finite numbers, and preserves held Button records when `tap` is called while the same action is pressed.
- `POST /api/playmode/input/perform` now sends full virtual device state for Button actions instead of delta events against bitfield controls, avoiding Keyboard `Key` delta-state failures.
- `POST /api/assets/animation-clips/{guid}/curves` — `objectReferenceCurves` keys now load `Sprite` sub-assets via `AssetDatabase.LoadAssetAtPath<Sprite>` instead of `LoadMainAssetAtPath`. The previous behavior returned a `Texture2D` for Sprite-mode PNGs, causing a type mismatch in `m_Sprite` that crashed Unity during animation preview.
- `DELETE /api/assets/animation-clips/{guid}/curves` — bindings that match existing object reference (PPtr) curves are now removed with `AnimationUtility.SetObjectReferenceCurve(clip, binding, null)` instead of `clip.SetCurve(..., null)`, which silently did nothing for PPtr bindings.
- Hardened JSON string escaping in API responses to correctly encode control characters and prevent malformed JSON output from string fields.
- Normalized non-finite float values (`NaN`, `Infinity`, `-Infinity`) to `null` in JSON responses to keep numeric fields JSON-compliant.
- Replaced non-ASCII dash and arrow characters in API-visible string literals with ASCII equivalents; on machines whose system code page is not UTF-8 (e.g. Japanese Windows), the compiler could misread the BOM-less source files and emit mojibake in `/api/help` summaries and error messages.

### Changed

- Documented that bodyless POST requests are supported and must be framed with `Content-Length: 0` so Windows `HttpListener` does not reject them with 411 before routing.
- `POST /api/editor/refresh` guidance now requires both asset updating and script compilation to become idle, including reconnecting after a domain reload, before clients make dependent calls.
- `POST /api/playmode/input/perform` and `/set` now accept `Map/Action` identifiers. Bare action names remain supported when unique and return 409 with candidate identifiers when ambiguous.
- `POST /api/assets/animation-clips/{guid}/curves` — `curves` is no longer declared as a required body field; `objectReferenceCurves` alone is now a valid payload.
- `POST /api/playmode/input/perform` is now Button-only with `mode` (`tap`, `press`, `release`) instead of `value`; Axis, Vector2, and Stick values use `POST /api/playmode/input/set`.
- Clarified that `GET /api/editor/capture` and `GET /api/editor/capture/image` resize the captured GameView frame in Play mode instead of re-rendering the GameView at the requested `width` and `height`.
- `PATCH /api/gameobjects/components` can now set and clear serialized object references, including scene GameObjects, Components, and assets such as TextAsset.
- Existing scene, search, GameObject, component, and prefab APIs now accept optional `scenePath` targeting for loaded scenes.
- Scene GameObject and Component APIs now expose `globalObjectId` values and accept them as stable target identifiers.
- GameObject, component, camera, and prefab write APIs now use typed object references (`target`, `parent`, `source`) instead of parallel path and ID fields.
- Serialized component object reference payloads now use typed `hierarchyPath`, `componentPath`, and `globalObjectId` references.
- Endpoint metadata now declares Play Mode safety policy, and write APIs are centrally blocked or require both Editor-side Play Mode scene-change permission and `allowWhilePlaying=true` while the Editor is in Play Mode.
- Play Mode scene-object writes now skip scene dirty marking and Undo registration because they are transient runtime changes.
- Endpoint risk metadata now includes `requestDependent` for APIs whose side effects depend on request parameters.
- Endpoint risk metadata now includes `editorState`, and endpoints can override their category risk when they have a narrower side-effect profile.
- Built-in and custom API endpoint lists in the EditorWindow can now expand and collapse by category.
- `SerializedPropertySerializer` utility extracted from `ComponentWriteHandler` and extended with a read direction for reuse across asset and component property serialization.
- `AssetUtils.EnsureDirectory` extracted from `MaterialWriteHandler` as a shared asset file system utility.

## [0.1.0] - 2026-05-17

### Added

#### Read API

- `GET /api/health` — health check
- `GET /api/scene` — current scene info (name, path, isDirty, rootCount)
- `GET /api/scene/hierarchy` — full GameObject tree with transform data (supports `?depth`, `?compact`, `?limit`, `?path`)
- `GET /api/scene/stats` — scene statistics (object counts, component/tag/layer breakdown)
- `GET /api/gameobjects` — GameObject details with serialized component properties
- `GET /api/editor/status` — Editor state (isPlaying, isPaused, isCompiling, isUpdating)
- `GET /api/editor/logs` — console log capture with type/search/limit filters
- `GET /api/cameras` — camera list with depth, FOV, and path
- `GET /api/cameras/capture` — render camera to base64 image (JPEG/PNG)
- `GET /api/cameras/capture/image` — render camera as binary image stream
- `GET /api/assets` — asset list with path/type/search filters
- `GET /api/assets/{guid}` — asset detail with dependencies and labels
- `GET /api/assets/dependents` — reverse dependency lookup
- `GET /api/search/gameobjects` — multi-criteria GameObject search
- `GET /api/search/asset-refs` — find scene references to an asset

#### Scene Write API (disabled by default)

- `POST /api/gameobjects` — create a new empty GameObject
- `POST /api/gameobjects/primitive` — create a primitive GameObject (Cube, Sphere, Capsule, Cylinder, Plane, Quad)
- `DELETE /api/gameobjects` — delete a GameObject
- `PATCH /api/gameobjects` — update GameObject properties (name, isActive, tag, layer, transform)
- `POST /api/gameobjects/duplicate` — duplicate a GameObject
- `POST /api/gameobjects/reparent` — move a GameObject to a new parent
- `POST /api/gameobjects/batch` — bulk create/update/delete in a single Undo group (HTTP 207)
- `POST /api/gameobjects/components` — add a component to a GameObject
- `DELETE /api/gameobjects/components` — remove a component from a GameObject
- `PATCH /api/gameobjects/components` — update serialized component properties
- `POST /api/scene/save` — save the current scene to disk

#### Asset Write API (disabled by default, separate toggle)

- `POST /api/editor/refresh` — trigger `AssetDatabase.Refresh()`
- `POST /api/assets/prefabs` — create a prefab from a scene GameObject
- `POST /api/assets/prefabs/apply` — apply instance overrides to the prefab asset
- `POST /api/assets/prefabs/revert` — revert a prefab instance to match the asset
- `POST /api/assets/materials` — create a new material
- `PATCH /api/assets/materials` — update material properties (Color, Float, Vector, Texture)
- `DELETE /api/assets/{guid}` — delete an asset and its `.meta` file
- `POST /api/assets/move` — move/rename an asset preserving GUID and references

#### Play Mode API (disabled by default, separate toggle)

- `POST /api/editor/play` — enter play mode
- `POST /api/editor/stop` — exit play mode
- `POST /api/editor/pause` — set or toggle pause state
- `POST /api/editor/step` — advance one frame (requires pause)

#### Infrastructure

- HTTP server via `HttpListener` (no external dependencies), default port 8765
- CORS headers (`Access-Control-Allow-Origin: *`) for cross-origin access
- Per-phase permission gating (Write / Asset Write / Play Mode toggles)
- EditorWindow UI for server control, port configuration, and request log
- Auto-start on Editor load via `[InitializeOnLoad]`
- Graceful shutdown on domain reload; auto-restart after domain reload and play mode exit
- Console log capture with 1000-entry ring buffer (`LogStore`)

<!-- 0.1.0 and 0.2.0 predate this repository being published and were never tagged,
     so they have no release page to link to. -->
[Unreleased]: https://github.com/LeonAkasaka/UnionAir/compare/v0.5.1...HEAD
[0.5.1]: https://github.com/LeonAkasaka/UnionAir/releases/tag/v0.5.1
[0.5.0]: https://github.com/LeonAkasaka/UnionAir/releases/tag/v0.5.0
[0.4.0]: https://github.com/LeonAkasaka/UnionAir/releases/tag/v0.4.0
[0.3.0]: https://github.com/LeonAkasaka/UnionAir/releases/tag/v0.3.0
