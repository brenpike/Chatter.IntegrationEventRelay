using Chatter.CQRS;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;
using Chatter.SqlChangeFeed.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// The SqlChangeFeed objects of source event type <c>T</c> on a table, named as SqlChangeFeed 0.16.0 names them: a trigger
    /// on the table sends each change to service <c>Chatter_Service_T</c> on queue <c>Chatter_Queue_T</c>, and the relay
    /// dead-letters a message it can't process to service <c>Chatter_DeadLetterService_T</c> on queue <c>Chatter_DeadLetterQueue_T</c>.
    /// </summary>
    public sealed record ServiceBrokerChangeFeed(string ConnectionString, string Schema, string Table, string Suffix)
    {
        public string Queue => $"Chatter_Queue_{Suffix}";
        public string Service => $"Chatter_Service_{Suffix}";
        public string DeadLetterQueue => $"Chatter_DeadLetterQueue_{Suffix}";
        public string DeadLetterService => $"Chatter_DeadLetterService_{Suffix}";
        public string Trigger => $"Chatter_ChangeFeedTrigger_{Suffix}";
        public string InstallProcedure => $"Chatter_InstallChangeFeed_{Suffix}";
        public string UninstallProcedure => $"Chatter_UninstallChangeFeed_{Suffix}";

        public string QualifiedQueue => $"{Quote(Schema)}.{Quote(Queue)}";
        public string QualifiedDeadLetterQueue => $"{Quote(Schema)}.{Quote(DeadLetterQueue)}";
        public string QualifiedTrigger => $"{Quote(Schema)}.{Quote(Trigger)}";
        public string QualifiedTable => $"{Quote(Schema)}.{Quote(Table)}";

        /// <summary>
        /// Installs the change feed of <typeparamref name="TSource"/> on <c>[<paramref name="schema"/>].[<paramref name="table"/>]</c>
        /// as the relay's SQL Service Broker change source does, with SqlChangeFeed's migrations.
        /// </summary>
        public static async Task<ServiceBrokerChangeFeed> InstallAsync<TSource>(string connectionString, string table, string schema = "dbo")
            where TSource : class, IMessage, new()
        {
            var builder = new ChatterBuilderCreator(null).Mock;
            var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            builder.AddSqlChangeFeed<TSource>(connectionString, database, table, options => options.WithSchema(schema));
            await builder.Services.BuildServiceProvider().UseChangeFeedSqlMigrationsAsync<TSource>();
            return new ServiceBrokerChangeFeed(connectionString, schema, table, typeof(TSource).Name);
        }

        /// <summary>
        /// Drops the install and uninstall procedures, as when the change feed was installed by hand or by an older version.
        /// </summary>
        public Task DropProceduresAsync()
            => ExecuteAsync($"DROP PROCEDURE {Quote(Schema)}.{Quote(InstallProcedure)}; DROP PROCEDURE {Quote(Schema)}.{Quote(UninstallProcedure)};");

        /// <summary>
        /// Sends <paramref name="body"/> to the dead-letter service, as the relay does with a message it couldn't process.
        /// </summary>
        public async Task SendToDeadLetterServiceAsync(string body)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"""
                DECLARE @handle uniqueidentifier;
                BEGIN DIALOG @handle FROM SERVICE {Quote(DeadLetterService)} TO SERVICE @service ON CONTRACT [//Chatter] WITH ENCRYPTION = OFF;
                SEND ON CONVERSATION @handle MESSAGE TYPE [//Chatter/BrokeredMessage] (@body);
                """, connection);
            command.Parameters.AddWithValue("@service", DeadLetterService);
            command.Parameters.AddWithValue("@body", Encoding.Unicode.GetBytes(body));
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// The number of messages in the queue, read committed: it waits for a receive that hasn't committed.
        /// </summary>
        public Task<int> CountQueueAsync() => ScalarAsync<int>($"SELECT COUNT(*) FROM {QualifiedQueue};");

        public Task<int> CountDeadLetterQueueAsync() => ScalarAsync<int>($"SELECT COUNT(*) FROM {QualifiedDeadLetterQueue};");

        public Task<bool> IsTriggerDisabledAsync() => ScalarAsync<bool>($"SELECT is_disabled FROM sys.triggers WHERE object_id = OBJECT_ID(N'{QualifiedTrigger.Replace("'", "''")}');");

        private async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (T)await command.ExecuteScalarAsync();
        }

        private static string Quote(string name) => SqlServerFixture.TestDatabase.Quote(name);
    }
}
