# CodexGateway.ServiceDefaults

This project contains the common Aspire hosting defaults used by executable gateway projects.

## Main flow

1. An executable calls the extensions in `Hosting/Extensions.cs` while building its host.
2. The extensions register health checks, service discovery, resilience, and telemetry defaults.
3. The executable maps the shared health endpoints as part of its normal endpoint setup.
