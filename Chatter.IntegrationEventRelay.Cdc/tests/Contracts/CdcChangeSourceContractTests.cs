using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Cdc.Tests.Fakes;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Chatter.IntegrationEventRelay.Core.Tests.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Contracts
{
    public class CdcChangeSourceContractTests : ChangeSourceContractTests<CdcContractAdapter>
    {
    }

    /// <summary>
    /// Captures changes into an in-memory capture instance; delivering runs one poll of the capture instance's poller,
    /// which acknowledges by saving its checkpoint.
    /// </summary>
    public sealed class CdcContractAdapter : IChangeSourceContractAdapter
    {
        private readonly PollerHarness<ContractRow> _harness = new();

        public IChangeSource Source { get; } = new CdcChangeSource(PollerHarness<ContractRow>.SourceName);

        public void Capture(CapturedChange change)
        {
            switch (change.Operation)
            {
                case RowOperation.Insert:
                    _harness.Queries.Commit((CdcChangeRow.Insert, Columns(change.NewValue)));
                    break;
                case RowOperation.Update:
                    _harness.Queries.Commit((CdcChangeRow.UpdateBefore, Columns(change.OldValue)), (CdcChangeRow.UpdateAfter, Columns(change.NewValue)));
                    break;
                case RowOperation.Delete:
                    _harness.Queries.Commit((CdcChangeRow.Delete, Columns(change.NewValue)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        public async Task<bool> DeliverAsync(IRowChangeProcessor<ContractRow> processor, CancellationToken cancellationToken)
        {
            _harness.Processor = processor;
            var checkpoint = _harness.Checkpoint;
            try
            {
                await _harness.Poller.PollOnceAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
            }

            return _harness.Checkpoint != checkpoint;
        }

        private static System.Collections.Generic.IReadOnlyDictionary<string, object> Columns(ContractRow row)
            => PollerHarness<ContractRow>.Columns(row.Id, row.Name);
    }
}
