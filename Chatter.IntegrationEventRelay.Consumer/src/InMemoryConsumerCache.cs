using Chatter.CQRS.Events;
using System.Collections.Concurrent;

namespace Chatter.IntegrationEventRelay.Consumer;

public class InMemoryConsumerCache
{
    private readonly ConcurrentDictionary<string, IEvent> _cache = new ConcurrentDictionary<string, IEvent>();

    public void Add(string messageId, IEvent @event)
        => _cache.TryAdd(messageId, @event);

    public IEvent? Get(string messageId)
    {
        if (_cache.TryGetValue(messageId, out var @event))
        {
            return @event;
        }

        return null;
    }

    public int Count => _cache.Count;

    public override string ToString()
    {
        var list = _cache.ToList();
        return string.Join(Environment.NewLine, list);
    }
}
