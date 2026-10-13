using Chatter.IntegrationEventRelay.Cdc.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Testcontainers.MsSql;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// A SQL Server 2022 container with SQL Server Agent running (CDC's capture job needs it) and an empty database.
    /// </summary>
    public sealed class SqlServerFixture : IAsyncLifetime
    {
        public const string Image = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";
        private const string Database = "RelayIntegration";

        private readonly MsSqlContainer _container = new MsSqlBuilder(Image)
            .WithEnvironment("MSSQL_AGENT_ENABLED", "true")
            .Build();

        public string ConnectionString { get; private set; }

        public async Task InitializeAsync()
        {
            await _container.StartAsync();
            await ExecuteAsync(_container.GetConnectionString(), $"CREATE DATABASE [{Database}];");
            ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = Database, TrustServerCertificate = true }.ConnectionString;

            // Enabling CDC on the first table creates the capture and cleanup jobs, which fails while SQL Server Agent
            // is still starting (error 14258); do it once here, retrying, so tests don't race the Agent
            await ExecuteAsync(ConnectionString, "CREATE TABLE dbo.AgentWarmup (Id int NOT NULL PRIMARY KEY);");
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (true)
            {
                try
                {
                    await EnableCdcAsync("AgentWarmup", "Chatter");
                    return;
                }
                catch (SqlException e) when (e.Message.Contains("SQLServerAgent is starting", StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }

        public Task DisposeAsync() => _container.DisposeAsync().AsTask();

        /// <summary>
        /// Creates table <c>dbo.&lt;name&gt;</c>, enables CDC on it as capture instance <c>dbo_&lt;name&gt;</c> and creates the relay tables in <paramref name="schema"/>.
        /// </summary>
        public async Task<string> CreateCapturedTableAsync(string name, string schema = "Chatter")
        {
            await ExecuteAsync(ConnectionString, $"""
                CREATE TABLE dbo.[{name}] (
                    Id int NOT NULL PRIMARY KEY,
                    Name nvarchar(50) NULL,
                    Flag bit NULL,
                    DeletedAt datetime2 NULL
                );
                """);

            await EnableCdcAsync(name, schema);
            return $"dbo_{name}";
        }

        private async Task EnableCdcAsync(string table, string schema)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new CdcMigrationTarget(ConnectionString, schema, "dbo", table, $"dbo_{table}"));
            await services.BuildServiceProvider().UseCdcSqlMigrationsAsync();
        }

        public Task ExecuteAsync(string sql) => ExecuteAsync(ConnectionString, sql);

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (T)await command.ExecuteScalarAsync();
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Creates a database of its own, with SQL Service Broker and CDC enabled and the relay tables in <paramref name="relaySchema"/>,
        /// for a test that relies on database-wide state: CDC's capture job and log scan sessions, or Service Broker. Dispose it to drop it.
        /// </summary>
        public async Task<TestDatabase> CreateDatabaseAsync(string relaySchema = "Chatter")
        {
            var name = $"Switch_{Guid.NewGuid():N}";
            await ExecuteAsync(_container.GetConnectionString(), $"CREATE DATABASE [{name}]; ALTER DATABASE [{name}] SET ENABLE_BROKER;");
            var database = new TestDatabase(_container.GetConnectionString(), name, relaySchema);
            await database.ExecuteAsync("EXEC sys.sp_cdc_enable_db;");
            await database.ExecuteAsync($"DECLARE @schema sysname = N'{relaySchema.Replace("'", "''")}';\n{Sql.CdcSqlScripts.CreateRelayTables}");
            return database;
        }

        /// <summary>
        /// A database created by <see cref="CreateDatabaseAsync"/>.
        /// </summary>
        public sealed class TestDatabase : IAsyncDisposable
        {
            private readonly string _serverConnectionString;

            internal TestDatabase(string serverConnectionString, string name, string relaySchema)
            {
                _serverConnectionString = serverConnectionString;
                Name = name;
                RelaySchema = relaySchema;
                ConnectionString = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = name, TrustServerCertificate = true }.ConnectionString;
            }

            public string Name { get; }
            public string RelaySchema { get; }
            public string ConnectionString { get; }

            /// <summary>
            /// Creates table <c>[<paramref name="schema"/>].[<paramref name="name"/>]</c> and enables CDC on it as capture instance
            /// <paramref name="captureInstance"/> (default <c>&lt;schema&gt;_&lt;name&gt;</c>), which it returns.
            /// </summary>
            public async Task<string> CreateCapturedTableAsync(string name, string schema = "dbo", string captureInstance = null)
            {
                await CreateTableAsync(name, schema);
                return await EnableCdcAsync(name, schema, captureInstance);
            }

            /// <summary>
            /// Creates table <c>[<paramref name="schema"/>].[<paramref name="name"/>]</c>, with an <c>Id</c> primary key and
            /// <c>Name</c>, <c>Flag</c> and <c>DeletedAt</c> columns, and the schema if missing.
            /// </summary>
            public async Task CreateTableAsync(string name, string schema = "dbo")
            {
                await ExecuteAsync($"IF SCHEMA_ID(N'{schema.Replace("'", "''")}') IS NULL EXEC (N'CREATE SCHEMA {Quote(schema).Replace("'", "''")}');");
                await ExecuteAsync($"""
                    CREATE TABLE {Quote(schema)}.{Quote(name)} (
                        Id int NOT NULL PRIMARY KEY,
                        Name nvarchar(50) NULL,
                        Flag bit NULL,
                        DeletedAt datetime2 NULL
                    );
                    """);
            }

            /// <summary>
            /// Enables CDC on table <c>[<paramref name="schema"/>].[<paramref name="table"/>]</c> as capture instance
            /// <paramref name="captureInstance"/> (default <c>&lt;schema&gt;_&lt;table&gt;</c>), which it returns.
            /// </summary>
            public async Task<string> EnableCdcAsync(string table, string schema = "dbo", string captureInstance = null)
            {
                captureInstance ??= $"{schema}_{table}";
                var services = new ServiceCollection();
                services.AddSingleton(new CdcMigrationTarget(ConnectionString, RelaySchema, schema, table, captureInstance));
                await services.BuildServiceProvider().UseCdcSqlMigrationsAsync();
                return captureInstance;
            }

            public Task ExecuteAsync(string sql) => SqlServerFixture.ExecuteAsync(ConnectionString, sql);

            public async Task<T> ScalarAsync<T>(string sql)
            {
                await using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
                return (T)await command.ExecuteScalarAsync();
            }

            public async ValueTask DisposeAsync()
            {
                await ExecuteAsync("EXEC sys.sp_cdc_disable_db;");
                await SqlServerFixture.ExecuteAsync(_serverConnectionString, $"ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}];");
                SqlConnection.ClearPool(new SqlConnection(ConnectionString));
            }

            public static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";
        }
    }

    [CollectionDefinition(Name)]
    public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
    {
        public const string Name = "SQL Server";
    }

    public static class Category
    {
        public const string Integration = "Integration";
    }
}
