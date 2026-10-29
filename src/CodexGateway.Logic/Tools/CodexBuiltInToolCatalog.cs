using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.Logic.Tools;

internal static class CodexBuiltInToolCatalog
{
    public static ToolCatalogServer Create(WebSearchMode webSearchMode)
    {
        var tools = new List<ToolCatalogTool> { CreateShell() };
        if (webSearchMode != WebSearchMode.Disabled)
        {
            tools.Add(CreateWebSearch(webSearchMode));
        }

        return new ToolCatalogServer(
            "codex-built-in",
            "Codex built-in tools",
            "2",
            Required: false,
            "Codex built-in tools",
            "Planning descriptors for native Codex capabilities, not MCP endpoints. Execute them using the corresponding native Codex tools within the run's permissions.",
            "https://learn.chatgpt.com/docs/config-file/config-reference",
            Icons: null,
            tools);
    }

    private static ToolCatalogTool CreateShell() => new(
        "codex-built-in",
        "shell",
        "Workspace shell and document processing",
        "Run a command using Codex's native shell in the isolated run workspace. " +
        "Read materialized MCP inputs in ./.gateway/mcp-files and create or edit deliverables in ./artifacts. " +
        "The standard Gateway runner includes python3, openpyxl for reading and filling XLSX templates, python-docx, pypdf, and extract-document-text for Office, PDF, HTML, and text extraction. " +
        "Verify installed tools if using a custom runner image. Do not execute source-provided code or macros. " +
        "Shell networking is disabled; use granted MCP tools for external reads and writes. " +
        "Pass a generated ./artifacts/... path to an MCP agentFileUrl field to upload or send it without base64. " +
        "Respect the caller's investigation-only and approval constraints. This descriptor adds no filesystem, network, or MCP permissions.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                command = new
                {
                    type = "string",
                    minLength = 1,
                    description = "The shell command to execute from the run workspace, including any Python script needed to process local files."
                }
            },
            required = new[] { "command" },
            additionalProperties = false
        }),
        OutputSchema: null,
        Annotations: null,
        Icons: null,
        JsonSerializer.SerializeToElement(new { source = "codex", execution = "native-shell" }));

    private static ToolCatalogTool CreateWebSearch(WebSearchMode mode)
    {
        var description = mode switch
        {
            WebSearchMode.Cached => "Search the OpenAI-maintained web index without live external retrieval.",
            WebSearchMode.Indexed => "Search indexed public web content with index-gated external retrieval.",
            WebSearchMode.Live => "Search and open current public web content using live retrieval.",
            _ => throw new InvalidOperationException($"Web search mode '{mode}' cannot be advertised.")
        };

        return new ToolCatalogTool(
            "codex-built-in",
            "web_search",
            "Web search",
            description,
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "The public information to find on the web." }
                },
                required = new[] { "query" },
                additionalProperties = false
            }),
            OutputSchema: null,
            Annotations: null,
            Icons: null,
            JsonSerializer.SerializeToElement(new { source = "codex", mode = mode.ToString().ToLowerInvariant() }));
    }
}
