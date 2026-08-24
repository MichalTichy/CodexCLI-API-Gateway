using System.Data;
using Npgsql;

namespace Shared.Infrastructure.Persistence.Marten.UnitOfWorks;

public class UnitOfWork : IAsyncDisposable
{
    public Guid OwnerId { get; }

    internal NpgsqlConnection? Connection { get; private set; }

    internal NpgsqlTransaction? Transaction { get; private set; }

    internal UnitOfWork(Guid ownerId)
    {
        OwnerId = ownerId;
    }

    public async ValueTask DisposeAsync()
    {
        if (Transaction is not null)
        {
            await Transaction.DisposeAsync();
        }

        if (Connection is not null)
        {
            await Connection.DisposeAsync();
        }
    }

    internal async Task CommitAsync()
    {
        if (Transaction is not null)
        {
            await Transaction.CommitAsync();
        }
    }

    internal async Task EnsureInitializedAsync(
        Func<Task<NpgsqlConnection>> connectionFactory,
        IsolationLevel preferredIsolationLevel)
    {
        if (Connection is not null)
        {
            return;
        }

        Connection = await connectionFactory();
        if (Connection.State != ConnectionState.Open)
        {
            throw new ArgumentException("Created connection is not open.", nameof(connectionFactory));
        }

        Transaction = await Connection.BeginTransactionAsync(preferredIsolationLevel);
    }
}
