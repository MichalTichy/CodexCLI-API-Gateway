namespace Shared.Infrastructure.Persistence.Marten.UnitOfWorks;

public class UnitOfWorkProvider : IAsyncDisposable
{
    private static readonly AsyncLocal<UnitOfWork> Data = new();
    private readonly Guid _id = Guid.NewGuid();

    public UnitOfWorkProvider()
    {
        Data.Value ??= new UnitOfWork(_id);
    }

    public UnitOfWork Get() => Data.Value!;

    public async Task CommitAsync()
    {
        if (OwnsConnection && Data.Value is not null)
        {
            await Data.Value.CommitAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (OwnsConnection && Data.Value is not null)
        {
            await Data.Value.DisposeAsync();
            Data.Value = null!;
        }
    }

    private bool OwnsConnection => Data.Value?.OwnerId == _id;
}
