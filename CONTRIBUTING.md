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

## Known Constraints

- There is currently **no automated test suite or CI**. Verify changes manually against a Unity 6000.0+ project (see the [Getting Started guide](Documentation~/index.md)).
- Request-body JSON parsing uses a lightweight custom reader (`Editor/Utils/RequestBodyReader.cs`) with known edge cases for deeply nested bodies.
- JSON response serialization is hand-written per endpoint; a shared serializer is a welcome future refactor.

## Security Issues

Please report security issues per [SECURITY.md](SECURITY.md) instead of opening a public issue.
