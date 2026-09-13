namespace CodexGateway.Logic.Codex;

public interface ICodexModelCatalog
{
    Task<IReadOnlyList<CodexModel>> GetModelsAsync(CancellationToken cancellationToken);
}
