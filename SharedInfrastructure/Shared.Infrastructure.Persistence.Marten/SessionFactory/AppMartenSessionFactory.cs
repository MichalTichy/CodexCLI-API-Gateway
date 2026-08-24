using System.Data;
using Marten;
using Marten.Services;
using Npgsql;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;

namespace Shared.Infrastructure.Persistence.Marten.SessionFactory;

public class AppMartenSessionFactory(
    IDocumentStore store,
    ICurrentTenancyProvider currentTenancyProvider) : IAsyncSessionFactory
{
    protected readonly ICurrentTenancyProvider CurrentTenancyProvider = currentTenancyProvider;
    protected readonly IDocumentStore Store = store;

    public virtual async Task<IQuerySession> QuerySessionAsync(UnitOfWork unitOfWork)
    {
        var tenantId = await CurrentTenancyProvider.GetUserTenantAsync();
        var sessionOptions = await CreateSessionOptionsAsync(tenantId, unitOfWork);
        return Store.QuerySession(sessionOptions);
    }

    public virtual async Task<IDocumentSession> OpenSessionAsync(UnitOfWork unitOfWork)
    {
        var tenantId = await CurrentTenancyProvider.GetUserTenantAsync();
        var sessionOptions = await CreateSessionOptionsAsync(tenantId, unitOfWork);
        return await Store.OpenSerializableSessionAsync(sessionOptions);
    }

    protected virtual async Task<SessionOptions> CreateSessionOptionsAsync(
        string? tenantId,
        UnitOfWork unitOfWork)
    {
        await unitOfWork.EnsureInitializedAsync(GetOpenConnectionAsync, IsolationLevel.ReadCommitted);
        var options = SessionOptions.ForTransaction(unitOfWork.Transaction!);
        options.IsolationLevel = IsolationLevel.ReadCommitted;

        if (tenantId is not null)
        {
            options.TenantId = tenantId;
        }

        return options;
    }

    private async Task<NpgsqlConnection> GetOpenConnectionAsync()
    {
        var connection = Store.Storage.Database.CreateConnection();
        await connection.OpenAsync();
        return connection;
    }
}
