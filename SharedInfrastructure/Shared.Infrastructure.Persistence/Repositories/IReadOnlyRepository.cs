using Shared.Infrastructure.Persistence.Specifications;

namespace Shared.Infrastructure.Persistence.Repositories;

public interface IReadOnlyRepository<T> where T : IItemWithId
{
    Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null);

    Task<IReadOnlyList<T>> GetByIdsAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task<T?> GetBySpecAsync(
        ISpecification<T> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task<TResult?> GetBySpecAsync<TResult>(
        ISpecification<T, TResult> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task<IReadOnlyList<T>> ListAsync(
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task<IReadOnlyList<T>> ListAsync(
        IListSpecification<T> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        IListSpecification<T, TResult> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null);

    Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null);

    async Task<T> GetRequiredByIdAsync(
        string id,
        CancellationToken cancellationToken = default,
        string? tenantId = null,
        Func<Exception>? exceptionFactory = null)
    {
        var result = await GetByIdAsync(id, cancellationToken, tenantId);
        return result
            ?? throw (exceptionFactory?.Invoke()
                      ?? new Exception($"Object {typeof(T).Name} with ID {id} was not found!"));
    }

    async Task<T> GetRequiredBySpecAsync(
        ISpecification<T> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null,
        Func<Exception>? exceptionFactory = null)
    {
        var result = await GetBySpecAsync(specification, cancellationToken, tenantId);
        return result
            ?? throw (exceptionFactory?.Invoke()
                      ?? new Exception($"Object {typeof(T).Name} was not found!"));
    }

    async Task<TResult> GetRequiredBySpecAsync<TResult>(
        ISpecification<T, TResult> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null,
        Func<Exception>? exceptionFactory = null)
    {
        var result = await GetBySpecAsync(specification, cancellationToken, tenantId);
        return result
            ?? throw (exceptionFactory?.Invoke()
                      ?? new Exception($"Object {typeof(T).Name} was not found!"));
    }
}
