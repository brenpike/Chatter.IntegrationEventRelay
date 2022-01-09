namespace Chatter.IntegrationEventRelay.Core.Mapping;

public record MappingData<TSourceEvent> where TSourceEvent : class, ISourceEvent
{
    public MappingData(TSourceEvent? oldValue, TSourceEvent newValue)
    {
        OldValue = oldValue;
        NewValue = newValue ?? throw new ArgumentNullException(nameof(newValue), "The new value must have a value.");
    }

    public MappingData(TSourceEvent newValue) : this(null, newValue) { }

    /// <summary>
    /// The old value of type <typeparamref name="TSourceEvent"/> that will be set when the source event was triggered by an update. Will be null for inserts and deletes.
    /// </summary>
    public TSourceEvent? OldValue { get; init; }
    /// <summary>
    /// The new value of type <typeparamref name="TSourceEvent"/> that will be set after an insert, update or delete.
    /// </summary>
    public TSourceEvent NewValue { get; init; }
}
