namespace CodexGateway.Logic.UseCases.Setup;

public sealed record SetupApiKeyResult(string Id, bool Created, string? Key);
