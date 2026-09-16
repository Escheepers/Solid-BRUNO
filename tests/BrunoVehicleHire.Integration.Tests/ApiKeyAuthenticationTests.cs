using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using BrunoVehicleHire.Api.Auth;
using BrunoVehicleHire.Integration.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// End-to-end proof of AD-11 against a real ASP.NET Core pipeline via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> -- not a mock -- backed by its own ephemeral
/// Testcontainers Postgres instance (matching <see cref="VehicleMigrationTests"/>'s pattern), with
/// both <c>ConnectionStrings:Postgres</c> and <c>ApiKey:Key</c> overridden so the real
/// docker-compose Postgres instance and the real dev key are never touched by this test run.
///
/// Covers every row of the spec's I/O &amp; Edge-Case Matrix against <c>HealthController</c>, a
/// real endpoint carrying zero auth attributes, plus the Swagger security-scheme assertion and
/// the "raw key never logged" requirement across the whole pipeline (not just the handler in
/// isolation -- see BrunoVehicleHire.Api.Tests for that unit-level coverage).
/// </summary>
public class ApiKeyAuthenticationTests : IAsyncLifetime
{
    private const string ConfiguredKey = "integration-test-only-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    private readonly ConcurrentBag<string> _capturedLogMessages = new();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Development so the Swagger endpoints (mirrored on the same IsDevelopment() check as
            // the previous MapOpenApi() scaffold) are mapped and assertable.
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["ApiKey:Key"] = ConfiguredKey,
                    // spec-6-1: this factory runs under "Development" below, whose
                    // appsettings.Development.json enables seeding by default -- without this
                    // override every test in this class would get contaminated with ~15
                    // vehicles/customers and ~25+ bookings on startup, breaking exact-count
                    // assertions (Design Notes).
                    ["Seed:Enabled"] = "false",
                });
            });

            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(new CapturingLoggerProvider(_capturedLogMessages));
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

    [Fact]
    public async Task Get_Health_MissingHeader_Returns401()
    {
        var response = await _client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_Health_BlankHeader_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add(ApiKeyDefaults.HeaderName, "   ");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_Health_WrongKey_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add(ApiKeyDefaults.HeaderName, "definitely-the-wrong-key");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_Health_CorrectKey_Returns200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add(ApiKeyDefaults.HeaderName, ConfiguredKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_SwaggerJson_DeclaresApiKeySecuritySchemeAndGlobalRequirement()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var securitySchemes = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");

        securitySchemes.TryGetProperty(ApiKeyDefaults.Scheme, out var apiKeyScheme)
            .Should().BeTrue("the swagger document must declare the ApiKey security scheme");

        apiKeyScheme.GetProperty("type").GetString().Should().Be("apiKey");
        apiKeyScheme.GetProperty("in").GetString().Should().Be("header");
        apiKeyScheme.GetProperty("name").GetString().Should().Be(ApiKeyDefaults.HeaderName);

        document.RootElement.TryGetProperty("security", out var globalSecurity)
            .Should().BeTrue("a global security requirement must be declared, matching real enforcement");
        globalSecurity.GetArrayLength().Should().BeGreaterThan(0);
        globalSecurity[0].EnumerateObject().Select(p => p.Name).Should().Contain(ApiKeyDefaults.Scheme);
    }

    [Fact]
    public async Task AuthenticationFailureAndSuccess_NeverLogTheRawConfiguredOrSubmittedKey()
    {
        const string wrongKey = "definitely-the-wrong-key";

        using var missingHeaderRequest = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        await _client.SendAsync(missingHeaderRequest);

        using var wrongKeyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        wrongKeyRequest.Headers.Add(ApiKeyDefaults.HeaderName, wrongKey);
        await _client.SendAsync(wrongKeyRequest);

        using var correctKeyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        correctKeyRequest.Headers.Add(ApiKeyDefaults.HeaderName, ConfiguredKey);
        await _client.SendAsync(correctKeyRequest);

        _capturedLogMessages.Should().NotContain(m => m.Contains(wrongKey, StringComparison.Ordinal));
        _capturedLogMessages.Should().NotContain(m => m.Contains(ConfiguredKey, StringComparison.Ordinal));
    }
}
