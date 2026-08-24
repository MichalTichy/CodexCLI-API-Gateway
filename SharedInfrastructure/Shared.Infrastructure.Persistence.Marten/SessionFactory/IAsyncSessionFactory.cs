using Marten;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;

namespace Shared.Infrastructure.Persistence.Marten.SessionFactory;

public interface IAsyncSessionFactory
{
    Task<IQuerySession> QuerySessionAsync(UnitOfWork unitOfWork);

    Task<IDocumentSession> OpenSessionAsync(UnitOfWork unitOfWork);
}
