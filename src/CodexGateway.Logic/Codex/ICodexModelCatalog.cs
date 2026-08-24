namespace CodexGateway.Logic.Codex;

public interface ICodexModelCatalog
{
    IReadOnlyList<CodexModel> GetModels();
}
