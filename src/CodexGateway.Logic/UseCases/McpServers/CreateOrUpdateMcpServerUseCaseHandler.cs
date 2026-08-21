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
        var normalized = server with
        {
            Id = id,
            Name = server.Name.Trim(),
            Arguments = (server.Arguments ?? []).ToList(),
            AvailableTools = (server.AvailableTools ?? [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList(),
            EnvironmentVariables = (server.EnvironmentVariables ?? [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList()
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

        if (server.Transport == McpTransport.Http &&
            (!Uri.TryCreate(server.Url, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("http" or "https")))
        {
            throw GatewayException.InvalidRequest(
                "HTTP MCP servers require an absolute URL.",
                parameter: "url");
        }

        if (server.Transport == McpTransport.Stdio && string.IsNullOrWhiteSpace(server.Command))
        {
            throw GatewayException.InvalidRequest(
                "STDIO MCP servers require a command.",
                parameter: "command");
        }

        if (server.ExecutionMode == McpExecutionMode.Gateway &&
            server.Transport == McpTransport.Http &&
            string.IsNullOrWhiteSpace(server.BearerTokenEnvironmentVariable))
        {
            throw GatewayException.InvalidRequest(
                "Gateway-hosted HTTP MCP servers require an API-key environment variable.",
                parameter: "bearer_token_environment_variable");
        }

        if ((server.Arguments ?? []).Any(argument => argument is null))
        {
            throw GatewayException.InvalidRequest(
                "MCP command arguments cannot be null.",
                parameter: "arguments");
        }

        if ((server.AvailableTools ?? []).Any(string.IsNullOrWhiteSpace))
        {
            throw GatewayException.InvalidRequest(
                "MCP tool names cannot be null or empty.",
                parameter: "available_tools");
        }

        var environmentVariables = (server.EnvironmentVariables ?? [])
            .Append(server.BearerTokenEnvironmentVariable)
            .Where(name => name is not null)
            .Cast<string>();
        foreach (var variable in environmentVariables)
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
