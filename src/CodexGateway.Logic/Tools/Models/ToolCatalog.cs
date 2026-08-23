using System.Text.Json;

namespace CodexGateway.Logic.Tools;

public sealed record ToolCatalog(IReadOnlyList<ToolCatalogServer> Servers)
{
    public static ToolCatalog Empty { get; } = new([]);
}
