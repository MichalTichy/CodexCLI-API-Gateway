# CodexGateway.Models

This project contains the persisted gateway state and the domain data shared by the application and infrastructure layers. It contains no orchestration or storage implementation.

## Main flow

1. `Gateway/GatewayState.cs` is the single persisted aggregate.
2. API keys, projects, and MCP servers are stored as definitions inside that aggregate.
3. Logic use cases read or update those definitions through repository specifications.

## Folders

- `ApiKeys/` — stored API-key definitions, including the secret used for authentication.
- `Files/` — metadata for uploaded and project-owned files.
- `Gateway/` — the root persisted aggregate.
- `McpServers/` — HTTP and stdio MCP server definitions.
- `Projects/` — projects, access grants, MCP assignments, and resolved access.
