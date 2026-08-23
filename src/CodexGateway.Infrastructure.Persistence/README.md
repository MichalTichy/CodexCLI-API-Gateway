# CodexGateway.Infrastructure.Persistence

This project connects the application repository contracts to gateway-state persistence. Production uses Marten/PostgreSQL; the testing environment uses the in-memory repository.

## Main flow

1. `Composition/PersistenceInfrastructureInstaller.cs` selects the repository implementation for the current environment.
2. `Initialization/GatewayStateInitializer.cs` ensures the single `GatewayState` document exists.
3. Logic use cases read and atomically update that document through repository specifications.

## Folders

- `Composition/` — dependency-injection registration and Marten configuration.
- `Initialization/` — creation of the empty gateway aggregate.
- `Repositories/` — gateway-specific repository implementations used outside production.
