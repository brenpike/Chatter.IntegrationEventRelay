namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// A row returned by <c>cdc.fn_cdc_get_all_changes_&lt;capture_instance&gt;</c>.
/// </summary>
/// <param name="StartLsn">The commit LSN of the change's transaction.</param>
/// <param name="SeqVal">Orders the changes within the transaction. An update's before and after images share it.</param>
/// <param name="Operation">1 delete, 2 insert, 3 update (before image), 4 update (after image).</param>
/// <param name="CommitTime">The transaction's end time from <c>cdc.lsn_time_mapping</c>, in the server's local time.</param>
/// <param name="Columns">The captured column values by column name; <see cref="DBNull"/> is returned as null.</param>
internal sealed record CdcChangeRow(CdcLsn StartLsn, CdcLsn SeqVal, int Operation, DateTime? CommitTime, IReadOnlyDictionary<string, object?> Columns)
{
	public const int Delete = 1;
	public const int Insert = 2;
	public const int UpdateBefore = 3;
	public const int UpdateAfter = 4;
}
