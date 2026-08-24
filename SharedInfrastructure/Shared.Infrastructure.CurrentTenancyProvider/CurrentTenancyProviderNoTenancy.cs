namespace Shared.Infrastructure.CurrentTenancyProvider;

public class CurrentTenancyProviderNoTenancy : ICurrentTenancyProvider
{
    public const string NoTenancyName = "*DEFAULT*";

    public Task<string> GetUserTenantAsync() => Task.FromResult(NoTenancyName);
}
