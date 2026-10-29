# CodexGateway.McpGateway

This project provides gateway-scoped MCP sessions for Codex run containers.

## Main flow

1. `Sessions/GatewayMcpSessionManager.cs` creates a short-lived session for the MCP servers granted to one run.
2. The runner receives only the gateway session URL and bearer token, not upstream credentials.
3. The session manager selects the HTTP or STDIO transport through an explicit factory contract.
4. The selected transport project forwards JSON-RPC to its upstream.
5. An embedded binary resource with an `artifact://` or `https://` URI is decoded into the run's
   temporary `./.gateway/mcp-files` directory and replaced by a small textual result
   containing its local path, media type, size, checksum, and a run-scoped HTTP URL.
   This includes Todoist's binary XLSX/DOCX attachments, even when their media type
   is `application/octet-stream`. Only the supplied bytes are decoded; the source
   URI is never fetched. Text resources and links without embedded bytes remain unchanged.
6. Another MCP server on the private Gateway network can download that file by
   the opaque URL. Gateway verifies the on-disk checksum before serving it and
   revokes the URL when the run ends; the agent never needs to copy base64.
7. The session manager and cleanup service revoke expired or completed sessions.

## Generated output attachments

For an authorized `tools/call`, an `agentFileUrl` argument may contain a relative
`./artifacts/...` path to a newly generated output. Before forwarding the call,
Gateway snapshots that regular file and replaces the path with an opaque run-scoped
download URL. This works for Teams uploads and other MCP tools accepting the same
file-transfer convention, without base64 in the agent context or shell networking.

Exports are restricted to the run's `artifacts` directory. Traversal, symlinked
directories/files, and files exceeding the existing per-file/per-run limits are
rejected. Authorization runs before reading files. Snapshots keep their checksum
and expire with the run, even if the original output is edited afterward. The
receiving service must allow the configured `McpGateway:RunnerBaseUrl` origin;
the Compose default is `http://gateway:8080`.

## Folders

- `Sessions/` — session creation, authorization, lifetime, and cleanup.
- `Files/` — bounded, run-local materialization of explicitly marked MCP resources.
- `Transport/` — runner-facing request handling plus the contracts implemented by the HTTP and STDIO transport projects; request data is under `Transport/Models/`.
- `Configuration/Models/` — session and runner-address settings.
- `Errors/` — transport-specific failures.
- `Composition/` — registration for session management and cleanup.

Transport implementations live in `CodexGateway.McpGateway.Http` and
`CodexGateway.McpGateway.Stdio`. The core gateway does not contain either implementation.
