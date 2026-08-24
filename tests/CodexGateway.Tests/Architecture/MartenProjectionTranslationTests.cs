using CodexGateway.Logic.Security.Models;
using CodexGateway.Models.Gateway;
using JasperFx;
using Marten;
using System.Text.RegularExpressions;

namespace CodexGateway.Tests.Architecture;

public sealed class MartenProjectionTranslationTests
{
    [Fact]
    public void Gateway_state_child_projections_are_translatable_and_omit_unused_fields()
    {
        using var store = DocumentStore.For(options =>
        {
            options.Connection("Host=localhost;Database=projection_test;Username=test;Password=test");
            options.AutoCreateSchemaObjects = AutoCreate.None;
            options.Schema.For<GatewayState>();
        });
        using var session = store.QuerySession();
        var assignedServerIds = new[] { "docs" };

        const string suppliedSecret = "supplied-secret";
        using var authenticatedApiKeyCommand = session.Query<GatewayState>()
            .SelectMany(state => state.ApiKeys)
            .Where(key => key.Key == suppliedSecret)
            .Select(key => key.Id)
            .ToCommand();
        using var apiKeyIdentitiesCommand = session.Query<GatewayState>()
            .SelectMany(state => state.ApiKeys)
            .Select(key => new ApiKeyIdentity(key.Id, key.Name))
            .ToCommand();
        using var mcpServersCommand = session.Query<GatewayState>()
            .SelectMany(state => state.McpServers)
            .Where(server => assignedServerIds.Contains(server.Id))
            .Select(server => server)
            .ToCommand();
        using var projectCommand = session.Query<GatewayState>()
            .SelectMany(state => state.Projects)
            .Where(project => project.Id == "sample")
            .Select(project => project)
            .ToCommand();

        Assert.All(
            new[]
            {
                authenticatedApiKeyCommand.CommandText,
                apiKeyIdentitiesCommand.CommandText,
                mcpServersCommand.CommandText,
                projectCommand.CommandText
            },
            Assert.NotEmpty);
        var authenticationProjection = Regex.Matches(
                authenticatedApiKeyCommand.CommandText,
                @"select(?<projection>.*?)from",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Last()
            .Groups["projection"]
            .Value;
        Assert.Contains("'Id'", authenticationProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("'Key'", authenticationProjection, StringComparison.Ordinal);
        Assert.Contains("'Key'", authenticatedApiKeyCommand.CommandText, StringComparison.Ordinal);
        Assert.Contains("'Id'", apiKeyIdentitiesCommand.CommandText, StringComparison.Ordinal);
        Assert.Contains("'Name'", apiKeyIdentitiesCommand.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("'Key'", apiKeyIdentitiesCommand.CommandText, StringComparison.Ordinal);
    }
}
