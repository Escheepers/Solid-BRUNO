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
                    // spec-6-1: this factory runs under "Development" below, whose
                    // appsettings.Development.json enables seeding by default -- without this
                    // override every test in this class would get contaminated with ~15
                    // vehicles/customers and ~25+ bookings on startup, breaking exact-count
                    // assertions (Design Notes).
                    ["Seed:Enabled"] = "false",
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
        // left over from a prior test. Bookings/Customers are deleted first -- the Booking->Vehicle
        // FK (Restrict) would otherwise reject deleting a referenced Vehicle row, mirroring
        // BookingsEndpointTests's exact ordering; this class only started seeding Bookings/Customers
        // for spec-vehicle-deactivate-blocked-by-active-bookings's own guard test.
        await dbContext.Bookings.ExecuteDeleteAsync();
        await dbContext.Customers.IgnoreQueryFilters().ExecuteDeleteAsync();
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

    /// <summary>
    /// Seeds an Active Booking for <paramref name="vehicle"/> against a freshly-seeded Customer
    /// (spec-vehicle-deactivate-blocked-by-active-bookings) -- <c>Booking.Create</c> always starts
    /// Active, matching the exact state that must block deactivation.
    /// </summary>
    private async Task SeedActiveBookingAsync(Vehicle vehicle)
    {
        var customer = Customer.Create("Jane", "Doe", $"jane.{Guid.NewGuid()}@example.com", "0821234567");
        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1400m);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Customers.Add(customer);
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Regression test (spec-search-multi-word): a multi-word search ("Toyota Corolla") used to return
    /// nothing because the whole term was tested against Make, Model and RegistrationNumber separately.
    /// Now each word must match one of those fields, in any order.
    /// </summary>
    [Theory]
    [InlineData("Toyota Corolla")]
    [InlineData("corolla toyota")]
    [InlineData("  toy   coro ")]
    public async Task Get_SearchWithSeveralWords_EveryWordMustMatchMakeModelOrRegistration(string search)
    {
        await SeedVehiclesAsync(
            Vehicle.Create("CA111111", "Toyota", "Corolla", 2022, 400m),
            Vehicle.Create("CA222222", "Toyota", "Hilux", 2022, 500m),
            Vehicle.Create("CA333333", "Honda", "Corolla", 2022, 450m));

        using var request = AuthenticatedGet(
            $"/api/vehicles?page=1&pageSize=20&search={Uri.EscapeDataString(search)}");
        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("registrationNumber").GetString()
            .Should().Be("CA111111");
    }

    [Fact]
    public async Task Get_SearchWithMakeAndPartOfRegistration_MatchesAcrossDifferentFields()
    {
        await SeedVehiclesAsync(
            Vehicle.Create("CA111111", "Toyota", "Corolla", 2022, 400m),
            Vehicle.Create("CA222222", "Toyota", "Hilux", 2022, 500m));

        using var request = AuthenticatedGet("/api/vehicles?page=1&pageSize=20&search=Toyota%20CA2");
        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("model").GetString().Should().Be("Hilux");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Regression test (spec-new-rows-visible-first): lists used to be ordered oldest-first, so a
    /// vehicle created through the UI landed on the LAST page and looked as if nothing had been
    /// created. The list must now be newest-first, so the new row is always on page 1.
    /// </summary>
    [Fact]
    public async Task Get_AfterCreatingANewVehicle_ListsNewestFirst_SoItAppearsOnPageOne()
    {
        await SeedVehiclesAsync(
            Vehicle.Create("OLD00001", "Toyota", "Corolla", 2022, 400m, new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))),
            Vehicle.Create("OLD00002", "Toyota", "Corolla", 2022, 400m, new FixedTimeProvider(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero))),
            Vehicle.Create("OLD00003", "Toyota", "Corolla", 2022, 400m, new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero))));

        using var createRequest = AuthenticatedPost("/api/vehicles", new
        {
            registrationNumber = "NEW99999",
            make = "Honda",
            model = "Civic",
            year = 2024,
            dailyRate = 500m,
        });
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var listRequest = AuthenticatedGet("/api/vehicles?page=1&pageSize=2");
        var listResponse = await _client.SendAsync(listRequest);
        var json = await listResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var registrationNumbers = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("registrationNumber").GetString())
            .ToList();

        registrationNumbers.Should().Equal(
            new[] { "NEW99999", "OLD00003" }, "the list is newest-first, so the just-created vehicle is first");
        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(4);
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

    /// <summary>
    /// Regression test for the adversarial-found bug (spec-search-wildcard-escaping): the raw
    /// <c>search</c> string used to be interpolated straight into the <c>ILIKE</c> pattern, so a
    /// bare <c>"%"</c> became the pattern <c>"%%%"</c> (matches anything) and a bare <c>"_"</c>
    /// became <c>"%_%"</c> (also matches any non-empty string, since <c>_</c> matches any single
    /// character). Both must now match only rows containing that character literally.
    /// </summary>
    [Fact]
    public async Task Get_SearchWithLiteralPercentOrUnderscore_TreatsThemLiterally_NeverAsWildcards()
    {
        var percentVehicle = Vehicle.Create("CA400001", "50% Off Deals", "Corolla", 2023, 350m);
        var underscoreVehicle = Vehicle.Create("CA_400003", "Honda", "Civic", 2024, 400m);
        var decoyVehicle = Vehicle.Create("CAX400003", "Mazda", "3", 2021, 280m);
        var plainVehicle = Vehicle.Create("CA400002", "Toyota", "Corolla", 2023, 350m);

        await SeedVehiclesAsync(percentVehicle, underscoreVehicle, decoyVehicle, plainVehicle);

        // A bare "%" must match only the row with a literal "%" -- not every row (4 seeded here).
        using var percentOnlyRequest =
            AuthenticatedGet($"/api/vehicles?page=1&pageSize=20&search={Uri.EscapeDataString("%")}");
        var percentOnlyResponse = await _client.SendAsync(percentOnlyRequest);
        var percentOnlyJson = await percentOnlyResponse.Content.ReadAsStringAsync();
        using var percentOnlyDocument = JsonDocument.Parse(percentOnlyJson);

        percentOnlyDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        percentOnlyDocument.RootElement.GetProperty("items")[0].GetProperty("registrationNumber").GetString()
            .Should().Be("CA400001");

        // A literal "%" substring match still works as an ordinary substring search.
        using var percentSubstringRequest =
            AuthenticatedGet($"/api/vehicles?page=1&pageSize=20&search={Uri.EscapeDataString("50%")}");
        var percentSubstringResponse = await _client.SendAsync(percentSubstringRequest);
        var percentSubstringJson = await percentSubstringResponse.Content.ReadAsStringAsync();
        using var percentSubstringDocument = JsonDocument.Parse(percentSubstringJson);

        percentSubstringDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        percentSubstringDocument.RootElement.GetProperty("items")[0].GetProperty("registrationNumber").GetString()
            .Should().Be("CA400001");

        // A bare "_" must match only the row with a literal "_" -- not the same-length decoy
        // registration number "CAX400003" that a single-char wildcard would otherwise also match.
        using var underscoreOnlyRequest =
            AuthenticatedGet($"/api/vehicles?page=1&pageSize=20&search={Uri.EscapeDataString("_")}");
        var underscoreOnlyResponse = await _client.SendAsync(underscoreOnlyRequest);
        var underscoreOnlyJson = await underscoreOnlyResponse.Content.ReadAsStringAsync();
        using var underscoreOnlyDocument = JsonDocument.Parse(underscoreOnlyJson);

        underscoreOnlyDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        underscoreOnlyDocument.RootElement.GetProperty("items")[0].GetProperty("registrationNumber").GetString()
            .Should().Be("CA_400003");

        // The full literal registration number containing "_" must match itself exactly, and must
        // NOT also match the decoy "CAX400003" (same length, "_" replaced with a real character).
        using var underscoreFullRequest =
            AuthenticatedGet($"/api/vehicles?page=1&pageSize=20&search={Uri.EscapeDataString("CA_400003")}");
        var underscoreFullResponse = await _client.SendAsync(underscoreFullRequest);
        var underscoreFullJson = await underscoreFullResponse.Content.ReadAsStringAsync();
        using var underscoreFullDocument = JsonDocument.Parse(underscoreFullJson);

        underscoreFullDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        underscoreFullDocument.RootElement.GetProperty("items")[0].GetProperty("registrationNumber").GetString()
            .Should().Be("CA_400003");
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

    [Theory]
    [InlineData("ca999999")]
    [InlineData("  CA999999  ")]
    public async Task Post_RegistrationNumberDifferingOnlyByCaseOrWhitespace_Returns409(string registrationNumber)
    {
        await SeedVehiclesAsync(Vehicle.Create("CA999999", "Ford", "Ranger", 2022, 500m));

        using var request = AuthenticatedPost("/api/vehicles", new
        {
            registrationNumber,
            make = "Toyota",
            model = "Hilux",
            year = 2022,
            dailyRate = 480m,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("1000000.01")]
    [InlineData("99999999999999999")]
    [InlineData("350.555")]
    public async Task Post_DailyRateTooLargeOrTooPrecise_Returns400NotA500(string dailyRate)
    {
        using var request = AuthenticatedPost("/api/vehicles", new
        {
            registrationNumber = "CA555555",
            make = "Toyota",
            model = "Hilux",
            year = 2022,
            dailyRate = decimal.Parse(dailyRate, System.Globalization.CultureInfo.InvariantCulture),
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The QA-found race this fix exists for: two near-simultaneous Create requests for the SAME
    /// registration number, neither of which existed before either request started, fired
    /// concurrently via <see cref="Task.WhenAll(Task[])"/> against the real ASP.NET Core pipeline and
    /// its own ephemeral Postgres container (mirrors <c>BookingMigrationTests</c>'s own concurrency
    /// proof's spirit, but through the full HTTP stack rather than bypassing it). Before the fix, the
    /// loser crashed with an unhandled 500 from the unique-index violation; after it, the loser must
    /// get the identical 409 the sequential-duplicate case already returns.
    /// </summary>
    [Fact]
    public async Task Post_TwoConcurrentRequestsWithSameRegistrationNumber_ExactlyOneSucceeds_TheOtherReturns409NotServerError()
    {
        object Body() => new
        {
            registrationNumber = "CA999111",
            make = "Toyota",
            model = "Hilux",
            year = 2023,
            dailyRate = 480m,
        };

        using var requestA = AuthenticatedPost("/api/vehicles", Body());
        using var requestB = AuthenticatedPost("/api/vehicles", Body());

        var responses = await Task.WhenAll(_client.SendAsync(requestA), _client.SendAsync(requestB));

        responses.Should().NotContain(response => response.StatusCode == HttpStatusCode.InternalServerError);
        responses.Count(response => response.StatusCode == HttpStatusCode.Created).Should().Be(
            1, "exactly one of the two concurrently-racing creates must win");

        var rejected = responses.Single(response => response.StatusCode != HttpStatusCode.Created);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await rejected.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This registration number is already in use.");

        foreach (var response in responses)
        {
            response.Dispose();
        }
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
    public async Task Deactivate_VehicleWithActiveBooking_Returns409WithExactDetailMessage_AndVehicleStillAppearsInDefaultList()
    {
        var vehicle = Vehicle.Create("CA151617", "Toyota", "Fortuner", 2023, 550m);
        await SeedVehiclesAsync(vehicle);
        await SeedActiveBookingAsync(vehicle);

        using var request = AuthenticatedPost($"/api/vehicles/{vehicle.Id}/deactivate");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString().Should().Be(
            "This vehicle has an active or upcoming booking — cancel it first, or wait for it to complete.");

        // Prove the vehicle really is still active -- both in the default list and in the raw row.
        using var getRequest = AuthenticatedGet("/api/vehicles?page=1&pageSize=20&search=CA151617");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);

        using var verifyScope = _factory.Services.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var isDeleted = await dbContext.Vehicles
            .Where(v => v.Id == vehicle.Id)
            .Select(v => v.IsDeleted)
            .SingleAsync();
        isDeleted.Should().BeFalse();
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

    /// <summary>
    /// The QA-found race this fix exists for: two near-simultaneous Restore requests against the
    /// SAME soft-deleted vehicle (e.g. a double-click), both reading <c>IsDeleted = true</c> before
    /// either commits. Before the fix, both would silently succeed (double-processing); after it, the
    /// xmin concurrency token makes the loser's <c>SaveChangesAsync</c> throw
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>, which
    /// <c>RestoreVehicleCommandHandler</c> catches, re-fetches, and re-calls <c>Vehicle.Restore()</c>
    /// on -- now correctly seeing the vehicle as already active and throwing the identical 409 the
    /// sequential-attempt case already returns.
    /// </summary>
    [Fact]
    public async Task Restore_TwoConcurrentRequestsOnSameSoftDeletedVehicle_ExactlyOneSucceeds_TheOtherReturns409()
    {
        var vehicle = Vehicle.Create("CA353637", "Isuzu", "D-Max", 2023, 470m);
        vehicle.SoftDelete();
        await SeedVehiclesAsync(vehicle);

        using var requestA = AuthenticatedPost($"/api/vehicles/{vehicle.Id}/restore");
        using var requestB = AuthenticatedPost($"/api/vehicles/{vehicle.Id}/restore");

        var responses = await Task.WhenAll(_client.SendAsync(requestA), _client.SendAsync(requestB));

        responses.Should().NotContain(response => response.StatusCode == HttpStatusCode.InternalServerError);
        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).Should().Be(
            1, "exactly one of the two concurrently-racing restores must win");

        var rejected = responses.Single(response => response.StatusCode != HttpStatusCode.NoContent);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await rejected.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("detail").GetString().Should().Be("Already active.");

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// Found by the Bruno concurrency demo: two near-simultaneous Deactivate requests for the SAME
    /// vehicle (a double-click) both read it as active, and the loser's xmin-guarded UPDATE matched
    /// nothing. <c>SoftDeleteVehicleCommandHandler</c> never caught that
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>, so the loser was a 500
    /// (every time). It must now be the clean 404 a sequential second click already gets.
    /// </summary>
    [Fact]
    public async Task Deactivate_TwoConcurrentRequestsOnSameVehicle_ExactlyOneSucceeds_TheOtherIs404Or409()
    {
        var vehicle = Vehicle.Create("CA383940", "Isuzu", "D-Max", 2023, 470m);
        await SeedVehiclesAsync(vehicle);

        using var requestA = AuthenticatedPost($"/api/vehicles/{vehicle.Id}/deactivate");
        using var requestB = AuthenticatedPost($"/api/vehicles/{vehicle.Id}/deactivate");

        var responses = await Task.WhenAll(_client.SendAsync(requestA), _client.SendAsync(requestB));

        responses.Should().NotContain(response => response.StatusCode == HttpStatusCode.InternalServerError);
        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).Should().Be(
            1, "exactly one of the two concurrently-racing deactivates must win");
        responses.Single(response => response.StatusCode != HttpStatusCode.NoContent).StatusCode
            .Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Conflict);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
