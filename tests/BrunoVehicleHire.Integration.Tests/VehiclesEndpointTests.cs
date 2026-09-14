using System.Net;
using System.Net.Http.Json;
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

    private HttpRequestMessage AuthenticatedPost(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(ApiKeyDefaults.HeaderName, ConfiguredKey);
        return request;
    }

    private HttpRequestMessage AuthenticatedPut(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(ApiKeyDefaults.HeaderName, ConfiguredKey);
        return request;
    }

    private HttpRequestMessage AuthenticatedPost(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
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

    [Fact]
    public async Task GetById_ActiveVehicleExists_Returns200WithFullDtoAndIsDeletedFalse()
    {
        var vehicle = Vehicle.Create("CA131415", "Toyota", "Corolla", 2023, 350m);
        await SeedVehiclesAsync(vehicle);

        using var request = AuthenticatedGet($"/api/vehicles/{vehicle.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("id").GetGuid().Should().Be(vehicle.Id);
        document.RootElement.GetProperty("registrationNumber").GetString().Should().Be("CA131415");
        document.RootElement.GetProperty("make").GetString().Should().Be("Toyota");
        document.RootElement.GetProperty("model").GetString().Should().Be("Corolla");
        document.RootElement.GetProperty("year").GetInt32().Should().Be(2023);
        document.RootElement.GetProperty("dailyRate").GetDecimal().Should().Be(350m);
        document.RootElement.TryGetProperty("createdDate", out _).Should().BeTrue();
        document.RootElement.GetProperty("isDeleted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetById_SoftDeletedVehicleExists_Returns200WithIsDeletedTrue()
    {
        var vehicle = Vehicle.Create("CA161718", "Mazda", "3", 2021, 280m);
        vehicle.SoftDelete();
        await SeedVehiclesAsync(vehicle);

        using var request = AuthenticatedGet($"/api/vehicles/{vehicle.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("id").GetGuid().Should().Be(vehicle.Id);
        document.RootElement.GetProperty("isDeleted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetById_NonexistentVehicleId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedGet($"/api/vehicles/{missingId}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_ValidCommand_Returns201WithVehicleDto_AndVehicleIsThenRetrievableViaGet()
    {
        using var request = AuthenticatedPost("/api/vehicles", new
        {
            registrationNumber = "CA888888",
            make = "Kia",
            model = "Sportage",
            year = 2023,
            dailyRate = 420m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("registrationNumber").GetString().Should().Be("CA888888");
        document.RootElement.GetProperty("make").GetString().Should().Be("Kia");
        document.RootElement.GetProperty("model").GetString().Should().Be("Sportage");
        document.RootElement.GetProperty("year").GetInt32().Should().Be(2023);
        document.RootElement.GetProperty("dailyRate").GetDecimal().Should().Be(420m);
        document.RootElement.TryGetProperty("id", out var idProperty).Should().BeTrue();

        var createdId = idProperty.GetGuid();
        response.Headers.Location!.OriginalString.Should().Be($"/api/vehicles/{createdId}");

        // Prove the write really committed -- a subsequent GET must see it, not merely that the
        // POST response body looked right.
        using var getRequest = AuthenticatedGet("/api/vehicles?page=1&pageSize=20&search=CA888888");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        getDocument.RootElement.GetProperty("items")[0].GetProperty("registrationNumber").GetString()
            .Should().Be("CA888888");
    }

    [Fact]
    public async Task Post_DuplicateRegistrationNumber_Returns409WithExactDetailMessage()
    {
        await SeedVehiclesAsync(Vehicle.Create("CA999999", "Ford", "Ranger", 2022, 500m));

        using var request = AuthenticatedPost("/api/vehicles", new
        {
            registrationNumber = "CA999999",
            make = "Toyota",
            model = "Hilux",
            year = 2022,
            dailyRate = 480m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This registration number is already in use.");
    }

    [Fact]
    public async Task Post_BlankMake_Returns400WithMakeInErrors()
    {
        using var request = AuthenticatedPost("/api/vehicles", new
        {
            registrationNumber = "CA101010",
            make = "   ",
            model = "Corolla",
            year = 2023,
            dailyRate = 350m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty("Make", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_NonPositiveDailyRate_Returns400WithDailyRateInErrors()
    {
        using var request = AuthenticatedPost("/api/vehicles", new
        {
            registrationNumber = "CA202020",
            make = "Toyota",
            model = "Corolla",
            year = 2023,
            dailyRate = 0m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty("DailyRate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_ImplausibleYear_Returns409NotBadRequest_ProvingItsStillDomainEnforced()
    {
        using var request = AuthenticatedPost("/api/vehicles", new
        {
            registrationNumber = "CA303030",
            make = "Toyota",
            model = "Corolla",
            year = DateTime.UtcNow.Year + 5,
            dailyRate = 350m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Put_ValidEdit_Returns200WithUpdatedDto_AndChangeIsRetrievableViaGet()
    {
        var vehicle = Vehicle.Create("CA404040", "Toyota", "Corolla", 2023, 350m);
        await SeedVehiclesAsync(vehicle);

        using var request = AuthenticatedPut($"/api/vehicles/{vehicle.Id}", new
        {
            registrationNumber = "CA404040",
            make = "Toyota",
            model = "Corolla",
            year = 2023,
            dailyRate = 999m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("id").GetGuid().Should().Be(vehicle.Id);
        document.RootElement.GetProperty("dailyRate").GetDecimal().Should().Be(999m);

        // Prove the write really committed -- a subsequent GET must see it too.
        using var getRequest = AuthenticatedGet("/api/vehicles?page=1&pageSize=20&search=CA404040");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("items")[0].GetProperty("dailyRate").GetDecimal().Should().Be(999m);
    }

    [Fact]
    public async Task Put_DuplicateRegistrationNumberAgainstADifferentVehicle_Returns409WithExactDetailMessage()
    {
        var otherVehicle = Vehicle.Create("CA505050", "Ford", "Ranger", 2022, 500m);
        var vehicleBeingEdited = Vehicle.Create("CA606060", "Honda", "Civic", 2021, 300m);
        await SeedVehiclesAsync(otherVehicle, vehicleBeingEdited);

        using var request = AuthenticatedPut($"/api/vehicles/{vehicleBeingEdited.Id}", new
        {
            registrationNumber = "CA505050",
            make = "Honda",
            model = "Civic",
            year = 2021,
            dailyRate = 300m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This registration number is already in use.");
    }

    [Fact]
    public async Task Put_SubmittingVehiclesOwnUnchangedRegistrationNumber_Returns200NotConflict()
    {
        // Regression-proving row: the exclude-self check must not flag a vehicle's own current
        // RegistrationNumber as a duplicate of itself.
        var vehicle = Vehicle.Create("CA707070", "Nissan", "Micra", 2020, 250m);
        await SeedVehiclesAsync(vehicle);

        using var request = AuthenticatedPut($"/api/vehicles/{vehicle.Id}", new
        {
            registrationNumber = "CA707070",
            make = "Nissan",
            model = "Micra",
            year = 2020,
            dailyRate = 275m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("dailyRate").GetDecimal().Should().Be(275m);
    }

    [Fact]
    public async Task Put_NonexistentVehicleId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedPut($"/api/vehicles/{missingId}", new
        {
            registrationNumber = "CA808080",
            make = "Toyota",
            model = "Corolla",
            year = 2023,
            dailyRate = 350m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_BlankMake_Returns400WithMakeInErrors()
    {
        var vehicle = Vehicle.Create("CA909090", "Toyota", "Corolla", 2023, 350m);
        await SeedVehiclesAsync(vehicle);

        using var request = AuthenticatedPut($"/api/vehicles/{vehicle.Id}", new
        {
            registrationNumber = "CA909090",
            make = "   ",
            model = "Corolla",
            year = 2023,
            dailyRate = 350m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty("Make", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_ActiveVehicle_Returns204_AndVehicleDisappearsFromDefaultList()
    {
        var vehicle = Vehicle.Create("CA111213", "Toyota", "Hilux", 2022, 450m);
        await SeedVehiclesAsync(vehicle);

        using var request = AuthenticatedPost($"/api/vehicles/{vehicle.Id}/deactivate");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Prove the write really committed -- a subsequent GET must no longer see it.
        using var getRequest = AuthenticatedGet("/api/vehicles?page=1&pageSize=20&search=CA111213");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
        getDocument.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);

        using var verifyScope = _factory.Services.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var isDeleted = await dbContext.Vehicles
            .IgnoreQueryFilters()
            .Where(v => v.Id == vehicle.Id)
            .Select(v => v.IsDeleted)
            .SingleAsync();
        isDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_NonexistentVehicleId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedPost($"/api/vehicles/{missingId}/deactivate");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deactivate_AlreadyDeactivatedVehicle_Returns404NotSilent204()
    {
        // Idempotency-adjacent: the query filter already excludes a soft-deleted vehicle, so a
        // second deactivate attempt on the same vehicle must 404, not silently succeed again.
        var vehicle = Vehicle.Create("CA141516", "Ford", "Ranger", 2021, 500m);
        vehicle.SoftDelete();
        await SeedVehiclesAsync(vehicle);

        using var request = AuthenticatedPost($"/api/vehicles/{vehicle.Id}/deactivate");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_ShowInactiveTrue_ReturnsBothActiveAndSoftDeletedVehicles_WithIsDeletedFlag()
    {
        var activeVehicle = Vehicle.Create("CA171819", "Toyota", "Yaris", 2023, 300m);
        var deletedVehicle = Vehicle.Create("CA202122", "Mazda", "3", 2021, 280m);
        deletedVehicle.SoftDelete();

        await SeedVehiclesAsync(activeVehicle, deletedVehicle);

        using var request = AuthenticatedGet("/api/vehicles?page=1&pageSize=20&showInactive=true");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);

        var deletedItem = items.Single(item => item.GetProperty("registrationNumber").GetString() == "CA202122");
        deletedItem.GetProperty("isDeleted").GetBoolean().Should().BeTrue();

        var activeItem = items.Single(item => item.GetProperty("registrationNumber").GetString() == "CA171819");
        activeItem.GetProperty("isDeleted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Get_ShowInactiveOmittedOrFalse_StillExcludesSoftDeletedVehicles()
    {
        // Regression: the default (Story 1.7/2.3) behavior must stay exactly as before.
        var activeVehicle = Vehicle.Create("CA232425", "Honda", "Jazz", 2022, 310m);
        var deletedVehicle = Vehicle.Create("CA262728", "Kia", "Rio", 2020, 260m);
        deletedVehicle.SoftDelete();

        await SeedVehiclesAsync(activeVehicle, deletedVehicle);

        using var omittedRequest = AuthenticatedGet("/api/vehicles?page=1&pageSize=20");
        var omittedResponse = await _client.SendAsync(omittedRequest);
        var omittedJson = await omittedResponse.Content.ReadAsStringAsync();
        using var omittedDocument = JsonDocument.Parse(omittedJson);

        omittedDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        omittedDocument.RootElement.GetProperty("items")[0].GetProperty("registrationNumber").GetString()
            .Should().Be("CA232425");

        using var explicitFalseRequest = AuthenticatedGet("/api/vehicles?page=1&pageSize=20&showInactive=false");
        var explicitFalseResponse = await _client.SendAsync(explicitFalseRequest);
        var explicitFalseJson = await explicitFalseResponse.Content.ReadAsStringAsync();
        using var explicitFalseDocument = JsonDocument.Parse(explicitFalseJson);

        explicitFalseDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Restore_SoftDeletedVehicle_Returns204_AndReappearsInDefaultListing()
    {
        var vehicle = Vehicle.Create("CA293031", "Nissan", "Qashqai", 2022, 380m);
        vehicle.SoftDelete();
        await SeedVehiclesAsync(vehicle);

        using var restoreRequest = AuthenticatedPost($"/api/vehicles/{vehicle.Id}/restore");
        var restoreResponse = await _client.SendAsync(restoreRequest);

        restoreResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var inactiveGetRequest =
            AuthenticatedGet($"/api/vehicles?page=1&pageSize=20&showInactive=true&search=CA293031");
        var inactiveGetResponse = await _client.SendAsync(inactiveGetRequest);
        var inactiveJson = await inactiveGetResponse.Content.ReadAsStringAsync();
        using var inactiveDocument = JsonDocument.Parse(inactiveJson);

        inactiveDocument.RootElement.GetProperty("items")[0].GetProperty("isDeleted").GetBoolean()
            .Should().BeFalse();

        using var defaultGetRequest = AuthenticatedGet("/api/vehicles?page=1&pageSize=20&search=CA293031");
        var defaultGetResponse = await _client.SendAsync(defaultGetRequest);
        var defaultJson = await defaultGetResponse.Content.ReadAsStringAsync();
        using var defaultDocument = JsonDocument.Parse(defaultJson);

        defaultDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        defaultDocument.RootElement.GetProperty("items")[0].GetProperty("registrationNumber").GetString()
            .Should().Be("CA293031");
    }

    [Fact]
    public async Task Restore_AlreadyActiveVehicle_Returns409WithExactDetailMessage()
    {
        var vehicle = Vehicle.Create("CA323334", "Subaru", "Forester", 2021, 420m);
        await SeedVehiclesAsync(vehicle);

        using var request = AuthenticatedPost($"/api/vehicles/{vehicle.Id}/restore");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString().Should().Be("Already active.");
    }

    [Fact]
    public async Task Restore_NonexistentVehicleId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedPost($"/api/vehicles/{missingId}/restore");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
