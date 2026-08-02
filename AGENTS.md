# AGENTS.md

Guidelines for AI agents (and contributors) working in this repository.

## Project Overview

`com.leonakasaka.unionair` is a Unity Editor-only UPM package that exposes Unity Editor state as a REST API.  
All implementation lives under `Editor/` and is excluded from runtime builds via `includePlatforms: ["Editor"]`.

## Documentation Layout

| File | Purpose |
|------|---------|
| `README.md` | Package overview, setup, security notes, and quick-start |
| `Documentation~/index.md` | Detailed setup, EditorWindow guide, and lifecycle |
| `Documentation~/api-reference.md` | API reference index: conventions, categories, and links to per-category pages |
| `Documentation~/api/<category>.md` | Per-category endpoint reference (requests, responses, and examples): `general`, `editor`, `scenes`, `gameobjects`, `assets`, `animation`, `playmode` |
| `CHANGELOG.md` | Version history in Keep a Changelog format |
| `CONTRIBUTING.md` / `SECURITY.md` | Contributor guide and security policy |

English documents are the canonical source. Japanese translations live next to each English document with a `.ja.md` suffix (e.g., `README.ja.md`, `Documentation~/api/editor.ja.md`). `CHANGELOG.md` and `AGENTS.md` are not translated.

## Language Policy

**All documentation, source code comments, and commit messages must be in English.**

- Exception: Markdown table cell content may use English to maintain international clarity.
- Source files: C# code comments, XML docs, and inline comments must be English.
- Documentation: All `.md` files must be English, except `.ja.md` files, which are Japanese translations of their English counterparts. The English document is always canonical.
- Commit messages: Use English. Include a co-authored-by trailer for AI contributions.

## Documentation Update Rules

**Whenever an API endpoint is added, changed, or removed, update the following in the same commit:**

1. **`Documentation~/api/<category>.md`** — add or update the relevant endpoint section in the matching category page, and keep the endpoint list in `Documentation~/api-reference.md` (the index) in sync
2. **`[UnionAirEndpoint]` on the controller method** — keep routing, `GET /api/help`, and the EditorWindow endpoint list in sync
3. **`CHANGELOG.md`** — record the change under `[Unreleased]`

Update `README.md` only for overview-level changes (e.g., a new capability being introduced).

**Translation sync:** whenever an English document changes, update its `.ja.md` counterpart in the same commit. If a translation is intentionally deferred, note it in the commit message.

## Coding Conventions

- Before adding an endpoint, check it against [Design Philosophy](README.md#design-philosophy). Route work through the Editor when the alternative is the client reimplementing Unity's rules, or when the Editor holds state no file exposes — and only when the route can offer a contract worth depending on. A wrapper added because a Unity method exists, rather than because a client needs it, does not qualify. If direct file editing would already be correct, leave it to the client.
- **Namespace**: `LeonAkasaka.UnionAir.Editor`
- **Assembly**: `Editor/com.leonakasaka.unionair.editor.asmdef` (Editor-only, no external references)
- Add new built-in endpoints as controller methods in `Editor/Controllers/`; keep reusable implementation helpers in `Editor/Handlers/` or `Editor/Utils/`
- Declare built-in API routes with `[UnionAirController]` and `[UnionAirEndpoint]`; public paths must be explicit attribute strings
- Set `[UnionAirEndpoint]` metadata deliberately: `Category` must reference built-in category constants or custom `[UnionAirCategory]` metadata that controls enablement and risk reporting
- Controllers in UnionAir's own assembly are built-in; controllers in other assemblies are custom and are exposed under `/api/custom/...`
- Do not add external NuGet dependencies — the HTTP server is intentionally `HttpListener`-based with no third-party runtime dependencies

## Versioning

- The version is managed via the `version` field in `package.json`
- At release time, rename the `[Unreleased]` section in `CHANGELOG.md` to the version number with a date
- Installation instructions pin a tag (`...UnionAir.git#vX.Y.Z`) in four files — both READMEs and both `Documentation~/index` pages. Releasing means updating all eight occurrences; `package.json`'s `repository.url` stays unpinned. The full procedure is in [CONTRIBUTING.md](CONTRIBUTING.md#releasing), which is the source of truth.
- Never move a tag once it is pushed

## Branching and Commits

- `main` is the stable branch
- Write commit messages in English, concisely describing the change
