# CodexGateway.Logic

This project contains protocol-neutral application behavior. HTTP endpoints and Blazor components call MediatR use cases; use cases depend on repository specifications and infrastructure interfaces rather than concrete adapters.

## Main flow

1. A caller sends a request from `UseCases/{Purpose}/Models/` through MediatR.
2. The matching handler in `UseCases/{Purpose}/` validates input and coordinates the operation.
3. Specifications select data from `GatewayState`; storage, Codex, and MCP interfaces perform external work.
4. The handler returns a model from the relevant purpose folder without depending on an HTTP schema.

## Folders

- `UseCases/` — commands, queries, and their handlers, grouped by purpose.
- `Specifications/` — named repository queries grouped by the data they select.
- `Codex/`, `Generation/`, `McpServers/`, `Storage/`, and `Tools/` — application services, ports, and their `Models/`.
- `Security/` — authenticated request identity and API-key identity models.
- `Configuration/Models/` — validated gateway and Codex settings.
- `Errors/` — protocol-neutral failures returned by use cases and adapters.
- `Composition/` — this module's installer.
