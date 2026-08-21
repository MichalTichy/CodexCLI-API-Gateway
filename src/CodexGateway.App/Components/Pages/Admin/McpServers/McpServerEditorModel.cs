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

    public McpTransport Transport { get; set; }

    public string Url { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string Location
    {
        get => Transport == McpTransport.Http ? Url : Command;
        set
        {
            if (Transport == McpTransport.Http)
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

    public static McpServerEditorModel From(McpServerDefinition server) => new()
    {
        Id = server.Id,
        Name = server.Name,
        Enabled = server.Enabled,
        ExecutionMode = server.ExecutionMode,
        Transport = server.Transport,
        Url = server.Url ?? string.Empty,
        Command = server.Command ?? string.Empty,
        Arguments = string.Join(Environment.NewLine, server.Arguments ?? []),
        BearerTokenEnvironmentVariable = server.BearerTokenEnvironmentVariable ?? string.Empty,
        EnvironmentVariables = string.Join(Environment.NewLine, server.EnvironmentVariables ?? []),
        AvailableTools = string.Join(Environment.NewLine, server.AvailableTools ?? [])
    };

    public McpServerDefinition ToDefinition()
    {
        var location = Location.Trim();
        return new McpServerDefinition
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            ExecutionMode = ExecutionMode,
            Transport = Transport,
            Url = Transport == McpTransport.Http ? AdminText.NullIfWhiteSpace(location) : null,
            Command = Transport == McpTransport.Stdio ? AdminText.NullIfWhiteSpace(location) : null,
            Arguments = Transport == McpTransport.Stdio ? AdminText.Lines(Arguments) : [],
            BearerTokenEnvironmentVariable = Transport == McpTransport.Http
                ? AdminText.NullIfWhiteSpace(BearerTokenEnvironmentVariable)
                : null,
            EnvironmentVariables = Transport == McpTransport.Stdio ? AdminText.Lines(EnvironmentVariables) : [],
            AvailableTools = AdminText.Lines(AvailableTools)
        };
    }
}
