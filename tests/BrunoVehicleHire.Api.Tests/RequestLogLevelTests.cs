using BrunoVehicleHire.Api.Logging;
using FluentAssertions;
using Serilog.Events;

namespace BrunoVehicleHire.Api.Tests;

/// <summary>
/// <see cref="RequestLogLevel"/> decides the colour a request line gets in Grafana: success/redirect is
/// Information, any 4xx (including a business-rule 409) is Warning, a 5xx or an exception is Error.
/// </summary>
public class RequestLogLevelTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(304)]
    public void SuccessAndRedirect_AreInformation(int statusCode)
    {
        RequestLogLevel.For(statusCode, null).Should().Be(LogEventLevel.Information);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(499)]
    public void ClientErrors_AreWarning(int statusCode)
    {
        RequestLogLevel.For(statusCode, null).Should().Be(LogEventLevel.Warning);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public void ServerErrors_AreError(int statusCode)
    {
        RequestLogLevel.For(statusCode, null).Should().Be(LogEventLevel.Error);
    }

    [Fact]
    public void AnException_IsError_EvenWhenTheStatusLooksFine()
    {
        RequestLogLevel.For(200, new InvalidOperationException("boom")).Should().Be(LogEventLevel.Error);
    }
}
