using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex;

public interface ICodexRunner
{
    Task<CodexRunResult> RunAsync(
        CodexRunRequest request,
        Func<string, CancellationToken, Task>? onText,
        CancellationToken cancellationToken);
}
