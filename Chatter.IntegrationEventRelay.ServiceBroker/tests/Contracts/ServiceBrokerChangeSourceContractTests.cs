using Chatter.CQRS.Context;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Chatter.IntegrationEventRelay.Core.Tests.Contracts;
using Chatter.IntegrationEventRelay.ServiceBroker.Handlers;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.ServiceBroker.Tests.Contracts
{
    public class ServiceBrokerChangeSourceContractTests : ChangeSourceContractTests<ServiceBrokerContractAdapter>
    {
    }

    /// <summary>
    /// Stands in for SqlChangeFeed's queue: each captured change is a message that the row event handlers receive, and
    /// that stays on the queue (Chatter rolls back the receive) if its handler throws.
    /// </summary>
    public sealed class ServiceBrokerContractAdapter : IChangeSourceContractAdapter
    {
        private readonly ServiceBrokerBinding<ContractRow> _binding = new("fakedb-ssb", new SourceTable("Event1", Database: "FakeDb"));
        private readonly LinkedList<CapturedChange> _queue = new();

        public IChangeSource Source { get; } = new ServiceBrokerChangeSource("fakedb-ssb");

        public void Capture(CapturedChange change) => _queue.AddLast(change);

        public async Task<bool> DeliverAsync(IRowChangeProcessor<ContractRow> processor, CancellationToken cancellationToken)
        {
            var context = new Mock<IMessageHandlerContext>();
            context.SetupGet(c => c.CancellationToken).Returns(cancellationToken);

            while (_queue.First is { } message)
            {
                try
                {
                    await HandleAsync(message.Value, processor, context.Object);
                }
                catch (Exception)
                {
                    return false;
                }

                _queue.RemoveFirst();
            }

            return true;
        }

        private Task HandleAsync(CapturedChange change, IRowChangeProcessor<ContractRow> processor, IMessageHandlerContext context) => change.Operation switch
        {
            RowOperation.Insert => new RowInsertedEventHandler<ContractRow>(_binding, processor, NullLogger<RowInsertedEventHandler<ContractRow>>.Instance)
                .Handle(new RowInsertedEvent<ContractRow>(change.NewValue), context),
            RowOperation.Update => new RowUpdatedEventHandler<ContractRow>(_binding, processor, NullLogger<RowUpdatedEventHandler<ContractRow>>.Instance)
                .Handle(new RowUpdatedEvent<ContractRow>(change.NewValue, change.OldValue), context),
            RowOperation.Delete => new RowDeletedEventHandler<ContractRow>(_binding, processor, NullLogger<RowDeletedEventHandler<ContractRow>>.Instance)
                .Handle(new RowDeletedEvent<ContractRow>(change.NewValue), context),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
    }
}
