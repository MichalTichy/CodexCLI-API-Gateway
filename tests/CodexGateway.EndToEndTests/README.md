# CodexGateway.EndToEndTests

This project exercises the in-memory gateway host through its real HTTP and Blazor boundaries.

## Main flow

1. `Infrastructure/GatewayFactory.cs` starts the application in the Testing environment with isolated storage and seeded repository state.
2. A feature test calls the public endpoint or admin UI using real authentication and serialization.
3. The fake Codex executable provides deterministic control-plane and container responses.
4. The test verifies the HTTP result and relevant filesystem or repository side effects.

Feature tests are grouped under `Admin/`, `Api/`, `Authentication/`, `ChatCompletions/`, `Containers/`, `Files/`, `Mcp/`, `Projects/`, `Runs/`, and `Security/`.
