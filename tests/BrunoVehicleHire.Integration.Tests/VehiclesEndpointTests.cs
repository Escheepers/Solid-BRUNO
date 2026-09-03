using System.Net;
using System.Text.Json;
using BrunoVehicleHire.Api.Auth;
using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// End-to-end proof of the first real CQRS slice (Story 1.7) against a real ASP.NET Core pipeline
/// via <see cref="WebApplicationFactory{TEntryPoint}"/> -- not a mock -- backed by its own ephemeral
/// Testcontainers Postgres instance (matching <see cref="VehicleMigrationTests"/>/
/// <see cref="ApiKeyAuthenticationTests"/>'s pattern): Angular's future consumer -> ApiClient ->
/// API-key auth -> <c>GetVehiclesQuery</c> -> <c>IVehicleRepository</c> -> PostgreSQL -> a real
/// <c>PagedResult&lt;VehicleDto&gt;</c> response. Covers every backend row of the spec's I/O &amp;
/// Edge-Case Matrix, seeded via <see cref="AppDbContext"/> directly, including a soft-deleted row
/// to prove the global query filter (AD-13) excludes it.
/// </summary>
public class VehiclesEndpointTests : IAsyncLifetime
{
    private const string ConfiguredKey = "vehicles-endpoint-test-key";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

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
                });
            });
        });

        _client = _factory.CreateClient();

        // Ensure the schema exists before any test seeds data -- Program.cs's own
        // Database.Migrate() on startup already does this for the real factory-created host, but
        // being explicit here keeps seeding helpers self-contained and order-independent.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        // Testcontainers may reuse an underlying Postgres container across test method instances
        // in this environment -- each test method gets its own WebApplicationFactory/xUnit test
        // class instance, but not necessarily an empty table, so start every test from a clean
        // slate regardless. IgnoreQueryFilters() so this also clears any already-soft-deleted rows
        // left over from a prior test.
        await dbContext.Vehicles.IgnoreQueryFilters().ExecuteDeleteAsync();
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

    private async Task SeedVehiclesAsync(params Vehicle[] vehicles)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Vehicles.AddRange(vehicles);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_VehiclesExist_ReturnsPagedResultShape()
    {
        await SeedVehiclesAsync(
            Vehicle.Create("CA111111", "Toyota", "Corolla", 2023, 350m),
            Vehicle.Create("CA222222", "Honda", "Civic", 2024, 400m));

        using var request = AuthenticatedGet("/api/vehicles?page=1&pageSize=20");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("items").GetArrayLength().Should().Be(2);
        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("pageSize").GetInt32().Should().Be(20);

        var firstItem = document.RootElement.GetProperty("items")[0];
        firstItem.TryGetProperty("id", out _).Should().BeTrue();
        firstItem.TryGetProperty("registrationNumber", out _).Should().BeTrue();
        firstItem.TryGetProperty("make", out _).Should().BeTrue();
        firstItem.TryGetProperty("model", out _).Should().BeTrue();
        firstItem.TryGetProperty("year", out _).Should().BeTrue();
        firstItem.TryGetProperty("dailyRate", out _).Should().BeTrue();
        firstItem.TryGetProperty("createdDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_SoftDeletedVehicleSeeded_NeverAppearsInItemsOrTotalCount()
    {
        var activeVehicle = Vehicle.Create("CA333333", "Ford", "Focus", 2022, 300m);
        var deletedVehicle = Vehicle.Create("CA444444", "Mazda", "3", 2021, 280m);
        deletedVehicle.SoftDelete();

        await SeedVehiclesAsync(activeVehicle, deletedVehicle);

        // Confirm it's actually persisted with IsDeleted=true before asserting the query filter
        // excludes it -- not just assumed.
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rawIsDeleted = await verifyDbContext.Vehicles
                .IgnoreQueryFilters()
                .Where(v => v.Id == deletedVehicle.Id)
                .Select(v => v.IsDeleted)
                .SingleAsync();
            rawIsDeleted.Should().BeTrue();
        }

        using var request = AuthenticatedGet("/api/vehicles?page=1&pageSize=20");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        var items = document.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("registrationNumber").GetString().Should().Be("CA333333");
    }

    [Theory]
    [InlineData("page=0&pageSize=20")]
    [InlineData("page=1&pageSize=500")]
    public async Task Get_InvalidPagingParameters_Returns400WithFieldNamedInErrors(string queryString)
    {
        using var request = AuthenticatedGet($"/api/vehicles?{queryString}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        document.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Get_SearchMatchesMakeModelOrRegistrationNumber_CaseInsensitivePartialMatch()
    {
        await SeedVehiclesAsync(
            Vehicle.Create("CA555555", "Toyota", "Corolla", 2023, 350m),
            Vehicle.Create("CA666666", "Honda", "Civic", 2024, 400m),
            Vehicle.Create("CA777777", "Nissan", "Toyota-Killer", 2022, 320m));

        using var request = AuthenticatedGet("/api/vehicles?page=1&pageSize=20&search=toyota");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var registrations = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("registrationNumber").GetString())
            .ToList();

        registrations.Should().Contain("CA555555");
        registrations.Should().Contain("CA777777");
        registrations.Should().NotContain("CA666666");
    }

    [Fact]
    public async Task Get_MissingApiKey_Returns401_FallbackPolicyStillApplies()
    {
        var response = await _client.GetAsync("/api/vehicles");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
