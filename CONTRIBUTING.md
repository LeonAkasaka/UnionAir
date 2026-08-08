# Contributing to UnionAir

**English** | [日本語](CONTRIBUTING.ja.md)

Thank you for your interest in contributing!

## Project Status

UnionAir is an **experimental, pre-beta** project. APIs, endpoints, and behavior may change or be removed at any time without backward compatibility. Expect breaking changes between releases.

## Issues and Pull Requests

UnionAir is currently in an early design phase, and its architecture and API specification may change substantially.

- Bug reports, feature requests, and questions are welcome as [GitHub Issues](https://github.com/LeonAkasaka/UnionAir/issues).
- Please do not open a pull request before discussing the change in an Issue.
- Pull requests are currently accepted only for changes agreed on with the maintainer or for Issues explicitly marked as open for contribution.
- Unsolicited pull requests may be closed without detailed review because they can conflict with planned changes that are not yet documented.

Feature requests are judged against [Design Philosophy](README.md#design-philosophy). A proposal whose job direct file editing already does correctly is out of scope, and so is one that exists only to wrap a Unity Editor API — however convenient an endpoint for it would be.

## Development Conventions

All development conventions live in [AGENTS.md](AGENTS.md) and apply to human and AI contributors alike. In short:

- All code comments, documentation, and commit messages are **English only**. Japanese documents exist only as `.ja.md` translations of English originals.
- When an API endpoint is added, changed, or removed, update the matching `Documentation~/api/<category>.md` page, the `[UnionAirEndpoint]` metadata, and `CHANGELOG.md` **in the same commit**.
- When an English document changes, update its `.ja.md` translation in the same commit.
- No external NuGet dependencies — the HTTP server is intentionally `HttpListener`-based.

## Tests

EditMode tests for the Unity-independent helpers live in `Tests/Editor`. They are **not** compiled in a consumer project: Unity only builds a package's test assemblies when the consuming project opts in. To run them, add the package to `testables` in your test project's `Packages/manifest.json`:

```json
{
  "testables": ["com.leonakasaka.unionair"]
}
```

Then run them from **Window > General > Test Runner** (EditMode), or through UnionAir itself with `POST /api/test-runs`.

Coverage is deliberately limited to logic that can be exercised without driving the Editor — compiler-message parsing, path normalization, and log cursor arithmetic. Everything that depends on real compilation, domain reloads, or the HTTP server still has to be verified by hand.

## Verifying a change across the version range

There is **no CI**. The declared floor is only real if someone compiles against it, so verify both ends by hand before opening a pull request.

`package.json`'s `unity` field is metadata: Unity will happily build a package that calls APIs newer than the declared minimum. Reading the field proves nothing.

1. **Compile at both ends.** Add the package as a local dependency to a project on 2022.3 and to one on the newest Unity 6 you have, and check the Console for `error CS` **and** `warning CS` — a deprecation warning at the top of the range is how the next breaking change announces itself.
2. **Check what the floor drops.** On 2022.3 and 2023.1 the Test Runner assembly is skipped, because `com.unity.test-framework` defaults to a version below 1.4.0 there. `GET /api/help` must still answer, without the `/api/tests` and `/api/test-runs` routes and without an error. Add `"com.unity.test-framework": "1.4.6"` to the project and confirm they come back.
3. **Exercise the endpoint you changed** against a live Editor. Most of this package is only reachable that way — see [Known Constraints](#known-constraints).
4. **Run the tests.** See [above](#tests).

## Releasing

`main` is the integration branch and runs ahead of the latest tag. The tag is what identifies a release, so **never move a tag once it is pushed** — consumers resolve it into their `packages-lock.json`, and moving it changes what that record means without changing the record.

The pinned tag in the installation instructions is the version people actually install, and it is the thing that rots quietly, so it is on this list rather than left to memory. The same applies to every other value that names a version — in the documentation, in the tag, and on the Release page.

1. Verify `main` across the version range, as [above](#verifying-a-change-across-the-version-range). A release is a promise that the declared floor works.
2. `package.json` — set `version`.
3. `CHANGELOG.md` — move `[Unreleased]` into a new `[x.y.z] - YYYY-MM-DD` heading, and update the link definitions at the bottom of the file.
4. **Update every version-pinned value in the documentation.** Ten occurrences across six files:
   - `README.md` and `README.ja.md` — the Package Manager Git URL and the `manifest.json` example. Two each.
   - `Documentation~/index.md` and `Documentation~/index.ja.md` — the same two in the Getting Started guide. Two each.
   - `Documentation~/api/general.md` and `general.ja.md` — the `version` field in the `GET /api/help` response sample. One each.

   `package.json`'s `repository.url` is **not** one of them: it identifies the repository, not a version, and stays unpinned.
5. Commit, then `git tag -a vX.Y.Z -m "UnionAir X.Y.Z"`. **The tag message is one identifying line — the product name and the version, without the `v`.** A tag cannot be corrected once pushed, so this is the one convention here with no second chance.
6. `git push origin main vX.Y.Z`.
7. **Create the GitHub Release from the pushed tag, titled `vX.Y.Z`.** Select the existing tag rather than letting the Release page create one, which would tag a second time from whatever `main` points at. Draft it first if the notes need review; a draft touches no tag and does not move Latest. Mark it as the latest release when publishing.

A UPM Git URL takes a fixed ref only — there is no version range syntax — so step 4 is what decides which version a reader ends up with. Leaving one occurrence behind silently hands that reader the previous release. The `/api/help` sample belongs to the same step for the same reason: `HelpHandler` fills that field from `package.json`, so a sample left behind describes a response the server no longer sends.

The tag message identifies the release rather than describing it. What changed belongs in `CHANGELOG.md` and in the Release notes, both of which can be corrected; a tag message is the one artifact in a release that cannot, which makes it the worst place to put prose worth reading. The same practice is common in widely used projects: a single product-and-version line, with the release notes kept out of the tag. Annotation still earns its place without a substantial message: only an annotated tag records a tagger and date, only an annotated tag can be signed, and `git describe` considers annotated tags by default.

`v0.5.0` and `v0.5.1` predate this rule and carry `Release v0.5.0` and `Release v0.5.1`; they stay as they are, since moving a tag to reword it is exactly what the warning above forbids. `v0.3.0` and `v0.4.0` already match it.

## Known Constraints

- There is **no CI**, and behavior involving compilation, domain reloads, Play mode, or the HTTP server has no automated coverage. Verify those manually against a Unity 6000.0+ project (see the [Getting Started guide](Documentation~/index.md)).
- Request-body JSON parsing uses a lightweight custom reader (`Editor/Utils/RequestBodyReader.cs`) with known edge cases for deeply nested bodies.
- JSON response serialization is hand-written per endpoint; a shared serializer is a welcome future refactor.

## Security Issues

Please report security issues per [SECURITY.md](SECURITY.md) instead of opening a public issue.
