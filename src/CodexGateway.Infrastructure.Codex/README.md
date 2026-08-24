# CodexGateway.Infrastructure.Codex

This project implements container-only Codex agent execution.

## Main flow

1. `Containers/ContainerCodexRunner.cs` receives a protocol-neutral run request.
2. `Containers/ContainerRuntime.cs` creates an isolated container command with the workspace and Codex authentication directory mounted explicitly.
3. The runtime monitors, terminates, and removes the container while the storage module enforces artifact limits.

## Folders

- `AppServer/` — the long-lived host Codex process used for control-plane requests.
- `Containers/` — container command construction, execution, lifecycle, and environment filtering; data-only values are under `Containers/Models/`.
