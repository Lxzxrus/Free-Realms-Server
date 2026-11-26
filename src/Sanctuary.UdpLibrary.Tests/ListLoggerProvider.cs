using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace Sanctuary.UdpLibrary.Tests;

/// <summary>Keeps every message logged through it, formatted, for tests that check what the library logs.</summary>
internal sealed class ListLoggerProvider : ILoggerFactory
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyList<string> Messages => _messages.ToList();

    public ILogger CreateLogger(string categoryName) => new ListLogger(_messages);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class ListLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => messages.Enqueue(formatter(state, exception));
    }
}
