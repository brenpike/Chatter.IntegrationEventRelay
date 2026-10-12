namespace Chatter.IntegrationEventRelay.Core.Abstractions;

/// <summary>
/// The position of a change in the change source's stream, for sources that have one (for example a CDC log sequence number).
/// </summary>
/// <param name="Value">A string that uniquely identifies the change within its source. Used to build idempotent message ids.</param>
/// <param name="Order">Optional. A value that orders changes within the source.</param>
public sealed record ChangePosition(string Value, IComparable? Order = null)
{
	public string ToMessageId() => Value;

	public override string ToString() => Value;
}
