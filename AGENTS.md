# AGENTS.md

Guidelines for AI agents (and contributors) working in this repository.

## Project Overview

`com.leonakasaka.unionair` is a Unity Editor-only UPM package that exposes Unity Editor state as a REST API.  
All implementation lives under `Editor/` and is excluded from runtime builds via `includePlatforms: ["Editor"]`.

## Documentation Layout

| File | Purpose |
|------|---------|
| `README.md` | Package overview, setup, and quick-start |
| `Documentation~/index.md` | Detailed setup, EditorWindow guide, and lifecycle |
| `Documentation~/api-reference.md` | Full endpoint reference (requests, responses, and examples) |
| `CHANGELOG.md` | Version history in Keep a Changelog format |

## Language Policy

**All documentation, source code comments, and commit messages must be in English.**

- Exception: Markdown table cell content may use English to maintain international clarity.
- Source files: C# code comments, XML docs, and inline comments must be English.
- Documentation: All `.md` files must be English.
- Commit messages: Use English. Include a co-authored-by trailer for AI contributions.

## Documentation Update Rules

**Whenever an API endpoint is added, changed, or removed, update the following in the same commit:**

1. **`Documentation~/api-reference.md`** — add or update the relevant endpoint section
2. **`CHANGELOG.md`** — record the change under `[Unreleased]`

Update `README.md` only for overview-level changes (e.g., a new capability being introduced).

## Coding Conventions

- **Namespace**: `LeonAkasaka.UnionAir.Editor`
- **Assembly**: `Editor/com.leonakasaka.unionair.editor.asmdef` (Editor-only, no external references)
- Add new endpoints as classes in `Editor/Handlers/` implementing `IRequestHandler`, then register them in `RestHttpServer`
- Do not add external NuGet dependencies — the HTTP server is intentionally `HttpListener`-based with no third-party runtime dependencies

## Versioning

- The version is managed via the `version` field in `package.json`
- At release time, rename the `[Unreleased]` section in `CHANGELOG.md` to the version number with a date

## Branching and Commits

- `main` is the stable branch
- Develop new features on a feature branch and merge via PR
- Write commit messages in English, concisely describing the change
- Include the co-authored-by trailer: `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`