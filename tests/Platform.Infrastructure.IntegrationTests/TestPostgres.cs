using Npgsql;
using Testcontainers.PostgreSql;

namespace Platform.Infrastructure.IntegrationTests;

/// <summary>
/// A real, empty Postgres database for one test class. With <c>TEST_POSTGRES</c> set to a
/// server connection string (e.g. a throwaway local server on a Mac without Docker:
/// <c>Host=127.0.0.1;Port=55432;Username=postgres</c>), each instance creates its own database
/// there and drops it afterwards; otherwise it starts a Testcontainers container, as before.
/// </summary>
public sealed class TestPostgres : IAsyncDisposable
{
    private PostgreSqlContainer? _container;
    private string? _serverConnection;
    private string? _database;

    public string ConnectionString { get; private set; } = default!;

    public static async Task<TestPostgres> StartAsync()
    {
        var db = new TestPostgres();
        var server = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(server))
        {
            db._container = new PostgreSqlBuilder().Build();
            await db._container.StartAsync();
            db.ConnectionString = db._container.GetConnectionString();
            return db;
        }
        db._serverConnection = server;
        db._database = "t_" + Guid.NewGuid().ToString("N")[..16];
        await using (var connection = new NpgsqlConnection(server))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{db._database}\";", connection);
            await create.ExecuteNonQueryAsync();
        }
        db.ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = db._database, Pooling = false }.ConnectionString;
        return db;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) { await _container.DisposeAsync(); return; }
        if (_serverConnection is null || _database is null) return;
        await using var connection = new NpgsqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
