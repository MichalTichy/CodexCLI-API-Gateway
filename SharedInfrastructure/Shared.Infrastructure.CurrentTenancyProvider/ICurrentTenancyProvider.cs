namespace Shared.Infrastructure.CurrentTenancyProvider;

public interface ICurrentTenancyProvider
{
    Task<string> GetUserTenantAsync();
}
