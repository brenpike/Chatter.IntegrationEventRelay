namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// Exclusive ownership of a capture instance, so only one relay instance polls it at a time.
/// </summary>
internal interface ICdcLease : IAsyncDisposable
{
	/// <summary>
	/// Takes the lease if no one else holds it, without waiting.
	/// </summary>
	Task<bool> TryAcquireAsync(CancellationToken cancellationToken);

	/// <summary>
	/// False once the lease is lost, for example because its connection dropped.
	/// </summary>
	Task<bool> IsHeldAsync(CancellationToken cancellationToken);
}

internal interface ICdcLeaseFactory
{
	ICdcLease Create(string resource);
}
