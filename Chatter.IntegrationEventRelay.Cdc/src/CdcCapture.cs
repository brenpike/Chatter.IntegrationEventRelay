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
	public string LeaseResource => $"Chatter.IntegrationEventRelay.Cdc:{SourceName}:{CaptureInstance}";
}
