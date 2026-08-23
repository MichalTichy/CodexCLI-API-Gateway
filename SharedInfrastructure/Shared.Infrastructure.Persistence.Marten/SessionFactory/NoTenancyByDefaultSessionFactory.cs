using Marten;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;

namespace Shared.Infrastructure.Persistence.Marten.SessionFactory;

public class NoTenancyByDefaultSessionFactory(
    IDocumentStore store,
    ICurrentTenancyProvider currentTenancyProvider)
    : SessionFactoryWithAlternateTenantSettings(store, currentTenancyProvider),
        INoTenancyByDefaultSessionFactory
{
    public override Task<IDocumentSession> OpenSessionAsync(
        string? alternateTenantId,
        UnitOfWork unitOfWork) =>
        alternateTenantId is null
            ? OpenSessionAsync(unitOfWork)
            : base.OpenSessionAsync(alternateTenantId, unitOfWork);

    public override Task<IQuerySession> QuerySessionAsync(
        string? alternateTenantId,
        UnitOfWork unitOfWork) =>
        alternateTenantId is null
            ? QuerySessionAsync(unitOfWork)
            : base.QuerySessionAsync(alternateTenantId, unitOfWork);

    public override async Task<IDocumentSession> OpenSessionAsync(UnitOfWork unitOfWork)
    {
        var sessionOptions = await CreateSessionOptionsAsync(null, unitOfWork);
        return await Store.OpenSerializableSessionAsync(sessionOptions);
    }

    public override async Task<IQuerySession> QuerySessionAsync(UnitOfWork unitOfWork)
    {
        var sessionOptions = await CreateSessionOptionsAsync(null, unitOfWork);
        return Store.QuerySession(sessionOptions);
    }
}
