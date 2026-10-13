using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", Category.Integration)]
    public class WhenCapturingChangesFromSqlServer
    {
        // CDC's capture job reads the log every few seconds
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(90);

        private readonly SqlServerFixture _sql;
        private readonly string _table = $"Event_{Guid.NewGuid():N}"[..20];
        private readonly Processor _processor = new();

        public WhenCapturingChangesFromSqlServer(SqlServerFixture sql) => _sql = sql;

        [Fact]
        public async Task MustDeliverInsertsUpdatesAndDeletesInCommitOrderWithOldValues()
        {
            var poller = await CreatePollerAsync();
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name, Flag) VALUES (1, N'one', 1);");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET Name = N'uno' WHERE Id = 1;");
            await _sql.ExecuteAsync($"DELETE FROM dbo.[{_table}] WHERE Id = 1;");

            var changes = await PollUntilAsync(poller, 3);

            Assert.Equal(new[] { RowOperation.Insert, RowOperation.Update, RowOperation.Delete }, changes.Select(c => c.Operation));
            Assert.Equal("one", changes[1].OldValue.Name);
            Assert.Equal("uno", changes[1].NewValue.Name);
            Assert.Equal("uno", changes[2].NewValue.Name);
            Assert.All(changes, c => Assert.Equal(_table, c.Table.Name));
            Assert.All(changes, c => Assert.NotNull(c.CommittedAt));
            Assert.True(changes.Select(c => c.Position.Order).SequenceEqual(changes.Select(c => c.Position.Order).OrderBy(o => o)));
        }

        [Fact]
        public async Task MustDeliverTheCommandsOfOneTransactionInOrder()
        {
            var poller = await CreatePollerAsync();
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'a'), (2, N'b');");
            await _sql.ExecuteAsync($"""
                BEGIN TRAN;
                UPDATE dbo.[{_table}] SET Name = N'b1' WHERE Id = 2;
                UPDATE dbo.[{_table}] SET Name = N'a1' WHERE Id = 1;
                UPDATE dbo.[{_table}] SET Name = N'b2' WHERE Id = 2;
                DELETE FROM dbo.[{_table}] WHERE Id = 1;
                INSERT INTO dbo.[{_table}] (Id, Name) VALUES (3, N'c');
                COMMIT;
                """);

            var changes = (await PollUntilAsync(poller, 7)).Skip(2).ToList();

            Assert.Equal(new[] { "b1", "a1", "b2", "a1", "c" }, changes.Select(c => c.NewValue.Name));
            Assert.Equal(new[] { RowOperation.Update, RowOperation.Update, RowOperation.Update, RowOperation.Delete, RowOperation.Insert }, changes.Select(c => c.Operation));
            Assert.Equal(changes.Count, changes.Select(c => c.Position.Value).Distinct().Count());
        }

        [Fact]
        public async Task MustDisallowPartitionSwitchesWhenEnablingCdcOnAPartitionedTable()
        {
            await _sql.ExecuteAsync($"""
                CREATE PARTITION FUNCTION [pf_{_table}] (int) AS RANGE LEFT FOR VALUES (100);
                CREATE PARTITION SCHEME [ps_{_table}] AS PARTITION [pf_{_table}] ALL TO ([PRIMARY]);
                CREATE TABLE dbo.[{_table}_p] (Id int NOT NULL PRIMARY KEY, Name nvarchar(50) NULL) ON [ps_{_table}] (Id);
                """);
            var services = new ServiceCollection();
            services.AddSingleton(new Extensions.CdcMigrationTarget(_sql.ConnectionString, "Chatter", "dbo", $"{_table}_p", $"dbo_{_table}_p"));

            await Extensions.ServiceProviderExtensions.UseCdcSqlMigrationsAsync(services.BuildServiceProvider());

            Assert.False(await _sql.ScalarAsync<bool>($"SELECT partition_switch FROM cdc.change_tables WHERE capture_instance = N'dbo_{_table}_p';"));
            Assert.Equal(new CdcCapturedTable("dbo", $"{_table}_p", Partitioned: true, PartitionSwitchAllowed: false),
                await new SqlCdcQueries(_sql.ConnectionString).GetCapturedTableAsync($"dbo_{_table}_p", CancellationToken.None));
            Assert.True(await _sql.ScalarAsync<bool>("SELECT partition_switch FROM cdc.change_tables WHERE capture_instance = N'dbo_AgentWarmup';"));
        }

        [Fact]
        public async Task MustDeliverASoftDeleteThenRestoreInOrder()
        {
            var poller = await CreatePollerAsync();
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'one');");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET DeletedAt = SYSUTCDATETIME() WHERE Id = 1;");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET DeletedAt = NULL WHERE Id = 1;");

            var updates = (await PollUntilAsync(poller, 3)).Where(c => c.Operation == RowOperation.Update).ToList();

            Assert.Equal(2, updates.Count);
            Assert.True(updates[0].OldValue.DeletedAt is null && updates[0].NewValue.DeletedAt is not null, "first update deletes");
            Assert.True(updates[1].OldValue.DeletedAt is not null && updates[1].NewValue.DeletedAt is null, "second update restores");
        }

        [Fact]
        public async Task MustResumeAfterARestartWithoutLossAndWithTheSamePositions()
        {
            var processor = new Processor { FailOnId = 2 };
            var first = await CreatePollerAsync(processor);
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'one'), (2, N'two'), (3, N'three');");
            await WaitForCaptureAsync(3);

            await Assert.ThrowsAsync<InvalidOperationException>(() => first.PollOnceAsync(CancellationToken.None));
            var attempted = processor.Changes.ToList();
            Assert.Null(await Checkpoints().GetAsync("fakedb-cdc", Capture().CaptureInstance, CancellationToken.None));

            // A new relay instance, as after a crash
            var restarted = new Processor();
            var changes = await PollUntilAsync(await CreatePollerAsync(restarted, createTable: false), 3, restarted);

            Assert.Equal(new[] { 1, 2, 3 }, changes.Select(c => c.NewValue.Id));
            Assert.Equal(attempted.Single().Position, changes[0].Position);
            Assert.NotNull(await Checkpoints().GetAsync("fakedb-cdc", Capture().CaptureInstance, CancellationToken.None));
        }

        [Fact]
        public async Task MustReadBatchesOfAtMostMaxTransactions()
        {
            var processor = new Processor();
            var poller = await CreatePollerAsync(processor, options: new CdcSourceOptions { MaxTransactionsPerBatch = 1 });
            for (var id = 1; id <= 3; id++)
                await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES ({id}, N'row');");
            await WaitForCaptureAsync(3);

            var batches = 0;
            while (processor.Changes.Count < 3 && batches < 10)
            {
                await poller.PollOnceAsync(CancellationToken.None);
                batches++;
            }

            Assert.Equal(new[] { 1, 2, 3 }, processor.Changes.Select(c => c.NewValue.Id));
            Assert.Equal(new[] { 1, 1, 1 }, processor.BatchSizes.Where(s => s > 0));
        }

        [Fact]
        public async Task MustUseTheDefaultForANullColumnOfANonNullableProperty()
        {
            var poller = await CreatePollerAsync();
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name, Flag) VALUES (1, NULL, NULL);");

            var change = Assert.Single(await PollUntilAsync(poller, 1));

            Assert.False(change.NewValue.Flag);
            Assert.Null(change.NewValue.Name);
        }

        [Fact]
        public async Task MustStartAfterTheLatestChangeIfConfigured()
        {
            var processor = new Processor();
            await _sql.CreateCapturedTableAsync(_table);
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'old');");
            await WaitForCaptureAsync(1);

            var poller = await CreatePollerAsync(processor, createTable: false, options: new CdcSourceOptions { StartPosition = CdcStartPosition.Latest });
            await poller.PollOnceAsync(CancellationToken.None);
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (2, N'new');");

            var changes = await PollUntilAsync(poller, 1, processor);

            Assert.Equal(2, Assert.Single(changes).NewValue.Id);
        }

        [Fact]
        public async Task MustRefuseToReuseACaptureInstanceOfAnotherTable()
        {
            var captureInstance = await _sql.CreateCapturedTableAsync(_table);
            await _sql.ExecuteAsync($"CREATE TABLE dbo.[{_table}_2] (Id int NOT NULL PRIMARY KEY);");
            var services = new ServiceCollection();
            services.AddSingleton(new Extensions.CdcMigrationTarget(_sql.ConnectionString, "Chatter", "dbo", $"{_table}_2", captureInstance));

            var e = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => Extensions.ServiceProviderExtensions.UseCdcSqlMigrationsAsync(services.BuildServiceProvider()));

            Assert.Contains("different table", e.Message);
            Assert.Equal(new CdcCapturedTable("dbo", _table), await new SqlCdcQueries(_sql.ConnectionString).GetCapturedTableAsync(captureInstance, CancellationToken.None));
        }

        [Theory]
        [InlineData(CdcErrorPolicy.Halt, false)]
        [InlineData(CdcErrorPolicy.ParkRow, false)]
        [InlineData(CdcErrorPolicy.Halt, true)]
        [InlineData(CdcErrorPolicy.ParkRow, true)]
        public async Task MustRelayUnderExactlyTheGrantsOfTheGrantScript(CdcErrorPolicy errorPolicy, bool gated)
        {
            var gatingRole = gated ? $"gate_{_table}" : null;
            if (gated)
                await CreateGatedCapturedTableAsync();
            else
                await _sql.CreateCapturedTableAsync(_table);
            // A column added after CDC was enabled isn't captured; the relay's login mustn't need to read it
            await _sql.ExecuteAsync($"ALTER TABLE dbo.[{_table}] ADD Secret nvarchar(50) NULL;");
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name, Secret) VALUES (1, N'one', N'secret');");
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (2, N'two');");
            await WaitForCaptureAsync(2);
            // sp_cdc_enable_db grants it to public; the script grants it too, for databases that revoke it. The database is
            // shared by the whole collection, so the grant is restored afterwards.
            await _sql.ExecuteAsync("REVOKE SELECT ON cdc.lsn_time_mapping FROM public;");
            try
            {
                // Under every grant the script makes, the relay relays both changes
                var full = await CreateLoginAsync("full");
                var statements = await GrantStatementsAsync(full.User, errorPolicy, gatingRole);
                await _sql.ExecuteAsync(GrantScript.Render(full.User, $"dbo_{_table}", "Chatter", errorPolicy, gatingRole));
                // Each run's source is named after its login, so it shares no checkpoint or parked change with another
                // run or another case: the relay tables are shared by the whole collection
                Assert.Null(await RunRestrictedAsync(full.ConnectionString, full.User, errorPolicy));

                await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(full.ConnectionString))
                {
                    await connection.OpenAsync();
                    await using var readSecret = new Microsoft.Data.SqlClient.SqlCommand($"SELECT Secret FROM dbo.[{_table}];", connection);
                    Assert.Equal(230, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => readSecret.ExecuteScalarAsync())).Number);
                }

                // Without any one of them, it fails
                Assert.Equal(errorPolicy == CdcErrorPolicy.ParkRow, statements.Any(s => s.Contains(CdcSqlScripts.ParkedChangesTable)));
                Assert.Equal(gated, statements.Any(s => s.StartsWith("ALTER ROLE", StringComparison.Ordinal)));
                for (var i = 0; i < statements.Count; i++)
                {
                    var login = await CreateLoginAsync($"without{i}");
                    foreach (var statement in statements.Where((_, j) => j != i))
                        await _sql.ExecuteAsync(statement.Replace($"[{full.User}]", $"[{login.User}]", StringComparison.Ordinal));

                    var failure = await RunRestrictedAsync(login.ConnectionString, login.User, errorPolicy);

                    Assert.True(failure is not null, $"The relay didn't need: {statements[i]}");
                    // 229 and 230: permission denied on an object or a column
                    Assert.True(failure is CdcCaptureInstanceAccessDeniedException or Microsoft.Data.SqlClient.SqlException { Number: 229 or 230 },
                        $"Without {statements[i]} the relay failed for another reason: {failure}");
                }
            }
            finally
            {
                await _sql.ExecuteAsync("GRANT SELECT ON cdc.lsn_time_mapping TO public;");
            }
        }

        [Fact]
        public async Task MustGrantOnNamesThatQuoteToTheirMaximumLength()
        {
            // 128 characters, mostly ']', which QUOTENAME doubles: each quoted name is about twice as long as the name
            static string Name(string prefix) => (prefix + new string(']', 128))[..128];
            static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";
            static string Literal(string value) => "N'" + value.Replace("'", "''") + "'";
            var schema = Name($"s_{_table}");
            var table = Name($"t_{_table}");
            var user = Name($"u_{_table}");
            var captureInstance = $"q_{_table}";
            await _sql.ExecuteAsync($"CREATE SCHEMA {Quote(schema)};");
            await _sql.ExecuteAsync($"""
                CREATE TABLE {Quote(schema)}.{Quote(table)} (Id int NOT NULL PRIMARY KEY, Name nvarchar(50) NULL);
                EXEC sys.sp_cdc_enable_table @source_schema = {Literal(schema)}, @source_name = {Literal(table)}, @role_name = NULL, @capture_instance = {Literal(captureInstance)};
                CREATE USER {Quote(user)} WITHOUT LOGIN;
                """);

            await _sql.ExecuteAsync(GrantScript.Render(user, captureInstance, "Chatter", CdcErrorPolicy.Halt));

            var granted = $"FROM sys.database_permissions WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID({Literal(user)}) AND state = 'G'";
            Assert.Equal(2, await _sql.ScalarAsync<int>($"SELECT COUNT(*) {granted} AND major_id = OBJECT_ID({Literal(Quote(schema) + "." + Quote(table))}) AND permission_name = 'SELECT';"));
            Assert.Equal(1, await _sql.ScalarAsync<int>($"SELECT COUNT(*) {granted} AND major_id = OBJECT_ID({Literal($"cdc.{Quote(captureInstance + "_CT")}")});"));
        }

        [Fact]
        public async Task MustJoinAGatingRoleOnlyWhenTheOperatorNamesIt()
        {
            await CreateGatedCapturedTableAsync();
            var login = await CreateLoginAsync("gate");
            var ungated = await _sql.CreateCapturedTableAsync($"{_table}_u");

            foreach (var (captureInstance, gatingRole) in new[] { ($"dbo_{_table}", null), ($"dbo_{_table}", "db_datareader"), (ungated, $"gate_{_table}") })
            {
                var e = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => _sql.ExecuteAsync(GrantScript.Render(login.User, captureInstance, "Chatter", CdcErrorPolicy.Halt, gatingRole)));
                Assert.Equal(50000, e.Number);
            }

            Assert.Equal(0, await _sql.ScalarAsync<int>($"SELECT COUNT(*) FROM sys.database_permissions WHERE class = 1 AND grantee_principal_id = DATABASE_PRINCIPAL_ID(N'{login.User}');"));
            Assert.Equal(0, await _sql.ScalarAsync<int>($"SELECT COUNT(*) FROM sys.database_role_members WHERE member_principal_id = DATABASE_PRINCIPAL_ID(N'{login.User}');"));
        }

        [Fact]
        public async Task MustRefuseToGrantToARoleOrABuiltInUser()
        {
            var captureInstance = await _sql.CreateCapturedTableAsync(_table);
            var role = $"role_{_table}";
            await _sql.ExecuteAsync($"CREATE ROLE [{role}];");

            foreach (var principal in new[] { role, "public", "guest", "dbo" })
            {
                var e = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => _sql.ExecuteAsync(GrantScript.Render(principal, captureInstance, "Chatter", CdcErrorPolicy.ParkRow)));
                Assert.Equal(50000, e.Number);
            }
            Assert.Equal(0, await _sql.ScalarAsync<int>($"SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(N'{role}');"));
        }

        // Halt: relays both changes, one batch each, so the checkpoint is inserted and then updated. ParkRow: parks row 2,
        // fails one replay (recording the error) and then replays it (deleting it). Returns the failure, or null.
        private async Task<Exception> RunRestrictedAsync(string connectionString, string sourceName, CdcErrorPolicy errorPolicy)
        {
            var processor = new Processor { FailOnId = errorPolicy == CdcErrorPolicy.ParkRow ? 2 : null };
            var services = new ServiceCollection();
            services.AddSingleton<IRowChangeProcessor<Row>>(processor);
            var capture = Capture(new CdcSourceOptions { ErrorPolicy = errorPolicy, MaxAttemptsBeforePark = 1, MaxTransactionsPerBatch = 1 }) with { SourceName = sourceName };
            var poller = new CdcCapturePoller<Row>(capture, new SqlCdcQueries(connectionString), new SqlCheckpointStore(connectionString, "Chatter"),
                new SqlParkedChangeStore(connectionString, "Chatter"), new SqlAppLockLeaseFactory(connectionString),
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<CdcCapturePoller<Row>>.Instance);
            var parked = $"FROM Chatter.Chatter_RelayParkedChanges WHERE SourceName = N'{sourceName}'";

            try
            {
                if (errorPolicy == CdcErrorPolicy.Halt)
                {
                    for (var poll = 0; poll < 10 && processor.Changes.Count < 2; poll++)
                        await poller.PollOnceAsync(CancellationToken.None);
                    if (!processor.Changes.Select(c => c.NewValue.Id).SequenceEqual(new[] { 1, 2 }))
                        return new Exception($"Relayed rows {string.Join(", ", processor.Changes.Select(c => c.NewValue.Id))}");
                    return null;
                }

                for (var poll = 0; poll < 10 && await _sql.ScalarAsync<int>($"SELECT COUNT(*) {parked};") == 0; poll++)
                    await poller.PollOnceAsync(CancellationToken.None);
                await poller.PollOnceAsync(CancellationToken.None);
                if (!processor.Changes.Select(c => c.NewValue.Id).SequenceEqual(new[] { 1 }) || await _sql.ScalarAsync<int>($"SELECT COUNT(*) {parked};") != 1)
                    return new Exception("Row 2 wasn't parked");

                await _sql.ExecuteAsync($"UPDATE Chatter.Chatter_RelayParkedChanges SET ReplayRequestedAt = SYSUTCDATETIME() WHERE SourceName = N'{sourceName}';");
                await poller.PollOnceAsync(CancellationToken.None);
                if (await _sql.ScalarAsync<int>($"SELECT COUNT(*) {parked} AND Attempts = 2 AND ReplayRequestedAt IS NULL;") != 1)
                    return new Exception("The failed replay wasn't recorded");

                processor.FailOnId = null;
                await _sql.ExecuteAsync($"UPDATE Chatter.Chatter_RelayParkedChanges SET ReplayRequestedAt = SYSUTCDATETIME() WHERE SourceName = N'{sourceName}';");
                await poller.PollOnceAsync(CancellationToken.None);
                if (!processor.Changes.Select(c => c.NewValue.Id).SequenceEqual(new[] { 1, 2 }) || await _sql.ScalarAsync<int>($"SELECT COUNT(*) {parked};") != 0)
                    return new Exception("Row 2 wasn't replayed");
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        private async Task<(string User, string ConnectionString)> CreateLoginAsync(string run)
        {
            var login = $"relay_{_table}_{run}";
            await _sql.ExecuteAsync($"""
                CREATE LOGIN [{login}] WITH PASSWORD = N'Relay!Passw0rd1', CHECK_POLICY = OFF;
                CREATE USER [{login}] FOR LOGIN [{login}];
                """);
            return (login, new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_sql.ConnectionString) { UserID = login, Password = "Relay!Passw0rd1" }.ConnectionString);
        }

        private async Task<IReadOnlyList<string>> GrantStatementsAsync(string user, CdcErrorPolicy errorPolicy, string gatingRole)
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_sql.ConnectionString);
            await connection.OpenAsync();
            await using var command = new Microsoft.Data.SqlClient.SqlCommand(GrantScript.Render(user, $"dbo_{_table}", "Chatter", errorPolicy, gatingRole, whatIf: true), connection);
            await using var reader = await command.ExecuteReaderAsync();
            var statements = new List<string>();
            while (await reader.ReadAsync())
                statements.Add(reader.GetString(0));
            return statements;
        }

        private async Task CreateGatedCapturedTableAsync()
        {
            await _sql.ExecuteAsync($"""
                CREATE TABLE dbo.[{_table}] (Id int NOT NULL PRIMARY KEY, Name nvarchar(50) NULL, Flag bit NULL, DeletedAt datetime2 NULL);
                EXEC sys.sp_cdc_enable_table @source_schema = N'dbo', @source_name = N'{_table}', @role_name = N'gate_{_table}', @capture_instance = N'dbo_{_table}';
                """);
        }

        [Fact]
        public async Task MustFailForAMissingCaptureInstance()
        {
            var queries = new SqlCdcQueries(_sql.ConnectionString);

            Assert.Equal(CdcLsn.Zero, await queries.GetMinLsnAsync("dbo_DoesNotExist", CancellationToken.None));
        }

        [Fact]
        public async Task MustListTheCapturedColumns()
        {
            var captureInstance = await _sql.CreateCapturedTableAsync(_table);

            var columns = await new SqlCdcQueries(_sql.ConnectionString).GetCapturedColumnsAsync(captureInstance, CancellationToken.None);

            Assert.Equal(new[] { "Id", "Name", "Flag", "DeletedAt" }, columns);
            Assert.Equal(new[] { "Id" }, await new SqlCdcQueries(_sql.ConnectionString).GetKeyColumnsAsync(captureInstance, CancellationToken.None));
        }

        [Fact]
        public async Task MustKeepCheckpointsInTheRelaySchema()
        {
            await _sql.CreateCapturedTableAsync(_table, schema: "relay_it");
            var store = new SqlCheckpointStore(_sql.ConnectionString, "relay_it");
            var lsn = CdcLsn.Parse("0x0000002A000001F00003");

            await store.SaveAsync("fakedb-cdc", "dbo_X", lsn, CancellationToken.None);
            await store.SaveAsync("fakedb-cdc", "dbo_X", lsn.Increment(), CancellationToken.None);

            Assert.Equal(lsn.Increment(), await store.GetAsync("fakedb-cdc", "dbo_X", CancellationToken.None));
            await store.SaveAsync("fakedb-cdc", "dbo_X", lsn, CancellationToken.None);
            Assert.Equal(lsn.Increment(), await store.GetAsync("fakedb-cdc", "dbo_X", CancellationToken.None));
            Assert.Equal(lsn.Increment(), await store.GetAsync("FakeDb-CDC", "DBO_x", CancellationToken.None));
            Assert.Equal(1, await _sql.ScalarAsync<int>("SELECT COUNT(*) FROM relay_it.Chatter_RelayCheckpoints WHERE CaptureInstance = N'dbo_X';"));
        }

        [Fact]
        public async Task MustParkAFailingRowAndReplayItOnRequest()
        {
            var processor = new Processor { FailOnId = 2 };
            var poller = await CreatePollerAsync(processor, options: new CdcSourceOptions { ErrorPolicy = CdcErrorPolicy.ParkRow, MaxAttemptsBeforePark = 1 });
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'one'), (2, N'two');");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET Name = N'deux' WHERE Id = 2;");
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (3, N'three');");
            await WaitForCaptureAsync(5);

            await WaitUntilAsync(async () =>
            {
                await poller.PollOnceAsync(CancellationToken.None);
                return processor.Changes.Count >= 2;
            });

            Assert.Equal(new[] { 1, 3 }, processor.Changes.Select(c => c.NewValue.Id));
            var parked = new SqlParkedChangeStore(_sql.ConnectionString, "Chatter");
            var rows = await parked.GetParkedChangesAsync("fakedb-cdc", Capture().CaptureInstance, "[2]", CancellationToken.None);
            Assert.Equal(new[] { "Insert", "Update" }, rows.Select(r => r.Operation));
            Assert.Equal(2, (await parked.GetSummaryAsync("fakedb-cdc", Capture().CaptureInstance, CancellationToken.None)).Count);

            processor.FailOnId = null;
            await _sql.ExecuteAsync($"UPDATE Chatter.Chatter_RelayParkedChanges SET ReplayRequestedAt = SYSUTCDATETIME() WHERE CaptureInstance = N'dbo_{_table}' AND RowKey = N'[2]';");
            await poller.PollOnceAsync(CancellationToken.None);

            Assert.Equal(new[] { 1, 3, 2, 2 }, processor.Changes.Select(c => c.NewValue.Id));
            Assert.Equal("deux", processor.Changes.Last().NewValue.Name);
            Assert.Equal(rows.Select(r => r.Position), processor.Changes.Skip(2).Select(c => c.Position.Value));
            Assert.Empty(await parked.GetParkedRowKeysAsync("fakedb-cdc", Capture().CaptureInstance, CancellationToken.None));
        }

        [Fact]
        public async Task MustKeepAParkedRowsChangesTogetherWhenItsPrimaryKeyChanges()
        {
            var processor = new Processor { FailOnId = 2 };
            var poller = await CreatePollerAsync(processor, options: new CdcSourceOptions { ErrorPolicy = CdcErrorPolicy.ParkRow, MaxAttemptsBeforePark = 1 });
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (2, N'two');");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET Id = 5 WHERE Id = 2;");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET Name = N'five' WHERE Id = 5;");
            await WaitForCaptureAsync(5);

            await WaitUntilAsync(async () =>
            {
                await poller.PollOnceAsync(CancellationToken.None);
                return (await Checkpoints().GetAsync("fakedb-cdc", Capture().CaptureInstance, CancellationToken.None)) is not null;
            });

            Assert.Empty(processor.Changes);
            var parked = await new SqlParkedChangeStore(_sql.ConnectionString, "Chatter").GetParkedChangesAsync("fakedb-cdc", Capture().CaptureInstance, "[2]", CancellationToken.None);
            Assert.Equal(new[] { "Insert", "Delete", "Insert", "Update" }, parked.Select(c => c.Operation));

            processor.FailOnId = null;
            await _sql.ExecuteAsync($"UPDATE Chatter.Chatter_RelayParkedChanges SET ReplayRequestedAt = SYSUTCDATETIME() WHERE CaptureInstance = N'dbo_{_table}' AND RowKey = N'[2]';");
            await poller.PollOnceAsync(CancellationToken.None);

            Assert.Equal(new[] { RowOperation.Insert, RowOperation.Delete, RowOperation.Insert, RowOperation.Update }, processor.Changes.Select(c => c.Operation));
            Assert.Equal("five", processor.Changes.Last().NewValue.Name);
        }

        [Fact]
        public async Task MustRelayTheFirstChangeAfterStartingAtTheLatestChangeOfANewCaptureInstance()
        {
            var processor = new Processor();
            var poller = await CreatePollerAsync(processor, options: new CdcSourceOptions { StartPosition = CdcStartPosition.Latest });
            await poller.PollOnceAsync(CancellationToken.None);
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'first');");

            var changes = await PollUntilAsync(poller, 1, processor);

            Assert.Equal(1, Assert.Single(changes).NewValue.Id);
        }

        [Fact]
        public async Task MustLetOnlyOneRelayInstanceHoldTheLease()
        {
            var resource = $"it:{_table}";
            await using var first = new SqlAppLockLease(_sql.ConnectionString, resource);
            await using var second = new SqlAppLockLease(_sql.ConnectionString, resource);

            Assert.True(await first.TryAcquireAsync(CancellationToken.None));
            Assert.False(await second.TryAcquireAsync(CancellationToken.None));
            Assert.True(await first.IsHeldAsync(CancellationToken.None));
            Assert.False(await second.IsHeldAsync(CancellationToken.None));

            await first.DisposeAsync();

            Assert.True(await second.TryAcquireAsync(CancellationToken.None));
            Assert.True(await second.IsHeldAsync(CancellationToken.None));
        }

        [Fact]
        public async Task MustRunOnePollerAtATimeAcrossInstances()
        {
            var a = new Processor();
            var b = new Processor();
            var pollerA = await CreatePollerAsync(a);
            var pollerB = await CreatePollerAsync(b, createTable: false);
            using var cts = new CancellationTokenSource();
            var runs = new[] { pollerA.RunAsync(cts.Token), pollerB.RunAsync(cts.Token) };

            for (var id = 1; id <= 5; id++)
                await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES ({id}, N'row');");
            await WaitUntilAsync(() => a.Changes.Count + b.Changes.Count >= 5);
            cts.Cancel();
            await Task.WhenAll(runs);

            Assert.True(a.Changes.Count == 0 || b.Changes.Count == 0, "Both instances published");
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, a.Changes.Concat(b.Changes).Select(c => c.NewValue.Id));
            Assert.Contains(CdcPollerState.Standby, new[] { pollerA.State, pollerB.State });
        }

        private CdcCapture Capture(CdcSourceOptions options = null)
        {
            options ??= new CdcSourceOptions();
            options.PollInterval = options.LeaseRetryInterval = options.RetryDelay = TimeSpan.FromMilliseconds(200);
            return new("fakedb-cdc", $"dbo_{_table}", new SourceTable(_table, "dbo", "RelayIntegration"), typeof(Row), options);
        }

        private SqlCheckpointStore Checkpoints() => new(_sql.ConnectionString, "Chatter");

        private async Task<CdcCapturePoller<Row>> CreatePollerAsync(Processor processor = null, bool createTable = true, CdcSourceOptions options = null)
        {
            if (createTable)
                await _sql.CreateCapturedTableAsync(_table);

            var services = new ServiceCollection();
            services.AddSingleton<IRowChangeProcessor<Row>>(processor ?? _processor);

            return new CdcCapturePoller<Row>(Capture(options), new SqlCdcQueries(_sql.ConnectionString), Checkpoints(), new SqlParkedChangeStore(_sql.ConnectionString, "Chatter"), new SqlAppLockLeaseFactory(_sql.ConnectionString),
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<CdcCapturePoller<Row>>.Instance);
        }

        private async Task<IReadOnlyList<RowChange<Row>>> PollUntilAsync(CdcCapturePoller<Row> poller, int count, Processor processor = null)
        {
            processor ??= _processor;
            await WaitUntilAsync(async () =>
            {
                await poller.PollOnceAsync(CancellationToken.None);
                return processor.Changes.Count >= count;
            });
            return processor.Changes.ToList();
        }

        private Task WaitForCaptureAsync(int rows)
            => WaitUntilAsync(async () => await _sql.ScalarAsync<int>($"SELECT COUNT(*) FROM cdc.[dbo_{_table}_CT];") >= rows);

        private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

        private static async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow + CaptureTimeout;
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("CDC didn't capture the changes in time. Is SQL Server Agent running?");
                await Task.Delay(500);
            }
        }

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool Flag { get; set; }
            public DateTime? DeletedAt { get; set; }
        }

        private sealed class Processor : IRowChangeProcessor<Row>
        {
            private readonly ConcurrentQueue<RowChange<Row>> _changes = new();

            public int? FailOnId { get; set; }
            public IReadOnlyCollection<RowChange<Row>> Changes => _changes.ToList();
            public ConcurrentQueue<int> BatchSizes { get; } = new();

            public Task ProcessAsync(RowChange<Row> change, CancellationToken cancellationToken)
            {
                if (change.NewValue.Id == FailOnId)
                    throw new InvalidOperationException("publish failed");
                _changes.Enqueue(change);
                return Task.CompletedTask;
            }

            public async Task ProcessBatchAsync(IReadOnlyList<RowChange<Row>> changes, CancellationToken cancellationToken)
            {
                BatchSizes.Enqueue(changes.Count);
                foreach (var change in changes)
                    await ProcessAsync(change, cancellationToken);
            }
        }
    }
}
