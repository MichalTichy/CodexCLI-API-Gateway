using System.Text.Json;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Codex.Models;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.UseCases.McpServers;
using CodexGateway.Models;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CodexGateway.App.Components.Pages.Admin.McpServers;

public partial class McpCatalogSection : AdminComponentBase
{
    private McpServerEditorModel _create = new()
    {
        Enabled = true,
        ExecutionMode = McpExecutionMode.Gateway
    };
    private IReadOnlyList<McpToolMetadata> _discoveredTools = [];
    private bool _testingConnection;
    private string? _testedConnectionFingerprint;
    private string _testStatus = string.Empty;
    private string _testStatusKind = string.Empty;
    private bool _testValidationAttempted;
    private string? _focusInvalidFieldId;

    private bool ConnectionVerified => string.Equals(
        _testedConnectionFingerprint,
        ConnectionFingerprint(_create),
        StringComparison.Ordinal);

    private string CurrentTestStatus
    {
        get
        {
            if (_testValidationAttempted)
            {
                var errors = GetTestSettingErrors();
                return errors.Count > 0
                    ? string.Join(' ', errors.Select(error => error.Message))
                    : "Required settings are complete. Test the connection again.";
            }

            return _testStatusKind == "success" && !ConnectionVerified
                ? "Connection settings changed. Test the current settings again before adding this server."
                : _testStatus;
        }
    }

    private string CurrentTestStatusKind => _testValidationAttempted
        ? GetTestSettingErrors().Count > 0 ? "error" : "warning"
        : _testStatusKind == "success" && !ConnectionVerified
            ? "warning"
            : _testStatusKind;

    private string? ServerIdValidationError => _testValidationAttempted && string.IsNullOrWhiteSpace(_create.Id)
        ? "Enter a Server ID."
        : null;

    private string? DisplayNameValidationError => _testValidationAttempted && string.IsNullOrWhiteSpace(_create.Name)
        ? "Enter a Display name."
        : null;

