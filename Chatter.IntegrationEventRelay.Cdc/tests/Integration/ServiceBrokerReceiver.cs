using Microsoft.Data.SqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// Receives a change feed's queue as the relay's SQL Service Broker change source does with
    /// Chatter.MessageBrokers.SqlServiceBroker 0.18.0: per message, a transaction on a connection of its own, a
    /// <c>WAITFOR (RECEIVE TOP(1) ...)</c> without a timeout, the body decompressed, <c>END CONVERSATION</c> and a commit. An end
    /// dialog or error message only ends its conversation. Records the ids of the rows whose changes it received.
    /// </summary>
    public sealed class ServiceBrokerReceiver : IAsyncDisposable
    {
        private const string EndDialogType = "http://schemas.microsoft.com/SQL/ServiceBroker/EndDialog";
        private const string ErrorType = "http://schemas.microsoft.com/SQL/ServiceBroker/Error";

        private readonly string _connectionString;
        private readonly string _queue;
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<int> _rowIds = new();
        private readonly object _gate = new();
        private TaskCompletionSource<bool> _release;
        private TaskCompletionSource _holding;
        private Task _run;

        private ServiceBrokerReceiver(string connectionString, string queue)
        {
            _connectionString = connectionString;
            _queue = queue;
        }

        /// <summary>
        /// The ids of the rows whose changes were received and committed, in the order they were committed.
        /// </summary>
        public IReadOnlyList<int> RowIds => _rowIds.ToList();

        public static ServiceBrokerReceiver Start(ServiceBrokerChangeFeed feed)
        {
            var receiver = new ServiceBrokerReceiver(feed.ConnectionString, feed.QualifiedQueue);
            receiver._run = Task.Run(() => receiver.ReceiveUntilStoppedAsync(receiver._stop.Token));
            return receiver;
        }

        /// <summary>
        /// Holds the next change message received, its receive uncommitted, until <see cref="Release"/>. The returned task
        /// completes once a message is held.
        /// </summary>
        public Task HoldNextAsync()
        {
            lock (_gate)
            {
                _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return _holding.Task;
            }
        }

        /// <summary>
        /// Commits the held message, or rolls its receive back so the message is received again, and goes on receiving.
        /// </summary>
        public void Release(bool commit = true)
        {
            TaskCompletionSource<bool> release;
            lock (_gate)
            {
                release = _release;
                _release = null;
            }
            release?.TrySetResult(commit);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            Release(commit: false);
            try
            {
                await _run;
            }
            catch (Exception e) when (_stop.IsCancellationRequested && e is OperationCanceledException or SqlException)
            {
                // INVARIANT: cancelling the pending WAITFOR (RECEIVE ...) is how the receiver stops; its transaction rolls back
            }
        }

        private async Task ReceiveUntilStoppedAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
                await ReceiveOnceAsync(stop);
        }

        private async Task ReceiveOnceAsync(CancellationToken stop)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stop);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(stop);
            var message = await ReceiveAsync(connection, transaction, stop);
            if (message is null)
            {
                await transaction.CommitAsync(stop);
                return;
            }

            var rowIds = message.Type is EndDialogType or ErrorType ? Array.Empty<int>() : ReadRowIds(message.Body);
            if (message.Type is not (EndDialogType or ErrorType) && !await AwaitReleaseAsync())
            {
                await transaction.RollbackAsync(stop);
                return;
            }

            await using (var end = new SqlCommand("END CONVERSATION @handle;", connection, transaction))
            {
                end.Parameters.AddWithValue("@handle", message.Handle);
                await end.ExecuteNonQueryAsync(stop);
            }
            await transaction.CommitAsync(stop);
            foreach (var id in rowIds)
                _rowIds.Enqueue(id);
        }

        private async Task<bool> AwaitReleaseAsync()
        {
            TaskCompletionSource<bool> release;
            lock (_gate)
            {
                release = _release;
                _holding?.TrySetResult();
                _holding = null;
            }
            return release is null || await release.Task;
        }

        private async Task<Message> ReceiveAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken stop)
        {
            await using var receive = new SqlCommand($"""
                WAITFOR (RECEIVE TOP(1) conversation_handle, message_type_name,
                    CASE WHEN SUBSTRING(message_body, 1, 2) = 0x1F8B THEN CAST(DECOMPRESS(message_body) AS varbinary(max)) ELSE message_body END AS message_body
                    FROM {_queue});
                """, connection, transaction) { CommandTimeout = 0 };
            await using var reader = await receive.ExecuteReaderAsync(stop);
            if (!await reader.ReadAsync(stop) || reader.IsDBNull(0))
                return null;
            return new Message(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetSqlBytes(2).Buffer);
        }

        /// <summary>
        /// Reads the row ids from the trigger's message, <c>{"Changes":[{"Inserted":{...},"Deleted":{...}}, ...]}</c> in UTF-16.
        /// </summary>
        private static int[] ReadRowIds(byte[] body)
        {
            if (body is null)
                return Array.Empty<int>();
            using var json = JsonDocument.Parse(Encoding.Unicode.GetString(body));
            if (!json.RootElement.TryGetProperty("Changes", out var changes))
                return Array.Empty<int>();
            return changes.EnumerateArray().Select(ReadRowId).ToArray();
        }

        private static int ReadRowId(JsonElement change)
            => (change.TryGetProperty("Inserted", out var inserted) ? inserted : change.GetProperty("Deleted")).GetProperty("Id").GetInt32();

        private sealed record Message(Guid Handle, string Type, byte[] Body);
    }
}
