using System.Text.Json;

namespace CodexGateway.Logic.Generation;

public sealed record InputMessage(
    GenerationMessageRole Role,
    IReadOnlyList<InputContentPart> Content);
