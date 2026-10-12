namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// Reads CDC metadata and changes from the source database.
/// </summary>
internal interface ICdcQueries
{
	/// <summary>
	/// <c>sys.fn_cdc_get_min_lsn</c>: the oldest LSN still available to the capture instance; <see cref="CdcLsn.Zero"/>
	/// when the capture instance doesn't exist or the caller can't read it.
	/// </summary>
	Task<CdcLsn> GetMinLsnAsync(string captureInstance, CancellationToken cancellationToken);

	/// <summary>
	/// <c>sys.fn_cdc_get_max_lsn</c>: the newest LSN captured in the database; <see cref="CdcLsn.Zero"/> when nothing has been captured.
	/// </summary>
	Task<CdcLsn> GetMaxLsnAsync(CancellationToken cancellationToken);

	/// <summary>
	/// The commit LSN of the last of the next <paramref name="maxTransactions"/> transactions committed from
	/// <paramref name="from"/> up to <paramref name="max"/> (from <c>cdc.lsn_time_mapping</c>), or null when there are none.
	/// </summary>
	Task<CdcLsn?> GetBatchUpperBoundAsync(CdcLsn from, CdcLsn max, int maxTransactions, CancellationToken cancellationToken);

	/// <summary>
	/// Every change of the capture instance committed from <paramref name="from"/> to <paramref name="to"/> (inclusive),
	/// with update before images, in commit order: by commit LSN, sequence value, then operation.
	/// </summary>
	Task<IReadOnlyList<CdcChangeRow>> GetChangesAsync(string captureInstance, CdcLsn from, CdcLsn to, CancellationToken cancellationToken);

	/// <summary>
	/// The server's current offset from UTC, to convert <c>cdc.lsn_time_mapping</c> times, which are in server local time.
	/// </summary>
	Task<TimeSpan> GetServerUtcOffsetAsync(CancellationToken cancellationToken);

	/// <summary>
	/// The names of the columns the capture instance captures.
	/// </summary>
	Task<IReadOnlyList<string>> GetCapturedColumnsAsync(string captureInstance, CancellationToken cancellationToken);
}
