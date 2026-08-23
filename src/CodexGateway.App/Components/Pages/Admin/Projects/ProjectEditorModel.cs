using System.ComponentModel.DataAnnotations;
using CodexGateway.Logic.Security;
using CodexGateway.Models;

namespace CodexGateway.App.Components.Admin;

internal sealed class ProjectEditorModel
{
    public required string Id { get; init; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    [MaxLength(255)]
    public string? RunnerImage { get; set; }

    public List<ApiKeyAccessEditorModel> ApiKeys { get; init; } = [];

    public static ProjectEditorModel From(
        ProjectDefinition project,
        IReadOnlyList<ApiKeyIdentity> apiKeys,
        IReadOnlyList<McpServerDefinition> servers)
    {
        var configuredAccess = (project.ApiKeyAccess ?? [])
            .ToDictionary(access => access.ApiKeyId, StringComparer.Ordinal);
        return new ProjectEditorModel
        {
            Id = project.Id,
            Name = project.Name,
            Enabled = project.Enabled,
            RunnerImage = project.RunnerImage,
            ApiKeys = apiKeys.Select(key => ApiKeyAccessEditorModel.From(
                key,
                configuredAccess.GetValueOrDefault(key.Id),
                servers)).ToList()
        };
    }

    public ProjectDefinition ToDefinition() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        RunnerImage = RunnerImage,
        ApiKeyAccess = ApiKeys
            .Where(key => key.HasProjectAccess)
            .Select(key => key.ToDefinition())
            .ToList()
    };
}
