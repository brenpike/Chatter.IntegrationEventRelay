namespace Chatter.IntegrationEventRelay.Core.Abstractions;

/// <summary>
/// Delivery guarantees a change source provides beyond at-least-once delivery.
/// </summary>
[Flags]
public enum ChangeSourceCapabilities
{
	None = 0,
	/// <summary>Changes are delivered in commit order.</summary>
	Ordered = 1,
	/// <summary>The source resumes from a checkpoint after a restart and can replay changes with stable positions.</summary>
	Resumable = 2,
	/// <summary>Each change carries its commit time.</summary>
	CommitTime = 4
}
