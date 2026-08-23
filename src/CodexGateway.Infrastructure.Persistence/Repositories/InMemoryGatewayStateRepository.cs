using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Infrastructure.Persistence.Repositories;

public sealed class InMemoryGatewayStateRepository : IRepository<GatewayState>
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GatewayState? _state;

    public InMemoryGatewayStateRepository()
    {
    }

    public InMemoryGatewayStateRepository(GatewayState state)
    {
        _state = state;
    }

    public GatewayState State => _state
        ?? throw new InvalidOperationException("The gateway state has not been initialized.");

    public int UpdateCount { get; private set; }

    public async Task AddAsync(
        ICollection<GatewayState> entities,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        foreach (var entity in entities)
        {
            await AddAsync(entity, cancellationToken, tenantId);
        }
    }

    public async Task<GatewayState> AddAsync(
        GatewayState entity,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_state is not null)
            {
                throw new InvalidOperationException($"Document with id {entity.Id} already exists.");
            }

            _state = entity;
            UpdateCount++;
            return entity;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(
        GatewayState entity,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state = entity;
            UpdateCount++;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> GetAndUpdateAsync<TResult>(
        string id,
        Func<GatewayState, Task<TResult>> updateMethod,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = _state is { } current && current.Id == id
                ? current
                : throw new InvalidOperationException($"Document with id {id} was not found.");
            var result = await updateMethod(state);
            UpdateCount++;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DeleteAsync(
        GatewayState entity,
        CancellationToken cancellationToken = default,
        string? tenantId = null) =>
        DeleteByIdAsync(entity.Id, cancellationToken, tenantId);

    public async Task DeleteByIdAsync(
        string id,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_state?.Id == id)
            {
                _state = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteRangeByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        foreach (var id in ids)
        {
            await DeleteByIdAsync(id, cancellationToken, tenantId);
        }
    }

    public async Task<GatewayState?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _state?.Id == id ? _state : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<GatewayState>> GetByIdsAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        var state = await GetByIdAsync(GatewayState.DocumentId, cancellationToken, tenantId);
        return state is not null && ids.Contains(state.Id, StringComparer.Ordinal) ? [state] : [];
    }

    public Task<GatewayState?> GetBySpecAsync(
        ISpecification<GatewayState> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null) =>
        ApplyAsync(specification.ApplyAsync, cancellationToken);

    public Task<TResult?> GetBySpecAsync<TResult>(
        ISpecification<GatewayState, TResult> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null) =>
        ApplyAsync(specification.ApplyAsync, cancellationToken);

    public async Task<IReadOnlyList<GatewayState>> ListAsync(
        CancellationToken cancellationToken = default,
        string? tenantId = null)
    {
        var state = await GetByIdAsync(GatewayState.DocumentId, cancellationToken, tenantId);
        return state is null ? [] : [state];
    }

    public Task<IReadOnlyList<GatewayState>> ListAsync(
        IListSpecification<GatewayState> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null) =>
        ApplyListAsync(specification.ApplyAsync, cancellationToken);

    public Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        IListSpecification<GatewayState, TResult> specification,
        CancellationToken cancellationToken = default,
        string? tenantId = null) =>
        ApplyListAsync(specification.ApplyAsync, cancellationToken);

    public async Task<int> CountAsync(
        CancellationToken cancellationToken = default,
        string? tenantId = null) =>
        await GetByIdAsync(GatewayState.DocumentId, cancellationToken, tenantId) is null ? 0 : 1;

    private async Task<TResult?> ApplyAsync<TResult>(
        Func<IQueryable<GatewayState>, CancellationToken, Task<TResult?>> apply,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await apply(Query(), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<TResult>> ApplyListAsync<TResult>(
        Func<IQueryable<GatewayState>, CancellationToken, Task<IReadOnlyList<TResult>>> apply,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await apply(Query(), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private IQueryable<GatewayState> Query() =>
        _state is null ? Array.Empty<GatewayState>().AsQueryable() : new[] { _state }.AsQueryable();
}
