using LuminaFeed.Services.Email;
using Microsoft.Extensions.Logging;

namespace LuminaFeed.Tests;

/// <summary>Captures log entries so tests can assert on level, message and exception.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception), exception));
}

/// <summary>Records what would have been emailed; optionally fails like a dead SMTP server. Safe to share across threads.</summary>
internal sealed class RecordingMailSender : IMailSender
{
    private readonly List<EmailMessage> _sent = [];

    public Exception? FailWith { get; set; }

    public IReadOnlyList<EmailMessage> Sent
    {
        get { lock (_sent) return [.. _sent]; }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWith is not null)
            throw FailWith;

        lock (_sent) _sent.Add(message);
        return Task.CompletedTask;
    }
}
