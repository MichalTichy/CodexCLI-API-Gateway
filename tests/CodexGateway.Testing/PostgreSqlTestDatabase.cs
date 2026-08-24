using Npgsql;
using Testcontainers.PostgreSql;

namespace CodexGateway.Testing;

public static class PostgreSqlTestDatabase
{
    private static readonly Lazy<PostgreSqlContainer> Container = new(
        StartContainer,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static string CreateConnectionString()
    {
        var container = Container.Value;
        var databaseName = "gateway_" + Guid.NewGuid().ToString("N");
        using var connection = new NpgsqlConnection(container.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        command.ExecuteNonQuery();

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;
    }

    private static PostgreSqlContainer StartContainer()
    {
        var container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        container.StartAsync().GetAwaiter().GetResult();
        return container;
    }
}
