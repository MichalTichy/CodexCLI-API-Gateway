namespace CodexGateway.Logic.Codex;

public static class McpCredentialVariablePolicy
{
    public static bool IsReserved(string name) =>
        name.Equals("OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("CODEX_API_KEY", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("AZURE_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Gateway__", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("AdminUi__", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase);
}
