using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Fakes
{
    internal sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception Exception)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
