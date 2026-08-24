using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Codex.Models;

public sealed class ConfiguredCodexModelCatalog : ICodexModelCatalog
{
    private readonly IReadOnlyList<CodexModel> _models;

    public ConfiguredCodexModelCatalog(IOptions<CodexOptions> options)
    {
        _models = options.Value.Models
            .Select(model => new CodexModel(
                model.Id,
                model.Name,
                model.SupportedReasoningEfforts,
                model.DefaultReasoningEffort))
            .ToArray();
    }

    public IReadOnlyList<CodexModel> GetModels() => _models;
}
