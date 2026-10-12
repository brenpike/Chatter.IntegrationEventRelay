namespace Chatter.IntegrationEventRelay.Cdc.Reading;

/// <summary>
/// Orders CDC changes as they were committed: by commit LSN, sequence value within the transaction, then operation.
/// </summary>
public readonly record struct CdcChangeOrder(CdcLsn StartLsn, CdcLsn SeqVal, int Operation) : IComparable<CdcChangeOrder>, IComparable
{
	public int CompareTo(CdcChangeOrder other)
	{
		var result = StartLsn.CompareTo(other.StartLsn);
		if (result == 0)
			result = SeqVal.CompareTo(other.SeqVal);
		return result != 0 ? result : Operation.CompareTo(other.Operation);
	}

	public int CompareTo(object? obj) => obj switch
	{
		null => 1,
		CdcChangeOrder other => CompareTo(other),
		_ => throw new ArgumentException($"Object must be of type {nameof(CdcChangeOrder)}.", nameof(obj))
	};
}