    private string? LocationValidationError
    {
        get
        {
            if (!_testValidationAttempted)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(_create.Location))
            {
                return _create.IsHttp
                    ? "Enter the MCP endpoint URL."
                    : "Enter the STDIO executable.";
            }

            if (_create.IsHttp &&
                (!Uri.TryCreate(_create.Location, UriKind.Absolute, out var endpoint) ||
                 (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)))
            {
                return "Enter an absolute HTTP or HTTPS URL.";
            }

            return null;
        }
    }

    private string TransportHelp => _create.IsHttp
        ? "The gateway sends MCP requests to a remote endpoint. Use HTTPS outside localhost."
        : "The selected process starts without a shell and communicates over standard input/output.";

    private string ExecutionHelp => _create.ExecutionMode == McpExecutionMode.Gateway
        ? "The gateway process owns the connection and supplies configured environment values."
        : "Advanced: each isolated run container owns its connection and resolves the executable inside that container.";

    private string LocationHelp => _create.IsHttp
        ? "Full URL of the MCP HTTP endpoint. Authentication headers come from the gateway environment mappings below."
        : "Executable name or absolute path. No shell is invoked; add each argument separately below.";

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<McpCatalogSection> Logger { get; set; } = null!;

    [Inject]
    private IMcpMetadataDiscoveryService McpMetadataDiscovery { get; set; } = null!;

    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<McpServerDefinition> Servers { get; set; } = [];

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusInvalidFieldId is null)
        {
            return;
        }

        var fieldId = _focusInvalidFieldId;
        _focusInvalidFieldId = null;
        await JavaScript.InvokeVoidAsync("adminUi.focus", fieldId);
    }

    private async Task CreateAsync()
    {
        if (!ConnectionVerified)
        {
            _testStatus = "Test the current connection settings before adding this trusted server.";
            _testStatusKind = "error";
            return;
        }

        string? createdId = null;
        await RunAsync(
            async () => createdId = (await Sender.Send(
                new CreateOrUpdateMcpServerUseCase(_create.ToDefinition()),
                PageCancellationToken)).Id,
            async () =>
            {
                _create = new McpServerEditorModel
                {
                    Enabled = true,
                    ExecutionMode = McpExecutionMode.Gateway
                };
                _discoveredTools = [];
                _testedConnectionFingerprint = null;
                _testStatus = string.Empty;
                await OnChanged.InvokeAsync($"Trusted MCP server {createdId} added.");
            },
            Logger,
            "Adding trusted MCP server…",
            $"Unexpected failure while creating MCP server {_create.Id}.");
    }

    private async Task TestConnectionAsync()
    {
        if (_testingConnection)
        {
            return;
        }

        _testingConnection = true;
        _testStatus = "Testing connection and discovering tools…";
        _testStatusKind = string.Empty;
        _testedConnectionFingerprint = null;
        _discoveredTools = [];
        try
        {
            _testValidationAttempted = true;
            var validationErrors = GetTestSettingErrors();
            if (validationErrors.Count > 0)
            {
                _testStatusKind = "error";
                _focusInvalidFieldId = validationErrors[0].FieldId;
                return;
            }

            _testValidationAttempted = false;
            var definition = _create.ToDefinition();
            var discovered = await McpMetadataDiscovery.DiscoverAsync(
                [new ResolvedMcpServer(definition, [], Required: false)],
                PageCancellationToken);
            var server = discovered.SingleOrDefault(item => string.Equals(item.ServerId, definition.Id, StringComparison.Ordinal));
            if (server?.ServerInfo is null)
            {
                throw GatewayException.InvalidRequest("The MCP server did not return valid server information.");
            }

            _discoveredTools = server.Tools.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
            _testedConnectionFingerprint = ConnectionFingerprint(_create);
            _testStatus = $"Connection verified. {Pluralize(_discoveredTools.Count, "tool")} discovered; select the tools this gateway may expose.";
            _testStatusKind = "success";
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while testing MCP server {McpServerId}.", _create.Id);
            }

            _testStatus = exception is GatewayException
                ? AdminText.Describe(exception)
                : "Unable to connect and read MCP metadata. Check the endpoint or executable, runtime environment, and gateway logs.";
            _testStatusKind = "error";
        }
        finally
        {
            _testingConnection = false;
        }
    }

    private bool IsToolSelected(string toolName) =>
        AdminText.Lines(_create.AvailableTools).Contains(toolName, StringComparer.Ordinal);

    private void ToggleDiscoveredTool(string toolName, ChangeEventArgs args)
    {
        var selected = AdminText.Lines(_create.AvailableTools).ToHashSet(StringComparer.Ordinal);
        if (args.Value is true)
        {
            selected.Add(toolName);
        }
        else
        {
            selected.Remove(toolName);
        }

        _create.AvailableTools = string.Join(Environment.NewLine, selected.OrderBy(name => name, StringComparer.Ordinal));
    }

    private static string ConnectionFingerprint(McpServerEditorModel model) => string.Join(
        '\u001f',
        model.Id,
        model.IsHttp,
        model.ExecutionMode,
        model.Location,
        model.Arguments,
        model.EnvironmentHeaders,
        model.EnvironmentVariables);

    private IReadOnlyList<(string FieldId, string Message)> GetTestSettingErrors()
    {
        var errors = new List<(string FieldId, string Message)>();
        if (ServerIdValidationError is not null)
        {
            errors.Add(("new-server-id", ServerIdValidationError));
        }

        if (DisplayNameValidationError is not null)
        {
            errors.Add(("new-server-name", DisplayNameValidationError));
        }

        if (LocationValidationError is not null)
        {
            errors.Add(("new-server-location", LocationValidationError));
        }

        return errors;
    }

    private static string FormatJson(JsonElement value)
    {
        using var document = JsonDocument.Parse(value.GetRawText());
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Pluralize(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}
