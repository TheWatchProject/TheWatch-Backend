using Microsoft.Extensions.Logging;
using TheWatch.Core.Logging;

namespace TheWatch.Infrastructure.Logging;

/// <summary>
/// Logger provider that wraps existing loggers with PII redaction.
/// </summary>
public sealed class PiiRedactionLoggerProvider : ILoggerProvider
{
    private readonly ILoggerFactory _innerFactory;
    private readonly PiiRedactionOptions _options;

    public PiiRedactionLoggerProvider(ILoggerFactory innerFactory, PiiRedactionOptions? options = null)
    {
        _innerFactory = innerFactory;
        _options = options ?? new PiiRedactionOptions();
    }

    public ILogger CreateLogger(string categoryName)
    {
        var innerLogger = _innerFactory.CreateLogger(categoryName);
        return new PiiRedactionLogger(innerLogger, _options);
    }

    public void Dispose()
    {
        _innerFactory.Dispose();
    }
}

/// <summary>
/// Logger that automatically redacts PII from log messages.
/// </summary>
public sealed class PiiRedactionLogger : ILogger
{
    private readonly ILogger _innerLogger;
    private readonly PiiRedactionOptions _options;

    public PiiRedactionLogger(ILogger innerLogger, PiiRedactionOptions options)
    {
        _innerLogger = innerLogger;
        _options = options;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (!_options.Enabled || !_options.RedactScopes)
        {
            return _innerLogger.BeginScope(state);
        }

        // Scopes commonly use structured state (IReadOnlyList<KeyValuePair<string, object?>>)
        if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
        {
            var redacted = PiiRedactor.RedactLogState(kvps, _options);
            return _innerLogger.BeginScope(redacted);
        }

        if (state is string s)
        {
            return _innerLogger.BeginScope(PiiRedactor.RedactMessage(s));
        }

        return _innerLogger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _innerLogger.IsEnabled(logLevel);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        if (!_options.Enabled)
        {
            _innerLogger.Log(logLevel, eventId, state, exception, formatter);
            return;
        }

        // Compute a redacted message string from the original formatter.
        var originalMessage = formatter(state, exception);
        var redactedMessage = PiiRedactor.RedactMessage(originalMessage);

        // Avoid passing Exception objects to sinks because many providers emit Exception.ToString().
        Exception? safeException = null;
        IReadOnlyDictionary<string, object?>? exceptionState = null;

        if (exception is not null)
        {
            if (_options.SanitizeExceptions)
            {
                exceptionState = PiiRedactor.SanitizeException(exception, _options);
            }
            else
            {
                safeException = exception;
            }
        }

        // Preserve structured logging while redacting values.
        if (_options.RedactStructuredState && state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
        {
            var redactedState = PiiRedactor.RedactLogState(kvps, _options);

            if (exceptionState is not null)
            {
                var merged = new List<KeyValuePair<string, object?>>(redactedState.Count + exceptionState.Count);
                merged.AddRange(redactedState);
                foreach (var kv in exceptionState)
                {
                    merged.Add(new KeyValuePair<string, object?>(kv.Key, kv.Value));
                }

                _innerLogger.Log(logLevel, eventId, merged, safeException, (s, e) => redactedMessage);
                return;
            }

            _innerLogger.Log(logLevel, eventId, redactedState, safeException, (s, e) => redactedMessage);
            return;
        }

        // Non-structured state: just log the redacted message, plus sanitized exception details if any.
        if (exceptionState is not null)
        {
            var fallbackState = new List<KeyValuePair<string, object?>>
            {
                new("{OriginalFormat}", "{Message}"),
                new("Message", redactedMessage)
            };
            foreach (var kv in exceptionState)
            {
                fallbackState.Add(new KeyValuePair<string, object?>(kv.Key, kv.Value));
            }

            _innerLogger.Log(logLevel, eventId, fallbackState, safeException, (s, e) => redactedMessage);
            return;
        }

        _innerLogger.Log(logLevel, eventId, state, safeException, (s, e) => redactedMessage);
    }
}

/// <summary>
/// Extension methods for adding PII redaction to logging.
/// </summary>
public static class PiiRedactionLoggingExtensions
{
    /// <summary>
    /// Adds PII redaction to the logging pipeline.
    /// </summary>
    public static ILoggingBuilder AddPiiRedaction(this ILoggingBuilder builder)
    {
        // Intentionally no-op.
        // This repo wires PII redaction by installing a single provider that wraps an inner ILoggerFactory.
        // See Program.cs in API/Functions for the configured pipeline.
        return builder;
    }
}
