# CodexGateway.AppHost

This Aspire host starts the local PostgreSQL database, builds the runner image, and launches the gateway with development paths mounted for storage and Codex authentication.

## Main flow

1. `Program.cs` resolves local data and Codex-home paths.
2. Aspire provisions PostgreSQL and builds the runner target from the repository Dockerfile.
3. The gateway project starts with references and environment settings for those resources.
4. Aspire waits for PostgreSQL and the runner image before considering the gateway ready.
