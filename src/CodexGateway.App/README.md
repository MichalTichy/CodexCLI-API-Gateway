# CodexGateway.App

This is the executable gateway host. It composes the protocol adapters and infrastructure modules, exposes the OpenAI-compatible and MCP routes, and hosts the Blazor administration UI.

## Main flow

1. `Program.cs` applies service defaults and runs installers discovered from the module assemblies.
2. The HTTP pipeline authenticates OpenAI requests, maps protocol endpoints, and exposes the internal MCP session route.
3. Admin authentication creates a server-side session and a non-persistent browser cookie.
4. Components under `Components/Pages/Admin/` send application use cases through MediatR and query repository specifications.

## Folders

- `Composition/` — registrations owned by the executable host.
- `Admin/Authentication/` — admin login routes and settings models.
- `Admin/Sessions/` — server-side admin-session validation for requests and Blazor circuits.
- `Components/Pages/Admin/{Purpose}/` — feature components; form and editor data is under each purpose's `Models/` folder.
- `Mcp/Endpoints/` — the internal MCP route used by isolated run containers.
- `Properties/` and `wwwroot/` — host metadata and static UI assets.
