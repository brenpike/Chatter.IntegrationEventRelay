using Chatter.CQRS;
using Chatter.CQRS.Context;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.ServiceBroker.Handlers;

public class RowDeletedEventHandler<TSource> : IMessageHandler<RowDeletedEvent<TSource>>
	where TSource : class, IMessage
{
	private readonly ServiceBrokerBinding<TSource> _binding;
	private readonly IRowChangeProcessor<TSource> _processor;
	private readonly ILogger<RowDeletedEventHandler<TSource>> _logger;

	public RowDeletedEventHandler(ServiceBrokerBinding<TSource> binding, IRowChangeProcessor<TSource> processor, ILogger<RowDeletedEventHandler<TSource>> logger)
	{
		_binding = binding ?? throw new ArgumentNullException(nameof(binding));
		_processor = processor ?? throw new ArgumentNullException(nameof(processor));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public Task Handle(RowDeletedEvent<TSource> message, IMessageHandlerContext context)
	{
		_logger.LogInformation("Handling '{RowChangedEventTypeName}' for source changed event '{SourceEventTypeName}'", nameof(RowDeletedEvent<TSource>), typeof(TSource).Name);
		var change = new RowChange<TSource>(_binding.SourceName, RowOperation.Delete, null, message.Deleted, _binding.Table, Position: null, CommittedAt: null);
		return _processor.ProcessAsync(change, context?.CancellationToken ?? CancellationToken.None);
	}
}
