namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// Changes parked by the <see cref="CdcErrorPolicy.ParkRow"/> error policy, by source, capture instance and row key.
/// </summary>
internal interface ICdcParkedChangeStore
{
	/// <summary>
	/// The keys of the rows that have parked changes, each mapped to the key its changes are parked under (itself,
	/// unless the row's key changed while it was parked). Later changes to these rows are parked too.
	/// </summary>
	Task<IReadOnlyDictionary<string, string>> GetParkedRowKeysAsync(string sourceName, string captureInstance, CancellationToken cancellationToken);

	/// <summary>
	/// Parks <paramref name="change"/> unless a change at the same position is already parked (a replayed batch).
	/// </summary>
	Task ParkAsync(string sourceName, string captureInstance, ParkedChange change, CancellationToken cancellationToken);

	/// <summary>
	/// The keys of the rows an operator asked to replay.
	/// </summary>
	Task<IReadOnlyList<string>> GetRowKeysToReplayAsync(string sourceName, string captureInstance, CancellationToken cancellationToken);

	/// <summary>
	/// The row's parked changes in the order they were committed.
	/// </summary>
	Task<IReadOnlyList<ParkedChange>> GetParkedChangesAsync(string sourceName, string captureInstance, string rowKey, CancellationToken cancellationToken);

	Task RemoveAsync(long id, CancellationToken cancellationToken);

	/// <summary>
	/// Records that replaying the change failed again, and clears the row's replay request.
	/// </summary>
	Task RecordReplayFailureAsync(string sourceName, string captureInstance, ParkedChange change, string error, CancellationToken cancellationToken);

	/// <summary>
	/// How many changes are parked and when the oldest was parked (UTC).
	/// </summary>
	Task<(int Count, DateTime? OldestParkedAt)> GetSummaryAsync(string sourceName, string captureInstance, CancellationToken cancellationToken);
}

/// <param name="Id">Assigned by the store; 0 for a change not parked yet.</param>
/// <param name="RowKey">The key the change is parked and replayed under: the key of the first parked change of its row.</param>
/// <param name="ChangeRowKey">The key of the row the change changed, when it differs from <paramref name="RowKey"/> (the row's key changed).</param>
/// <param name="Position">The change's <see cref="Core.Abstractions.ChangePosition.Value"/>, kept so republishing uses the same message ids.</param>
/// <param name="Payload">The change's old and new values as JSON.</param>
internal sealed record ParkedChange(long Id, string RowKey, string? ChangeRowKey, CdcLsn Lsn, CdcLsn SeqVal, string Operation, string Position, DateTimeOffset? CommittedAt, string Payload, string Error, int Attempts);
