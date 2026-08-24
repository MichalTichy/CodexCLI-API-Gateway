# CodexGateway.Infrastructure.Codex

This project implements gateway-managed Codex authentication, the configured model catalog, MCP metadata discovery, and container-only agent execution.

## Main flow

1. `Authentication/HostCodexAuthenticationManager.cs` runs the host CLI for device login, status, and logout against the configured Codex home.
2. `Models/ConfiguredCodexModelCatalog.cs` exposes the validated, restart-scoped model configuration without starting Codex.
3. `Containers/ContainerCodexRunner.cs` receives a protocol-neutral agent-run request.
4. `Containers/ContainerRuntime.cs` creates an isolated command with the workspace and that same Codex home mounted explicitly.
5. The runtime monitors, terminates, and removes the container while the storage module enforces artifact limits.

## Folders

- `Authentication/` — short-lived host Codex authentication commands and device-code lifecycle.
- `Models/` — configuration-backed model catalog.
- `Containers/` — container command construction, execution, lifecycle, and environment filtering; data-only values are under `Containers/Models/`.
- `Mcp/` — MCP metadata discovery through a short-lived Codex App Server inside an isolated discovery container.
- `Processes/` — process-lifecycle helpers shared inside this adapter.
- `Composition/` — this module's installer.
