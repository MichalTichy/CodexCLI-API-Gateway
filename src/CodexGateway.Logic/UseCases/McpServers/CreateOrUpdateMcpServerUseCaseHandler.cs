using System.Text.RegularExpressions;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.McpServers;

public sealed partial class CreateOrUpdateMcpServerUseCaseHandler(
    IGatewayConfigurationRepository repository)
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

        await repository.UpdateAsync(state => state with
        {
            McpServers = [
                .. state.McpServers.Where(existing => existing.Id != id),
                normalized
            ]
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

                if (server.ExecutionMode == McpExecutionMode.Gateway &&
                    string.IsNullOrWhiteSpace(http.BearerTokenEnvironmentVariable))
                {
                    throw GatewayException.InvalidRequest(
                        "Gateway-hosted HTTP MCP servers require an API-key environment variable.",
                        parameter: "bearer_token_environment_variable");
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

        var configuredEnvironmentVariables = server.EnvironmentVariables
            .Cast<string?>()
            .Concat(server is HttpMcpServerDefinition httpServer
                ? Enumerable.Repeat(httpServer.BearerTokenEnvironmentVariable, 1)
                : [])
            .Where(name => name is not null)
            .Cast<string>();
        foreach (var variable in configuredEnvironmentVariables)
        {
            if (!EnvironmentVariablePattern().IsMatch(variable) ||
                McpCredentialVariablePolicy.IsReserved(variable))
            {
                throw GatewayException.InvalidRequest(
                    $"Environment variable '{variable}' cannot be forwarded to an MCP server.",
                    parameter: "environment_variables");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentVariablePattern();
}
