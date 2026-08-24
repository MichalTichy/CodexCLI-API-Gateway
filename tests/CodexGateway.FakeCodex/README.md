# CodexGateway.FakeCodex

This executable is a deterministic test double for the host Codex authentication CLI, containerized Codex protocols, and the container engine.

## Main flow

1. A test writes scenario files into an isolated scenario directory.
2. `Program.cs` selects device-auth/status/logout, MCP app-server, agent-run, or container-engine behavior from its arguments.
3. The process emits the same JSON-lines or process results expected by the production adapters.
4. Tests inspect captured requests and lifecycle markers without invoking a real Codex installation or container engine.
