namespace CodexGateway.Logic.Specifications;

internal static class SpecificationQueryableExtensions
{
    public static Task<IReadOnlyList<T>> ToListAsync<T>(
        this IQueryable<T> queryable,
        CancellationToken cancellationToken)
        where T : notnull
    {
        if (IsMartenQuery(queryable))
        {
            return Marten.QueryableExtensions.ToListAsync(queryable, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<T>>(queryable.ToList());
    }

    public static Task<T?> SingleOrDefaultAsync<T>(
        this IQueryable<T> queryable,
        CancellationToken cancellationToken)
        where T : notnull
    {
        if (IsMartenQuery(queryable))
        {
            return Marten.QueryableExtensions.SingleOrDefaultAsync(queryable, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(queryable.SingleOrDefault());
    }

    private static bool IsMartenQuery<T>(IQueryable<T> queryable) =>
        queryable.GetType().Assembly == typeof(Marten.QueryableExtensions).Assembly;
}
