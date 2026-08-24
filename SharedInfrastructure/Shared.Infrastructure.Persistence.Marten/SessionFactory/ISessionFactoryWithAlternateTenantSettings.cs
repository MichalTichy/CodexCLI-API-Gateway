using Marten;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;

namespace Shared.Infrastructure.Persistence.Marten.SessionFactory;

public interface ISessionFactoryWithAlternateTenantSettings : IAsyncSessionFactory
{
    Task<IQuerySession> QuerySessionAsync(string? alternateTenantId, UnitOfWork unitOfWork);

    Task<IDocumentSession> OpenSessionAsync(string? alternateTenantId, UnitOfWork unitOfWork);
}
