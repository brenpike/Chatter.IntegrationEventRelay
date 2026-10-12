using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Core.Tests.Contracts
{
    /// <summary>
    /// Drives a change source adapter for <see cref="ChangeSourceContractTests{TAdapter}"/>, over a fake of whatever it captures from.
    /// </summary>
    public interface IChangeSourceContractAdapter
    {
        IChangeSource Source { get; }

        /// <summary>
        /// Makes <paramref name="change"/> to the source table, in its own transaction, after any changes captured before.
        /// </summary>
        void Capture(CapturedChange change);

        /// <summary>
        /// Lets the adapter deliver the changes it hasn't acknowledged yet to <paramref name="processor"/>, once.
        /// </summary>
        /// <returns>True if the adapter acknowledged what it delivered; false if it kept it for redelivery.</returns>
        Task<bool> DeliverAsync(IRowChangeProcessor<ContractRow> processor, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A change made to the source table. For deletes, <see cref="NewValue"/> is the deleted row.
    /// </summary>
    public sealed record CapturedChange(RowOperation Operation, ContractRow OldValue, ContractRow NewValue)
    {
        public static CapturedChange Insert(ContractRow row) => new(RowOperation.Insert, null, row);
        public static CapturedChange Update(ContractRow oldValue, ContractRow newValue) => new(RowOperation.Update, oldValue, newValue);
        public static CapturedChange Delete(ContractRow row) => new(RowOperation.Delete, null, row);
    }

    /// <summary>
    /// A row of the source table. A record, so rows an adapter reads back compare by value.
    /// </summary>
    public sealed record ContractRow : ISourceEvent
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
