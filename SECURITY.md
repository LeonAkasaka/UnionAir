# Security Policy

**English** | [日本語](SECURITY.ja.md)

## Security Model

UnionAir runs an HTTP server inside the Unity Editor. Understand this model before enabling any write category:

- The server binds to **`localhost` only** and is not reachable from other machines.
- There is **no authentication.** Any process on the same machine can call every enabled endpoint.
- Responses send `Access-Control-Allow-Origin: *`, so **any web page open in a browser on the same machine** can also call the API and read responses.
- Only the **Read** category is enabled by default. Scene Write, Asset Write, Play Mode, Editor Actions, and Test Runner are opt-in. Enabling them exposes state-changing operations — including arbitrary project test code, Unity Editor menu execution, and asset deletion — to any local client or browser origin. Enable them only when every local client is trusted.
- Test code can hang, change scenes or assets, enter Play mode, access the file system or network, and execute any other code permitted to the Unity Editor process. The Test Runner API does not sandbox tests or impose a timeout.

This package is **experimental**; the security posture (including the wildcard CORS policy) may change in future releases.

## Supported Versions

Only the latest release is supported. No fixes are backported.

## Reporting a Vulnerability

Please report vulnerabilities privately via [GitHub Security Advisories](https://github.com/LeonAkasaka/UnionAir/security/advisories/new) rather than opening a public issue. Include reproduction steps and the Unity/package versions involved.
