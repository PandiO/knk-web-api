using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;
using knkwebapi_v2.Properties;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// A test that needs a real MySQL (row locks, transactions, unique indexes, triggers — none of
/// which EF InMemory has). Tagged <c>Category=requires-mysql</c> and skipped unless
/// <c>KNK_TEST_MYSQL</c> holds a connection string (no Database=) for a server where the user
/// may create and drop databases named <c>KNK_TEST_MYSQL_DB_PREFIX</c>* (default
/// <c>knk_test_</c>). Creating the ledger triggers with binary logging on also needs SUPER (or
/// log_bin_trust_function_creators=1); set <c>KNK_SKIP_LEDGER_TRIGGERS=true</c> to test without them.
/// <code>
/// KNK_TEST_MYSQL="Server=localhost;User=knk_test;Password=…" \
///   dotnet test Tests/knkwebapi_v2.Tests/knkwebapi_v2.Tests.csproj --filter Category=requires-mysql
/// </code>
/// </summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MySqlTestDatabase.ConnectionVariable)))
        {
            Skip = $"requires-mysql: set {MySqlTestDatabase.ConnectionVariable} to run.";
        }
    }
}

/// <summary>A throwaway database with every migration applied; dropped on dispose.</summary>
public sealed class MySqlTestDatabase : IAsyncLifetime
{
    public const string ConnectionVariable = "KNK_TEST_MYSQL";

    public string ConnectionString { get; private set; } = null!;

    private DbContextOptions<KnKDbContext> _options = null!;
    private string _serverConnection = null!;
    private string _database = null!;

    public bool TriggersSkipped => string.Equals(Environment.GetEnvironmentVariable("KNK_SKIP_LEDGER_TRIGGERS"), "true", StringComparison.OrdinalIgnoreCase);

    public KnKDbContext NewContext() => new(_options);

    public async Task InitializeAsync()
    {
        _serverConnection = Environment.GetEnvironmentVariable(ConnectionVariable) ?? "";
        if (string.IsNullOrWhiteSpace(_serverConnection))
        {
            return; // every test is skipped
        }
        var prefix = Environment.GetEnvironmentVariable("KNK_TEST_MYSQL_DB_PREFIX") ?? "knk_test_";
        _database = prefix + Guid.NewGuid().ToString("N")[..12];
        ConnectionString = new MySqlConnectionStringBuilder(_serverConnection) { Database = _database, AllowUserVariables = true }.ConnectionString;

        // Detect the version on the server connection: the database doesn't exist yet.
        _options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseMySql(ConnectionString, ServerVersion.AutoDetect(_serverConnection))
            .Options;
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_serverConnection) || _database == null)
        {
            return;
        }
        await using var connection = new MySqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{_database}`";
        await command.ExecuteNonQueryAsync();
    }
}
