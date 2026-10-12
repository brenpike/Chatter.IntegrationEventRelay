namespace Chatter.IntegrationEventRelay.Core.Validation;

/// <summary>
/// A check of the relay's configuration that runs when the host starts. Change source adapters can register their own.
/// </summary>
public interface IRelayStartupCheck
{
	/// <returns>A description of each problem found; empty if none.</returns>
	IEnumerable<string> Validate(IServiceProvider services);
}
