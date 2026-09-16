using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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
/// spec-6-3's mandatory automated PII proof (Design Notes item 3/4; I/O &amp; Edge-Case Matrix row
/// 3): with Serilog wired as the app's logging provider (<c>Program.cs</c>'s
/// <c>builder.Host.UseSerilog(...)</c>) and <c>UseSerilogRequestLogging()</c> in the pipeline, a
/// real HTTP request that carries a customer's plaintext email (a create-customer POST, mirroring
/// <see cref="CustomersEndpointTests"/>'s pattern) must never surface that email in any log event --
/// neither the rendered message nor any structured property -- captured via
/// <see cref="CapturingLoggerProvider"/> (also used by <see cref="ApiKeyAuthenticationTests"/> for
/// its own "raw key never logged" proof), attached through
/// <c>builder.Host.UseSerilog(..., writeToProviders: true)</c> in <c>Program.cs</c>.
///
/// This is a TDD-first test in the sense spec-6-3 describes: it was written and run BEFORE the
/// Loki/Grafana stack existed, to confirm the current codebase's already-clean logging (no request/
/// response bodies, no raw entity objects logged anywhere -- <c>UseSerilogRequestLogging()</c>'s
/// default output is method/path/status/elapsed-time only) holds under Serilog exactly as it did
/// under the previous default provider, before any Loki sink is even added to the picture.
/// </summary>
public class CustomerLoggingPiiTests : IAsyncLifetime
{
    private const string ConfiguredKey = "customer-logging-pii-test-key";
    private const string PlaintextEmail = "pii-must-not-leak@example.com";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    private readonly ConcurrentBag<string> _capturedLogValues = new();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                    ["ApiKey:Key"] = ConfiguredKey,
                    // spec-6-1: see CustomersEndpointTests -- prevents seed data from being
                    // created and (irrelevantly here) logged during this factory's startup.
                    ["Seed:Enabled"] = "false",
                    // Explicit (matches the appsettings.json default) -- this test only needs the
                    // Console sink; Loki reachability is verified manually per Design Notes item 4.
                    ["Serilog:Loki:Enabled"] = "false",
                });
            });

            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(new CapturingLoggerProvider(_capturedLogValues));
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
    public async Task Post_CreateCustomerWithEmail_NoCapturedLogEventEverContainsThePlaintextEmail()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customers")
        {
            Content = JsonContent.Create(new
            {
                firstName = "Pii",
                lastName = "Guard",
                email = PlaintextEmail,
                phoneNumber = "0821239999",
            }),
        };
        request.Headers.Add(ApiKeyDefaults.HeaderName, ConfiguredKey);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Sanity check the matrix's precondition actually held -- the request truly carried the
        // email and at least one log event (the UseSerilogRequestLogging() request-completion line)
        // was genuinely captured, so a passing assertion below isn't hiding an empty capture list.
        _capturedLogValues.Should().NotBeEmpty();

        _capturedLogValues.Should().NotContain(value => value.Contains(PlaintextEmail, StringComparison.Ordinal));
    }
}
