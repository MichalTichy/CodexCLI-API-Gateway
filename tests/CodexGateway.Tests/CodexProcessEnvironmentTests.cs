using CodexGateway.Infrastructure.Codex;

namespace CodexGateway.Tests;

public sealed class CodexProcessEnvironmentTests
{
    [Fact]
    public void Child_environment_is_allowlisted_and_never_uses_gateway_or_provider_credentials()
    {
        var source = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = "/safe/bin",
            ["HTTPS_PROXY"] = "http://proxy.test",
            ["Gateway__ApiKeys__0__Key"] = "gateway-secret",
            ["AdminUi__Password"] = "admin-secret",
            ["ConnectionStrings__State"] = "database-secret",
            ["OPENAI_API_KEY"] = "provider-secret",
            ["CODEX_API_KEY"] = "codex-secret",
            ["PROJECT_MCP_TOKEN"] = "mcp-secret",
            ["UNRELATED_SECRET"] = "other-secret"
        };

        var environment = CodexProcessEnvironment.Build(
            source,
            "/gateway/codex-home",
            "/gateway/run/tmp",
            ["PROJECT_MCP_TOKEN", "OPENAI_API_KEY"]);

        Assert.Equal("/safe/bin", environment["PATH"]);
        Assert.Equal("http://proxy.test", environment["HTTPS_PROXY"]);
        Assert.Equal("mcp-secret", environment["PROJECT_MCP_TOKEN"]);
        Assert.Equal(Path.GetFullPath("/gateway/codex-home"), environment["CODEX_HOME"]);
        Assert.Equal(Path.GetFullPath("/gateway/run/tmp"), environment["TMPDIR"]);
        Assert.DoesNotContain("Gateway__ApiKeys__0__Key", environment.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("AdminUi__Password", environment.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings__State", environment.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("OPENAI_API_KEY", environment.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNRELATED_SECRET", environment.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
