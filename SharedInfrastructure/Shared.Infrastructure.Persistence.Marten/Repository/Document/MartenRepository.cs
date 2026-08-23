using JasperFx;
using JasperFx.Metadata;
using Marten;
using Marten.Linq;
using Polly;
using Shared.Infrastructure.Persistence.Marten.SessionFactory;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace Shared.Infrastructure.Persistence.Marten.Repository.Document;

public class MartenRepository<T>(ISessionFactoryWithAlternateTenantSettings sessionFactory)
    : IRepository<T> where T : IItemWithId
{
    public virtual async Task AddAsync(
        ICollection<T> entities,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.OpenSessionAsync(tenantId, unitOfWork.Get());
        session.Insert(entities.AsEnumerable());
        await SaveChangesAsync(session, cancellationToken);
        await unitOfWork.CommitAsync();
    }

    public virtual async Task<T> AddAsync(
        T entity,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.OpenSessionAsync(tenantId, unitOfWork.Get());
        session.Insert(entity);
        await SaveChangesAsync(session, cancellationToken);
        await unitOfWork.CommitAsync();
        return entity;
    }

    public virtual async Task UpdateAsync(
        T entity,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.OpenSessionAsync(tenantId, unitOfWork.Get());
        session.Update(entity);
        await SaveChangesAsync(session, cancellationToken);
        await unitOfWork.CommitAsync();
    }

    public virtual async Task<TResult> GetAndUpdateAsync<TResult>(
        string id,
        Func<T, Task<TResult>> updateMethod,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.OpenSessionAsync(tenantId, unitOfWork.Get());

        if (default(T) is not IVersioned
            && default(T) is not IRevisioned
            && !session.DocumentStore.Options.FindOrResolveDocumentType(typeof(T)).UseOptimisticConcurrency)
        {
            throw new InvalidOperationException(
                $"Cannot use {nameof(GetAndUpdateAsync)} on entities that do not support optimistic concurrency or versioning!");
        }

        T? entity = default;
        var result = await Policy
            .Handle<ConcurrencyException>()
            .Or<AggregateException>()
            .WaitAndRetryForeverAsync(attempt =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entity is not null)
                {
                    session.Eject(entity);
                }

                return TimeSpan.FromMilliseconds(50);
            })
            .ExecuteAsync(async () =>
            {
                entity = await session.LoadAsync<T>(id, cancellationToken)
                    ?? throw new Exception($"Document with id {id} was not found!");
                var updateResult = await updateMethod(entity);
                session.Update(entity);
                await SaveChangesAsync(session, cancellationToken);
                return updateResult;
            });

        await unitOfWork.CommitAsync();
        return result;
    }

    public virtual async Task DeleteAsync(
        T entity,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.OpenSessionAsync(tenantId, unitOfWork.Get());
        session.Delete(entity);
        await SaveChangesAsync(session, cancellationToken);
        await unitOfWork.CommitAsync();
    }

    public async Task DeleteByIdAsync(
        string id,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.OpenSessionAsync(tenantId, unitOfWork.Get());
        session.Delete<T>(id);
        await SaveChangesAsync(session, cancellationToken);
        await unitOfWork.CommitAsync();
    }

    public async Task DeleteRangeByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.OpenSessionAsync(tenantId, unitOfWork.Get());
        foreach (var id in ids)
        {
            session.Delete<T>(id);
        }

        await SaveChangesAsync(session, cancellationToken);
        await unitOfWork.CommitAsync();
    }

    public virtual async Task<T?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.QuerySessionAsync(tenantId, unitOfWork.Get());
        var result = await session.LoadAsync<T>(id, cancellationToken);
        await unitOfWork.CommitAsync();
        return result;
    }

    public virtual async Task<IReadOnlyList<T>> GetByIdsAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.QuerySessionAsync(tenantId, unitOfWork.Get());
        var result = await session.LoadManyAsync<T>(cancellationToken, ids);
        await unitOfWork.CommitAsync();
        return result;
    }

    public virtual async Task<T?> GetBySpecAsync(
        ISpecification<T> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.QuerySessionAsync(tenantId, unitOfWork.Get());
        var queryable = await PreprocessQueryAsync(session.Query<T>());
        var result = await specification.ApplyAsync(queryable, cancellationToken);
        await unitOfWork.CommitAsync();
        return result;
    }

    public virtual async Task<TResult?> GetBySpecAsync<TResult>(
        ISpecification<T, TResult> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.QuerySessionAsync(tenantId, unitOfWork.Get());
        var queryable = await PreprocessQueryAsync(session.Query<T>());
        var result = await specification.ApplyAsync(queryable, cancellationToken);
        await unitOfWork.CommitAsync();
        return result;
    }

    public virtual async Task<IReadOnlyList<T>> ListAsync(
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.QuerySessionAsync(tenantId, unitOfWork.Get());
        var result = await session.Query<T>().ToListAsync(cancellationToken);
        await unitOfWork.CommitAsync();
        return result;
    }

    public virtual async Task<IReadOnlyList<T>> ListAsync(
        IListSpecification<T> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.QuerySessionAsync(tenantId, unitOfWork.Get());
        var queryable = await PreprocessQueryAsync(session.Query<T>());
        var result = await specification.ApplyAsync(queryable, cancellationToken);
        await unitOfWork.CommitAsync();
        return result;
    }

    public virtual async Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        IListSpecification<T, TResult> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.QuerySessionAsync(tenantId, unitOfWork.Get());
        var queryable = await PreprocessQueryAsync(session.Query<T>());
        var result = await specification.ApplyAsync(queryable, cancellationToken);
        await unitOfWork.CommitAsync();
        return result;
    }

    public virtual async Task<int> CountAsync(
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await using var session = await sessionFactory.QuerySessionAsync(tenantId, unitOfWork.Get());
        var queryable = await PreprocessQueryAsync(session.Query<T>());
        var result = await queryable.CountAsync(cancellationToken);
        await unitOfWork.CommitAsync();
        return result;
    }

    protected virtual Task SaveChangesAsync(
        IDocumentSession session,
        CancellationToken cancellationToken = default) =>
        session.SaveChangesAsync(cancellationToken);

    public virtual Task<IMartenQueryable<T>> PreprocessQueryAsync(IMartenQueryable<T> queryable) =>
        Task.FromResult(queryable);
}
