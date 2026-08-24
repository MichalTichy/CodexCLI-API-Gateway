using System.Text.Json;

namespace CodexGateway.Logic.Generation;

public interface IGenerationObserver
{
    ValueTask StartedAsync(GenerationStarted started, CancellationToken cancellationToken);

    ValueTask TextDeltaAsync(string text, CancellationToken cancellationToken);
}
