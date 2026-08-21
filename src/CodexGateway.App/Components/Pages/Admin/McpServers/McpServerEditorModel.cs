using System.ComponentModel.DataAnnotations;
using CodexGateway.Models;

namespace CodexGateway.App.Components.Admin;

internal sealed class McpServerEditorModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public McpExecutionMode ExecutionMode { get; set; } = McpExecutionMode.Gateway;

    public bool IsHttp { get; set; } = true;

    public string Url { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string Location
    {
        get => IsHttp ? Url : Command;
        set
        {
            if (IsHttp)
            {
                Url = value;
            }
            else
            {
                Command = value;
            }
        }
    }

    public string Arguments { get; set; } = string.Empty;

    public string BearerTokenEnvironmentVariable { get; set; } = string.Empty;

    public string EnvironmentVariables { get; set; } = string.Empty;

    public string AvailableTools { get; set; } = string.Empty;

    public static McpServerEditorModel From(McpServerDefinition server)
    {
        var model = new McpServerEditorModel
        {
            Id = server.Id,
            Name = server.Name,
            Enabled = server.Enabled,
            ExecutionMode = server.ExecutionMode,
            IsHttp = server is HttpMcpServerDefinition,
            AvailableTools = string.Join(Environment.NewLine, server.AvailableTools ?? [])
        };
        if (server is HttpMcpServerDefinition http)
        {
            model.Url = http.Url;
            model.BearerTokenEnvironmentVariable = http.BearerTokenEnvironmentVariable ?? string.Empty;
        }
        else if (server is StdioMcpServerDefinition stdio)
        {
            model.Command = stdio.Command;
            model.Arguments = string.Join(Environment.NewLine, stdio.Arguments ?? []);
            model.EnvironmentVariables = string.Join(Environment.NewLine, stdio.EnvironmentVariables ?? []);
        }

        return model;
    }

    public McpServerDefinition ToDefinition()
    {
        var location = Location.Trim();
        if (IsHttp)
        {
            return new HttpMcpServerDefinition
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                ExecutionMode = ExecutionMode,
                Url = location,
                BearerTokenEnvironmentVariable = AdminText.NullIfWhiteSpace(BearerTokenEnvironmentVariable),
                AvailableTools = AdminText.Lines(AvailableTools)
            };
        }

        return new StdioMcpServerDefinition
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            ExecutionMode = ExecutionMode,
            Command = location,
            Arguments = AdminText.Lines(Arguments),
            EnvironmentVariables = AdminText.Lines(EnvironmentVariables),
            AvailableTools = AdminText.Lines(AvailableTools)
        };
    }
}
