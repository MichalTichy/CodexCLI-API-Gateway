using CodexGateway.Logic.Errors;

namespace CodexGateway.App.Components.Pages.Admin.Shared;

internal static class AdminText
{
    public static List<string> Lines(string? value) =>
        (value ?? string.Empty)
            .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToList();

    public static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Describe(Exception exception) => exception is GatewayException gateway
        ? gateway.Message
        : "The operation failed. Check the gateway logs for details.";
}
