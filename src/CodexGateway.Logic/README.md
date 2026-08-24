# CodexGateway.Logic

This project contains protocol-neutral application behavior. HTTP endpoints and Blazor components call MediatR use cases; use cases depend on repository specifications and infrastructure interfaces rather than concrete adapters.

## Main flow

1. A caller sends a request from `UseCases/{Purpose}/{UseCaseName}.cs` through MediatR.
2. The handler beside that request in the same file validates input and coordinates the operation.
3. Specifications asynchronously filter and project only the required data from `GatewayState` before materialization. Authentication compares the supplied API key in the database and returns only its ID, so the authentication path never materializes stored secrets.
4. The handler returns a model from the relevant purpose folder without depending on an HTTP schema.

## Folders

- `UseCases/` — commands and queries grouped by purpose; each use-case file contains its request and handler.
- `Specifications/` — named repository queries grouped by the data they select.
- `Codex/`, `Generation/`, `McpServers/`, `Storage/`, and `Tools/` — application services, ports, and their `Models/`.
- `Security/` — authenticated request identity and API-key identity models.
- `Configuration/Models/` — validated gateway and Codex settings.
- `Errors/` — protocol-neutral failures returned by use cases and adapters.
- `Composition/` — this module's installer.
