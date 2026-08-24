# CodexGateway.EndToEndTests

This project exercises the gateway host through its real HTTP, Blazor, Marten, and PostgreSQL boundaries.

## Main flow

1. `Infrastructure/GatewayFactory.cs` creates an isolated PostgreSQL database, starts the application in the Testing environment, and seeds state through the production repository.
2. A feature test calls the public endpoint or admin UI using real authentication and serialization.
3. The fake Codex executable provides deterministic control-plane and container responses.
4. The test verifies the HTTP result and relevant filesystem or database side effects.

Feature tests are grouped under `Admin/`, `Api/`, `Authentication/`, `ChatCompletions/`, `Containers/`, `Files/`, `Mcp/`, `Projects/`, `Runs/`, and `Security/`.
