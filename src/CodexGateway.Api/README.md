# CodexGateway.Api

This project contains HTTP-hosting concerns shared by protocol adapters. It does not define a public API schema itself.

## Main flow

1. `Composition/GatewayApiInstaller.cs` registers shared endpoint infrastructure and discovers protocol endpoint assemblies.
2. A protocol adapter authenticates its request and creates a protocol-neutral gateway request context.
3. `Security/GatewayRequestContextExtensions.cs` makes that context available to endpoint handlers.

## Folders

- `Composition/` — shared API registration and endpoint-assembly markers.
- `Security/` — translation from an authenticated HTTP request to application request context.
