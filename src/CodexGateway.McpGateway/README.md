# CodexGateway.McpGateway

This project provides gateway-scoped MCP sessions for Codex run containers.

## Main flow

1. `Sessions/GatewayMcpSessionManager.cs` creates a short-lived session for the MCP servers granted to one run.
2. The runner receives only the gateway session URL and bearer token, not upstream credentials.
3. The session manager selects the HTTP or STDIO transport through an explicit factory contract.
4. The selected transport project forwards JSON-RPC to its upstream.
5. An embedded binary resource with an `artifact://` URI is decoded into the run's
   temporary `./.gateway/mcp-files` directory and replaced by a small textual result
   containing its local path, media type, size, and checksum.
6. The session manager and cleanup service revoke expired or completed sessions.

## Folders

- `Sessions/` — session creation, authorization, lifetime, and cleanup.
- `Files/` — bounded, run-local materialization of explicitly marked MCP resources.
- `Transport/` — runner-facing request handling plus the contracts implemented by the HTTP and STDIO transport projects; request data is under `Transport/Models/`.
- `Configuration/Models/` — session and runner-address settings.
- `Errors/` — transport-specific failures.
- `Composition/` — registration for session management and cleanup.

Transport implementations live in `CodexGateway.McpGateway.Http` and
`CodexGateway.McpGateway.Stdio`. The core gateway does not contain either implementation.
