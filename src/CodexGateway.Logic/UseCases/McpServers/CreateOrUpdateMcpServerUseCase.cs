using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;
using System.Text.RegularExpressions;

namespace CodexGateway.Logic.UseCases.McpServers;

public sealed record CreateOrUpdateMcpServerUseCase(McpServerDefinition Server)
    : IRequest<McpServerDefinition>;

public sealed partial class CreateOrUpdateMcpServerUseCaseHandler(
    IRepository<GatewayState> repository)
    : IRequestHandler<CreateOrUpdateMcpServerUseCase, McpServerDefinition>
{
    public async Task<McpServerDefinition> Handle(
        CreateOrUpdateMcpServerUseCase request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Server);
        var server = request.Server;
        if (string.IsNullOrWhiteSpace(server.Id))
        {
            throw GatewayException.InvalidRequest("MCP server ID is required.", parameter: "id");
        }

        var id = server.Id.Trim().ToLowerInvariant();
        if (!IdentifierPattern().IsMatch(id))
        {
            throw GatewayException.InvalidRequest(
                "MCP server IDs must contain lowercase letters, numbers, or hyphens.",
                parameter: "id");
        }

        Validate(server);
        var availableTools = (server.AvailableTools ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var environmentVariables = (server.EnvironmentVariables ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        McpServerDefinition normalized = server switch
        {
            HttpMcpServerDefinition http => http with
            {
                Id = id,
                Name = server.Name.Trim(),
                EnvironmentHeaders = new Dictionary<string, string>(
                    http.EnvironmentHeaders ?? [],
                    StringComparer.OrdinalIgnoreCase),
                EnvironmentVariables = environmentVariables,
                AvailableTools = availableTools
            },
            StdioMcpServerDefinition stdio => stdio with
            {
                Id = id,
                Name = server.Name.Trim(),
                Arguments = (stdio.Arguments ?? []).ToList(),
                EnvironmentVariables = environmentVariables,
                AvailableTools = availableTools
            },
            _ => throw GatewayException.InvalidRequest(
                "MCP server transport is invalid.",
                parameter: "transport")
        };

        await repository.GetAndUpdateAsync(GatewayState.DocumentId, state =>
        {
            state.McpServers = [
                .. state.McpServers.Where(existing => existing.Id != id),
                normalized
            ];
        }, cancellationToken);
        return normalized;
    }

    private static void Validate(McpServerDefinition server)
    {
        if (string.IsNullOrWhiteSpace(server.Name))
        {
            throw GatewayException.InvalidRequest("MCP server name is required.", parameter: "name");
        }

        if (!Enum.IsDefined(server.ExecutionMode))
        {
            throw GatewayException.InvalidRequest(
                "MCP server execution mode is invalid.",
                parameter: "execution_mode");
        }

        switch (server)
        {
            case HttpMcpServerDefinition http:
                if (!Uri.TryCreate(http.Url, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                {
                    throw GatewayException.InvalidRequest(
                        "HTTP MCP servers require an absolute URL.",
                        parameter: "url");
                }

                var environmentHeaders = http.EnvironmentHeaders ?? [];
                if (environmentHeaders.Keys.Any(header => !HeaderNamePattern().IsMatch(header)))
                {
                    throw GatewayException.InvalidRequest(
                        "HTTP MCP header names must be valid HTTP token values.",
                        parameter: "environment_headers");
                }

                if (environmentHeaders
                    .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1))
                {
                    throw GatewayException.InvalidRequest(
                        "HTTP MCP header names must be unique.",
                        parameter: "environment_headers");
                }

                foreach (var variable in environmentHeaders.Values)
                {
                    ValidateEnvironmentVariable(variable, "environment_headers");
                }

                break;
            case StdioMcpServerDefinition stdio:
                if (string.IsNullOrWhiteSpace(stdio.Command))
                {
                    throw GatewayException.InvalidRequest(
                        "STDIO MCP servers require a command.",
                        parameter: "command");
                }

                if ((stdio.Arguments ?? []).Any(argument => argument is null))
                {
                    throw GatewayException.InvalidRequest(
                        "MCP command arguments cannot be null.",
                        parameter: "arguments");
                }

                break;
            default:
                throw GatewayException.InvalidRequest(
                    "MCP server transport is invalid.",
                    parameter: "transport");
        }

        if ((server.AvailableTools ?? []).Any(string.IsNullOrWhiteSpace))
        {
            throw GatewayException.InvalidRequest(
                "MCP tool names cannot be null or empty.",
                parameter: "available_tools");
        }

        foreach (var variable in server.EnvironmentVariables ?? [])
        {
            ValidateEnvironmentVariable(variable, "environment_variables");
        }
    }

    private static void ValidateEnvironmentVariable(string? variable, string parameter)
    {
        if (string.IsNullOrWhiteSpace(variable) ||
            !EnvironmentVariablePattern().IsMatch(variable) ||
            McpCredentialVariablePolicy.IsReserved(variable))
        {
            throw GatewayException.InvalidRequest(
                $"Environment variable '{variable}' cannot be forwarded to an MCP server.",
                parameter: parameter);
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentVariablePattern();

    [GeneratedRegex("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderNamePattern();
}
