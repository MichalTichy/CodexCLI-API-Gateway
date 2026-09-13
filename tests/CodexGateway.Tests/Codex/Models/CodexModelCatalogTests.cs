using System.Text.Json;
using CodexGateway.Infrastructure.Codex.Models;
using CodexGateway.Logic.Errors;

namespace CodexGateway.Tests.Codex.Models;

public sealed class CodexModelCatalogTests
{
    [Fact]
    public void Parse_page_preserves_every_advertised_reasoning_effort_and_skips_hidden_models()
    {
        using var page = JsonDocument.Parse(
            """
            {
              "data": [
                {
                  "model": "gpt-visible",
                  "displayName": "GPT Visible",
                  "hidden": false,
                  "supportedReasoningEfforts": [
                    { "reasoningEffort": "low" },
                    { "reasoningEffort": "xhigh" },
                    { "reasoningEffort": "max" },
                    { "reasoningEffort": "ultra" }
                  ],
                  "defaultReasoningEffort": "low"
                },
                {
                  "model": "gpt-hidden",
                  "displayName": "GPT Hidden",
                  "hidden": true,
                  "supportedReasoningEfforts": [
                    { "reasoningEffort": "high" }
                  ],
                  "defaultReasoningEffort": "high"
                }
              ],
              "nextCursor": "next-page"
            }
            """);

        var models = CodexModelCatalog.ParsePage(
            page.RootElement,
            new HashSet<string>(StringComparer.Ordinal),
            out var nextCursor);

        var model = Assert.Single(models);
        Assert.Equal("gpt-visible", model.Id);
        Assert.Equal("GPT Visible", model.Name);
        Assert.Equal(["low", "xhigh", "max", "ultra"], model.SupportedReasoningEfforts);
        Assert.Equal("low", model.DefaultReasoningEffort);
        Assert.Equal("next-page", nextCursor);
    }

    [Fact]
    public void Parse_page_rejects_duplicate_model_ids_across_pages()
    {
        using var page = JsonDocument.Parse(
            """
            {
              "data": [
                {
                  "model": "gpt-duplicate",
                  "displayName": "GPT Duplicate",
                  "hidden": false,
                  "supportedReasoningEfforts": [
                    { "reasoningEffort": "high" }
                  ],
                  "defaultReasoningEffort": "high"
                }
              ],
              "nextCursor": null
            }
            """);
        var seen = new HashSet<string>(StringComparer.Ordinal) { "gpt-duplicate" };

        Assert.Throws<CodexUnavailableException>(() =>
            CodexModelCatalog.ParsePage(page.RootElement, seen, out _));
    }

    [Fact]
    public void Parse_page_rejects_a_default_reasoning_effort_not_advertised_by_the_model()
    {
        using var page = JsonDocument.Parse(
            """
            {
              "data": [
                {
                  "model": "gpt-invalid",
                  "displayName": "GPT Invalid",
                  "hidden": false,
                  "supportedReasoningEfforts": [
                    { "reasoningEffort": "low" }
                  ],
                  "defaultReasoningEffort": "high"
                }
              ]
            }
            """);

        Assert.Throws<CodexUnavailableException>(() =>
            CodexModelCatalog.ParsePage(
                page.RootElement,
                new HashSet<string>(StringComparer.Ordinal),
                out _));
    }
}
