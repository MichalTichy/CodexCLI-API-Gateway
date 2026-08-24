using System.Text.Json;

namespace CodexGateway.Logic.Generation.Models;

public sealed record InputMessage(
    GenerationMessageRole Role,
    IReadOnlyList<InputContentPart> Content);
