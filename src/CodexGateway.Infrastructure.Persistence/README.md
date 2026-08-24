# CodexGateway.Infrastructure.Persistence

This project connects the application repository contracts to gateway-state persistence through Marten/PostgreSQL in every environment.

## Main flow

1. `Composition/PersistenceInfrastructureInstaller.cs` configures the shared Marten repository against `ConnectionStrings:Gateway`.
2. `Initialization/GatewayStateInitializer.cs` ensures the single `GatewayState` document exists.
3. Logic use cases read and atomically update that document through repository specifications.

## Folders

- `Composition/` — dependency-injection registration and Marten configuration.
- `Initialization/` — creation of the empty gateway aggregate.
