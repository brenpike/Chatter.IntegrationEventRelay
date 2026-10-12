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

            var services = new ServiceCollection();
            services.AddSingleton(new CdcMigrationTarget(ConnectionString, schema, "dbo", name, $"dbo_{name}"));
            await services.BuildServiceProvider().UseCdcSqlMigrationsAsync();
            return $"dbo_{name}";
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
