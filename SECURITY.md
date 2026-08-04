# Security Policy

**English** | [日本語](SECURITY.ja.md)

## Security Model

UnionAir runs an HTTP server inside the Unity Editor. Understand this model before enabling any write category:

- The server binds to **`localhost` only** and is not reachable from other machines.
- There is **no authentication.** Any process on the same machine can call every enabled endpoint.
- Requests carrying an `Origin` header are rejected before routing, and responses do not include CORS opt-in headers. Browser `fetch` and XMLHttpRequest clients are unsupported by default; non-browser local clients must omit `Origin`.
- Requests with a non-empty body must use `Content-Type: application/json`; empty POST requests do not require a content type.
- Only the **Read** category is enabled by default. Scene Write, Asset Write, Play Mode, Editor Actions, Test Runner, Profiling, and Build are opt-in. Enabling them exposes state-changing operations and diagnostic artifacts — including arbitrary project test code, Unity Editor menu execution, asset deletion, downloadable heap snapshots, build configuration such as scripting define symbols, build settings changes written to shared `ProjectSettings/` files, and player builds that write runnable programs into the project directory — to any local process. Enable them only when every local client is trusted.
- The Built-in API category checkboxes, Custom Handlers enablement, and Play Mode scene-change setting directly control the routes UnionAir exposes. They are accidental-operation guards and exposure-scope controls only. They are not authentication, authorization against hostile code, a sandbox, or a defense against settings tampering.
- `.unionair/settings.json` is ordinary project configuration: it is neither signed nor tamper-resistant, and it can be committed and shared through Git. Invalid project settings fail closed by disabling auto-start and exposing only Read, but this protects against malformed configuration rather than a malicious writer.
- When `.unionair/settings.json` is absent, category enablement retains the legacy `EditorPrefs` behavior and is shared by projects opened by the same user and Editor version. The first schema-backed UI change creates the project file; later changes save immediately. External edits are not reread until the Editor process restarts.
- Any process that can modify the project can edit the settings file or add C# Editor code. Code running inside the Unity Editor can change UnionAir's settings and can perform privileged Unity, filesystem, and network operations without using UnionAir at all. API toggles therefore cannot contain malicious project code or a local agent with code-execution capability. Use a separate OS account, restrictive filesystem permissions, a VM, or another isolated environment when untrusted code or clients require a real boundary.
- **Disable All Sensitive APIs...** is a convenience reset: it clears optional categories, custom handlers, and Play Mode scene changes while preserving server settings. It does not neutralize code already running in the Editor process.
- Test code can hang, change scenes or assets, enter Play mode, access the file system or network, and execute any other code permitted to the Unity Editor process. The Test Runner API does not sandbox tests or impose a timeout.
- Memory Profiler snapshots capture the Editor's managed heap. A snapshot of a project can therefore contain any string the Editor held in memory, and is served over the same unauthenticated local port.
- `.unionair/endpoint.txt` is discovery metadata, not authentication or proof of liveness. A client must call `GET /api/health` and compare its `projectPath` with the directory containing the discovery file; a hard Editor crash can leave stale content that now points at another project's Editor.

This package is **experimental**; the security posture may change in future releases.

## Supported Versions

Only the latest release is supported. No fixes are backported.

## Reporting a Vulnerability

Please report vulnerabilities privately via [GitHub Security Advisories](https://github.com/LeonAkasaka/UnionAir/security/advisories/new) rather than opening a public issue. Include reproduction steps and the Unity/package versions involved.
