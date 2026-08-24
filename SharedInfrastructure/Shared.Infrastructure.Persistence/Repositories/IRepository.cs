namespace Shared.Infrastructure.Persistence.Repositories;

public interface IRepository<T> : IReadOnlyRepository<T> where T : IItemWithId
{
    Task AddAsync(
        ICollection<T> entities,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task<T> AddAsync(
        T entity,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task UpdateAsync(
        T entity,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task<TResult> GetAndUpdateAsync<TResult>(
        string id,
        Func<T, Task<TResult>> updateMethod,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task DeleteAsync(
        T entity,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task DeleteByIdAsync(
        string id,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task DeleteRangeByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task GetAndUpdateAsync(
        string id,
        Func<T, Task> updateMethod,
        CancellationToken cancellationToken = default,
        string? tenantId = null) =>
        GetAndUpdateAsync(
            id,
            async item =>
            {
                await updateMethod(item);
                return true;
            },
            cancellationToken,
            tenantId);

    Task GetAndUpdateAsync(
        string id,
        Action<T> updateMethod,
        CancellationToken cancellationToken = default,
        string? tenantId = null) =>
        GetAndUpdateAsync(
            id,
            item =>
            {
                updateMethod(item);
                return Task.CompletedTask;
            },
            cancellationToken,
            tenantId);
}
