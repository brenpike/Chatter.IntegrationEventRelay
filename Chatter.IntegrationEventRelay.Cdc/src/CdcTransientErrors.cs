using System.Data.Common;
using System.Net.Sockets;

namespace Chatter.IntegrationEventRelay.Cdc;

public static class CdcTransientErrors
{
	/// <summary>
	/// True for timeouts, network and I/O errors, database errors the provider reports as transient, and any exception
	/// with a true <c>IsTransient</c> property (such as Azure Service Bus's), including inner exceptions.
	/// </summary>
	public static bool IsTransient(Exception exception) => exception switch
	{
		null => false,
		AggregateException aggregate => aggregate.InnerExceptions.Any(IsTransient),
		TimeoutException or TaskCanceledException or HttpRequestException or SocketException or IOException => true,
		DbException db when db.IsTransient => true,
		_ when exception.GetType().GetProperty("IsTransient")?.GetValue(exception) is true => true,
		_ => IsTransient(exception.InnerException!)
	};
}
