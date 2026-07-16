# Security Policy

**English** | [日本語](SECURITY.ja.md)

## Security Model

UnionAir runs an HTTP server inside the Unity Editor. Understand this model before enabling any write category:

- The server binds to **`localhost` only** and is not reachable from other machines.
- There is **no authentication.** Any process on the same machine can call every enabled endpoint.
- Responses send `Access-Control-Allow-Origin: *`, so **any web page open in a browser on the same machine** can also call the API and read responses.
- Only the **Read** category is enabled by default. Scene Write, Asset Write, Play Mode, and Editor Actions are opt-in. Enabling them exposes state-changing operations — including Unity Editor menu execution and asset deletion — to any local client or browser origin. Enable them only when every local client is trusted.

This package is **experimental**; the security posture (including the wildcard CORS policy) may change in future releases.

## Supported Versions

Only the latest release is supported. No fixes are backported.

## Reporting a Vulnerability

Please report vulnerabilities privately via [GitHub Security Advisories](https://github.com/LeonAkasaka/UnionAir/security/advisories/new) rather than opening a public issue. Include reproduction steps and the Unity/package versions involved.
