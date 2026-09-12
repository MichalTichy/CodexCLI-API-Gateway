using System.Text.Json;

namespace CodexGateway.McpGateway.Sessions;

internal static class GatewayMcpToolCallAuthorizer
{
    public static void EnsureAllowed(ReadOnlyMemory<byte> body, IReadOnlySet<string> enabledTools)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status400BadRequest,
                "The MCP request body is not valid JSON.",
                exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in document.RootElement.EnumerateArray())
                {
                    EnsureMessageAllowed(message, enabledTools);
                }

                return;
            }

            EnsureMessageAllowed(document.RootElement, enabledTools);
        }
    }

    private static void EnsureMessageAllowed(
        JsonElement message,
        IReadOnlySet<string> enabledTools)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            throw InvalidRequest("An MCP JSON-RPC message must be an object.");
        }

        if (!TryGetUniqueProperty(message, "method", out var method))
        {
            return;
        }

        if (method.ValueKind != JsonValueKind.String)
        {
            throw InvalidRequest("The MCP JSON-RPC method must be a string.");
        }

        if (!string.Equals(method.GetString(), "tools/call", StringComparison.Ordinal))
        {
            return;
        }

        if (!TryGetUniqueProperty(message, "params", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Object ||
            !TryGetUniqueProperty(parameters, "name", out var name) ||
            name.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(name.GetString()))
        {
            throw InvalidRequest("The MCP tools/call request does not identify a tool.");
        }

        if (!enabledTools.Contains(name.GetString()!))
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status403Forbidden,
                $"The MCP tool '{name.GetString()}' is not enabled for this API key and project.");
        }
    }

    private static bool TryGetUniqueProperty(
        JsonElement value,
        string propertyName,
        out JsonElement propertyValue)
    {
        propertyValue = default;
        var found = false;
        foreach (var property in value.EnumerateObject())
        {
            if (!property.NameEquals(propertyName))
            {
                continue;
            }

            if (found)
            {
                throw InvalidRequest(
                    $"The MCP JSON-RPC message contains more than one '{propertyName}' property.");
            }

            propertyValue = property.Value;
            found = true;
        }

        return found;
    }

    private static GatewayMcpRequestException InvalidRequest(string message) =>
        new(StatusCodes.Status400BadRequest, message);
}
