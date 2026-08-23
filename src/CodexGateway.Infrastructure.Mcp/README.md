# CodexGateway.Infrastructure.Mcp

This project provides gateway-scoped MCP sessions for Codex run containers.

## Main flow

1. `Sessions/GatewayMcpSessionManager.cs` creates a short-lived session for the MCP servers granted to one run.
2. The runner receives only the gateway session URL and bearer token, not upstream credentials.
3. A request handler under `Transport/` validates the session and forwards JSON-RPC to the selected HTTP or local stdio upstream.
4. The session manager and cleanup service revoke expired or completed sessions.

## Folders

- `Sessions/` — session creation, authorization, lifetime, and cleanup.
- `Transport/` — request handling and HTTP/stdio upstream adapters; request data is under `Transport/Models/`.
- `Configuration/Models/` — session and runner-address settings.
- `Errors/` — transport-specific failures.
- `Composition/` — this module's installer.
