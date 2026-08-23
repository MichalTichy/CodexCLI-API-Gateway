# CodexGateway.IoC

This project defines the installer convention used by every gateway module.

## Main flow

1. A module implements `IInstaller` or one of its priority marker interfaces.
2. `InstallerDiscovery` scans the selected assemblies for installer implementations.
3. Installers run in priority order and register their module with dependency injection.

The `Installers/` folder contains the contracts and discovery implementation together because they form one composition mechanism.
