using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BrunoVehicleHire.Integration.Tests.TestSupport;

/// <summary>
/// A minimal <see cref="ILoggerProvider"/> that captures every formatted log message -- and, since
/// spec-6-3 wired Serilog with <c>writeToProviders: true</c>, every structured property value too --
/// emitted anywhere in the pipeline during a test run. Attached via
/// <c>WebApplicationFactory{Program}.WithWebHostBuilder(b => b.ConfigureLogging(...))</c> so
/// PII/secret-leakage assertions (originally <see cref="ApiKeyAuthenticationTests"/>'s "raw key
/// never logged" check, now also spec-6-3's "customer email never logged" check) run against real
/// end-to-end request handling, not just a handler in isolation.
/// </summary>
public sealed class CapturingLoggerProvider(ConcurrentBag<string> capturedValues) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(capturedValues);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentBag<string> capturedValues) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            capturedValues.Add(formatter(state, exception));

            // Serilog's ILoggerProvider bridge passes the event's structured properties as the
            // TState, implementing IEnumerable<KeyValuePair<string, object>> (the same shape
            // Microsoft.Extensions.Logging's own structured logging uses) -- e.g.
            // UseSerilogRequestLogging()'s RequestPath/StatusCode/Elapsed properties, or any
            // {Placeholder} argument from a handler's own ILogger<T> call. Capturing each value
            // individually (not just the rendered message) proves a PII value can't hide inside a
            // property that the default message template doesn't happen to render.
            if (state is IEnumerable<KeyValuePair<string, object>> properties)
            {
                foreach (var property in properties)
                {
                    if (property.Value is string stringValue)
                    {
                        capturedValues.Add(stringValue);
                    }
                }
            }
        }
    }
}
