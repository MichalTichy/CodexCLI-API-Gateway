# CodexGateway.Infrastructure.Codex

This project implements gateway-managed Codex authentication, account-specific model discovery, MCP metadata discovery, and container-only agent execution.

## Main flow

1. `Authentication/HostCodexAuthenticationManager.cs` runs the host CLI for device login, status, and logout against the configured Codex home.
2. `Models/CodexModelCatalog.cs` asks Codex App Server in the pinned runner image for the authenticated account's visible models and supported reasoning efforts, then briefly caches the validated result.
3. `Containers/ContainerCodexRunner.cs` receives a protocol-neutral agent-run request.
4. `Containers/ContainerRuntime.cs` creates an isolated command with the workspace and that same Codex home mounted explicitly.
5. The runtime monitors, terminates, and removes the container while the storage module enforces artifact limits.

## Folders

- `Authentication/` — short-lived host Codex authentication commands and device-code lifecycle.
- `Models/` — isolated account-specific model discovery, response validation, and short-lived catalog caching.
- `Containers/` — container command construction, execution, lifecycle, and environment filtering; data-only values are under `Containers/Models/`.
- `Mcp/` — MCP metadata discovery through a short-lived Codex App Server inside an isolated discovery container.
- `Processes/` — process-lifecycle helpers shared inside this adapter.
- `Composition/` — this module's installer.
