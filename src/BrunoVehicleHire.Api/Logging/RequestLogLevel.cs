using Serilog.Events;

namespace BrunoVehicleHire.Api.Logging;

/// <summary>
/// The level Serilog's request-logging middleware writes each request line at, so Grafana colours it by
/// outcome: an exception or 5xx is <see cref="LogEventLevel.Error"/> (red), any 4xx is
/// <see cref="LogEventLevel.Warning"/> (amber -- a business-rule 409, a 404, a 400 or a 401 is the API
/// working as designed, but worth seeing and counting), and everything else stays
/// <see cref="LogEventLevel.Information"/>. Serilog's own default would log every status below 500 as
/// Information, which is why a 409 used to show green.
/// </summary>
public static class RequestLogLevel
{
    public static LogEventLevel For(int statusCode, Exception? exception)
    {
        if (exception is not null || statusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        return statusCode >= StatusCodes.Status400BadRequest
            ? LogEventLevel.Warning
            : LogEventLevel.Information;
    }
}
