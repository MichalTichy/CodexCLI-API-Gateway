using Marten;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;

namespace Shared.Infrastructure.Persistence.Marten.SessionFactory;

public class SessionFactoryWithAlternateTenantSettings(
    IDocumentStore store,
    ICurrentTenancyProvider currentTenancyProvider)
    : AppMartenSessionFactory(store, currentTenancyProvider),
        ISessionFactoryWithAlternateTenantSettings
{
    public virtual async Task<IQuerySession> QuerySessionAsync(
        string? alternateTenantId,
        UnitOfWork unitOfWork)
    {
        var tenantId = alternateTenantId ?? await CurrentTenancyProvider.GetUserTenantAsync();
        var sessionOptions = await CreateSessionOptionsAsync(tenantId, unitOfWork);
        return Store.QuerySession(sessionOptions);
    }

    public virtual async Task<IDocumentSession> OpenSessionAsync(
        string? alternateTenantId,
        UnitOfWork unitOfWork)
    {
        var tenantId = alternateTenantId ?? await CurrentTenancyProvider.GetUserTenantAsync();
        var sessionOptions = await CreateSessionOptionsAsync(tenantId, unitOfWork);
        return await Store.OpenSerializableSessionAsync(sessionOptions);
    }
}
