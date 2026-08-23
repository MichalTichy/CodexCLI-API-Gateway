# CodexGateway.Infrastructure.Codex

This project implements the Codex control plane and container-only agent execution.

## Main flow

1. `AppServer/CodexAppServerClient.cs` maintains the host-side Codex process used for account, login, and model operations.
2. `Containers/ContainerCodexRunner.cs` receives a protocol-neutral agent-run request.
3. `Containers/ContainerRuntime.cs` creates an isolated command with the workspace and Codex authentication directory mounted explicitly.
4. The runtime monitors, terminates, and removes the container while the storage module enforces artifact limits.

## Folders

- `AppServer/` — the long-lived host Codex process used for control-plane requests.
- `AppServer/` — the long-lived host Codex process and its request lifecycle.
- `Containers/` — container command construction, execution, lifecycle, and environment filtering; data-only values are under `Containers/Models/`.
- `Mcp/` — MCP metadata discovery through the Codex app server.
- `Processes/` — process-lifecycle helpers shared inside this adapter.
- `Composition/` — this module's installer.
