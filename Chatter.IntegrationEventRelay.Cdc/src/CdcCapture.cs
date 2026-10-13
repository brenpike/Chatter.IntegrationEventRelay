using Chatter.IntegrationEventRelay.Core.Abstractions;

namespace Chatter.IntegrationEventRelay.Cdc;

/// <summary>
/// A capture instance that a CDC source polls.
/// </summary>
/// <param name="SourceName">The CDC source's name.</param>
/// <param name="CaptureInstance">The capture instance, for example <c>dbo_Event1</c>.</param>
/// <param name="Table">The table the capture instance captures, reported on every change.</param>
/// <param name="SourceEventType">The type changes are read into.</param>
internal sealed record CdcCapture(string SourceName, string CaptureInstance, SourceTable Table, Type SourceEventType, CdcSourceOptions Options)
{
	/// <summary>
	/// The application lock that makes one relay instance the active poller of this capture instance.
	/// </summary>
	/// <remarks>
	/// Application lock names compare as binary, while source and capture instance names are case-insensitive, so the
	/// name is normalized: relay instances that spell them differently still share one lease.
	/// </remarks>
	public string LeaseResource => $"Chatter.IntegrationEventRelay.Cdc:{SourceName.ToUpperInvariant()}:{CaptureInstance.ToUpperInvariant()}";
}
