using System.Net;
using System.Text.Json;
using BrunoVehicleHire.Api.Auth;
using BrunoVehicleHire.Api.ExceptionHandling;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// End-to-end proof of Story 1.5's global exception handling against a real ASP.NET Core pipeline
/// via <see cref="WebApplicationFactory{TEntryPoint}"/> -- not a mock -- backed by its own ephemeral
/// Testcontainers Postgres instance (matching <see cref="VehicleMigrationTests"/>/
/// <see cref="ApiKeyAuthenticationTests"/>'s pattern), with both <c>ConnectionStrings:Postgres</c>
/// and <c>ApiKey:Key</c> overridden so the real docker-compose Postgres instance and the real dev
/// key are never touched by this test run.
///
/// Uses the "Testing" environment (<see cref="WebHostBuilderExtensions.UseEnvironment"/>) so
/// Program.cs's two diagnostic-only endpoints get mapped, while Story 1.4's global FallbackPolicy
/// still enforces the ApiKey requirement against them like any other endpoint.
/// </summary>
public class GlobalExceptionHandlingTests : IAsyncLifetime
{
    private const string ConfiguredKey = "global-error-handling-test-key";
    private const string ProhibitedInternalDetail = "some internal detail that must never reach the client";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["ApiKey:Key"] = ConfiguredKey,
                });
            });
        });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private HttpRequestMessage AuthenticatedGet(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(ApiKeyDefaults.HeaderName, ConfiguredKey);
        return request;
    }

    [Fact]
    public async Task Get_ThrowDomainRule_ReturnsConflictProblemDetails()
    {
        using var request = AuthenticatedGet("/api/test/throw-domain-rule");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        document.RootElement.GetProperty("type").GetString().Should().Be("urn:bruno:vehicle:year");
        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("Year must be between 1900 and next year.");
    }

    [Fact]
    public async Task Get_ThrowUnhandled_ReturnsInternalServerErrorProblemDetails()
    {
        using var request = AuthenticatedGet("/api/test/throw-unhandled");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        document.RootElement.GetProperty("type").GetString().Should().Be(ProblemTypeUris.UnexpectedError);

        json.Should().NotContain(ProhibitedInternalDetail);
    }

    [Fact]
    public async Task Get_ThrowDomainRule_MissingApiKey_Returns401_FallbackPolicyStillApplies()
    {
        var response = await _client.GetAsync("/api/test/throw-domain-rule");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_ThrowUnhandled_MissingApiKey_Returns401_FallbackPolicyStillApplies()
    {
        var response = await _client.GetAsync("/api/test/throw-unhandled");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
