using Chatter.CQRS;
using Chatter.CQRS.Context;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.ServiceBroker.Handlers;

public class RowInsertedEventHandler<TSource> : IMessageHandler<RowInsertedEvent<TSource>>
	where TSource : class, IMessage
{
	private readonly ServiceBrokerBinding<TSource> _binding;
	private readonly IRowChangeProcessor<TSource> _processor;
	private readonly ILogger<RowInsertedEventHandler<TSource>> _logger;

	public RowInsertedEventHandler(ServiceBrokerBinding<TSource> binding, IRowChangeProcessor<TSource> processor, ILogger<RowInsertedEventHandler<TSource>> logger)
	{
		_binding = binding ?? throw new ArgumentNullException(nameof(binding));
		_processor = processor ?? throw new ArgumentNullException(nameof(processor));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public Task Handle(RowInsertedEvent<TSource> message, IMessageHandlerContext context)
	{
		_logger.LogInformation("Handling '{RowChangedEventTypeName}' for source changed event '{SourceEventTypeName}'", nameof(RowInsertedEvent<TSource>), typeof(TSource).Name);
		var change = new RowChange<TSource>(_binding.SourceName, RowOperation.Insert, null, message.Inserted, _binding.Table, Position: null, CommittedAt: null);
		return _processor.ProcessAsync(change, context?.CancellationToken ?? CancellationToken.None);
	}
}
