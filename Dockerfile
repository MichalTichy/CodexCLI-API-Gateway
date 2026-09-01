# syntax=docker/dockerfile:1.7

ARG CODEX_VERSION=0.148.0
ARG DOCKER_CLI_IMAGE=docker:28.3.3-cli

FROM ${DOCKER_CLI_IMAGE} AS docker-cli

FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS build
WORKDIR /source

COPY Directory.Build.props global.json CodexGateway.slnx ./
COPY SharedInfrastructure/Shared.Infrastructure.CurrentTenancyProvider/Shared.Infrastructure.CurrentTenancyProvider.csproj SharedInfrastructure/Shared.Infrastructure.CurrentTenancyProvider/
COPY SharedInfrastructure/Shared.Infrastructure.IoC/Shared.Infrastructure.IoC.csproj SharedInfrastructure/Shared.Infrastructure.IoC/
COPY SharedInfrastructure/Shared.Infrastructure.Initializer/Shared.Infrastructure.Initializer.csproj SharedInfrastructure/Shared.Infrastructure.Initializer/
COPY SharedInfrastructure/Shared.Infrastructure.LeaderElection/Shared.Infrastructure.LeaderElection.csproj SharedInfrastructure/Shared.Infrastructure.LeaderElection/
COPY SharedInfrastructure/Shared.Infrastructure.Persistence/Shared.Infrastructure.Persistence.csproj SharedInfrastructure/Shared.Infrastructure.Persistence/
COPY SharedInfrastructure/Shared.Infrastructure.Persistence.Marten/Shared.Infrastructure.Persistence.Marten.csproj SharedInfrastructure/Shared.Infrastructure.Persistence.Marten/
COPY src/CodexGateway.Api/CodexGateway.Api.csproj src/CodexGateway.Api/
COPY src/CodexGateway.Api.OpenAI/CodexGateway.Api.OpenAI.csproj src/CodexGateway.Api.OpenAI/
COPY src/CodexGateway.App/CodexGateway.App.csproj src/CodexGateway.App/
COPY src/CodexGateway.Infrastructure.Codex/CodexGateway.Infrastructure.Codex.csproj src/CodexGateway.Infrastructure.Codex/
COPY src/CodexGateway.McpGateway/CodexGateway.McpGateway.csproj src/CodexGateway.McpGateway/
COPY src/CodexGateway.McpGateway.Http/CodexGateway.McpGateway.Http.csproj src/CodexGateway.McpGateway.Http/
COPY src/CodexGateway.McpGateway.Stdio/CodexGateway.McpGateway.Stdio.csproj src/CodexGateway.McpGateway.Stdio/
COPY src/CodexGateway.Infrastructure.Persistence/CodexGateway.Infrastructure.Persistence.csproj src/CodexGateway.Infrastructure.Persistence/
COPY src/CodexGateway.Infrastructure.FileStorage/CodexGateway.Infrastructure.FileStorage.csproj src/CodexGateway.Infrastructure.FileStorage/
COPY src/CodexGateway.Logic/CodexGateway.Logic.csproj src/CodexGateway.Logic/
COPY src/CodexGateway.Models/CodexGateway.Models.csproj src/CodexGateway.Models/
COPY src/CodexGateway.ServiceDefaults/CodexGateway.ServiceDefaults.csproj src/CodexGateway.ServiceDefaults/
RUN dotnet restore src/CodexGateway.App/CodexGateway.App.csproj

COPY src/CodexGateway.Api/ src/CodexGateway.Api/
COPY SharedInfrastructure/ SharedInfrastructure/
COPY src/CodexGateway.Api.OpenAI/ src/CodexGateway.Api.OpenAI/
COPY src/CodexGateway.App/ src/CodexGateway.App/
COPY src/CodexGateway.Infrastructure.Codex/ src/CodexGateway.Infrastructure.Codex/
COPY src/CodexGateway.McpGateway/ src/CodexGateway.McpGateway/
COPY src/CodexGateway.McpGateway.Http/ src/CodexGateway.McpGateway.Http/
COPY src/CodexGateway.McpGateway.Stdio/ src/CodexGateway.McpGateway.Stdio/
COPY src/CodexGateway.Infrastructure.Persistence/ src/CodexGateway.Infrastructure.Persistence/
COPY src/CodexGateway.Infrastructure.FileStorage/ src/CodexGateway.Infrastructure.FileStorage/
COPY src/CodexGateway.Logic/ src/CodexGateway.Logic/
COPY src/CodexGateway.Models/ src/CodexGateway.Models/
COPY src/CodexGateway.ServiceDefaults/ src/CodexGateway.ServiceDefaults/
RUN dotnet publish src/CodexGateway.App/CodexGateway.App.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9-noble AS codex-runtime
ARG CODEX_VERSION

RUN apt-get update \
    && apt-get install --yes --no-install-recommends bubblewrap ca-certificates nodejs npm \
    && npm install --global "@openai/codex@${CODEX_VERSION}" \
    && npm cache clean --force \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --uid 10001 gateway \
    && mkdir -p /workspace /codex-home \
    && chown -R gateway:gateway /workspace /codex-home /home/gateway

# Repository-owned, deterministic MCP fixture used only by the opt-in live
# Aspire smoke test. Production MCP servers should be installed in the section
# below with their versions pinned.
COPY test-assets/mcp-smoke-server.mjs /opt/codex-gateway/mcp-smoke-server.mjs
RUN chmod 0555 /opt/codex-gateway/mcp-smoke-server.mjs

# -----------------------------------------------------------------------------
# Add approved STDIO MCP servers here.
#
# Keep package versions pinned. Each server command must be available on PATH in
# the runner image because Codex starts it directly inside the run container.
#
# Example for an npm-packaged MCP server:
# RUN npm install --global "@modelcontextprotocol/server-filesystem@2025.1.14" \
#     && npm cache clean --force
#
# Example for a Python-based MCP server:
# RUN apt-get update \
#     && apt-get install --yes --no-install-recommends python3 python3-pip \
#     && pip3 install --no-cache-dir "example-mcp-server==1.2.3" \
#     && rm -rf /var/lib/apt/lists/*
# -----------------------------------------------------------------------------

# The only model-execution image. The gateway always reinforces this entrypoint,
# UID and the runtime security flags when it creates a run container.
FROM codex-runtime AS runner
WORKDIR /workspace
ENV CODEX_HOME=/codex-home \
    HOME=/home/gateway
USER 10001:10001
ENTRYPOINT ["codex"]

# The control-plane image retains Codex for App Server model/auth operations and
# adds only the Docker client; model execution happens in sibling runner containers.
FROM codex-runtime AS gateway
WORKDIR /app
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=build --chown=gateway:gateway /app/publish .

RUN chmod 0555 /app/CodexGateway.App \
    && mkdir -p /app/data /app/.codex-home \
    && chown -R gateway:gateway /app

ENV ASPNETCORE_URLS=http://+:8080 \
    Gateway__StoragePath=/app/data \
    Codex__ExecutablePath=codex \
    Codex__HomePath=/app/.codex-home \
    Codex__Container__EngineExecutablePath=docker \
    Codex__Container__Image=codex-gateway-runner:0.148.0

VOLUME ["/app/data", "/app/.codex-home"]
EXPOSE 8080
USER 10001:10001
ENTRYPOINT ["/app/CodexGateway.App"]
