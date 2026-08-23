using Shared.Infrastructure.Persistence.Marten.SessionFactory;
using Shared.Infrastructure.Persistence.Repositories;

namespace Shared.Infrastructure.Persistence.Marten.Repository.Document;

public interface INoTenancyRepository<T> : IRepository<T> where T : IItemWithId;

public interface INoTenancyReadOnlyRepository<T> : IReadOnlyRepository<T> where T : IItemWithId;

public class NoTenancyMartenRepository<T>(INoTenancyByDefaultSessionFactory sessionFactory)
    : MartenRepository<T>(sessionFactory),
        INoTenancyRepository<T>,
        INoTenancyReadOnlyRepository<T> where T : IItemWithId;
