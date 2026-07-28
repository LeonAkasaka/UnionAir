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

## Known Constraints

- There is **no CI**, and behavior involving compilation, domain reloads, Play mode, or the HTTP server has no automated coverage. Verify those manually against a Unity 6000.0+ project (see the [Getting Started guide](Documentation~/index.md)).
- Request-body JSON parsing uses a lightweight custom reader (`Editor/Utils/RequestBodyReader.cs`) with known edge cases for deeply nested bodies.
- JSON response serialization is hand-written per endpoint; a shared serializer is a welcome future refactor.

## Security Issues

Please report security issues per [SECURITY.md](SECURITY.md) instead of opening a public issue.
