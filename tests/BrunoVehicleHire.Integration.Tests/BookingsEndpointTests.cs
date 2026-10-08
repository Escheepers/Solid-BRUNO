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
using Npgsql;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// End-to-end proof of the Booking feature slice (List/Create -- spec-4-1; overlap prevention --
/// spec-4-2; Cancel -- spec-4-3) against a real ASP.NET Core pipeline via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> -- not a mock -- backed by its own ephemeral
/// Testcontainers Postgres instance (mirrors <see cref="VehiclesEndpointTests"/>'s exact pattern):
/// ApiClient -> API-key auth -> <c>GetBookingsQuery</c>/<c>CreateBookingCommand</c>/
/// <c>CancelBookingCommand</c> -> <c>IBookingRepository</c>/<c>IVehicleRepository</c>/
/// <c>ICustomerRepository</c> -> PostgreSQL -> a real <c>PagedResult&lt;BookingDto&gt;</c>/
/// <c>BookingDto</c>/204/409/404 response. Covers every row of spec-4-1/spec-4-2/spec-4-3's own
/// I/O &amp; Edge-Case Matrix.
/// </summary>
public class BookingsEndpointTests : IAsyncLifetime
{
    private const string ConfiguredKey = "bookings-endpoint-test-key";

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

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        // Start every test from a clean slate regardless of Testcontainers container reuse across
        // test method instances, mirroring VehiclesEndpointTests's exact reasoning.
        // IgnoreQueryFilters() clears any already-soft-deleted/anonymized rows left over too. Delete
        // Bookings first -- its FK constraints (Restrict) would otherwise reject deleting a
        // referenced Vehicle/Customer row.
        await dbContext.Bookings.ExecuteDeleteAsync();
        await dbContext.Vehicles.IgnoreQueryFilters().ExecuteDeleteAsync();
        await dbContext.Customers.IgnoreQueryFilters().ExecuteDeleteAsync();
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

    /// <summary>Body-less overload for action routes like Cancel, mirroring <c>VehiclesEndpointTests</c>'s own.</summary>
    private HttpRequestMessage AuthenticatedPost(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(ApiKeyDefaults.HeaderName, ConfiguredKey);
        return request;
    }

    private async Task<Vehicle> SeedActiveVehicleAsync(decimal dailyRate = 350m, string registrationNumber = "CA123456")
    {
        var vehicle = Vehicle.Create(registrationNumber, "Toyota", "Corolla", 2023, dailyRate);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Vehicles.Add(vehicle);
        await dbContext.SaveChangesAsync();

        return vehicle;
    }

    private async Task<Vehicle> SeedSoftDeletedVehicleAsync()
    {
        var vehicle = Vehicle.Create("CA654321", "Honda", "Civic", 2022, 300m);
        vehicle.SoftDelete();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Vehicles.Add(vehicle);
        await dbContext.SaveChangesAsync();

        return vehicle;
    }

    private async Task<Customer> SeedActiveCustomerAsync()
    {
        var customer = Customer.Create("Jane", "Doe", $"jane.{Guid.NewGuid()}@example.com", "0821234567");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        return customer;
    }

    private async Task<Customer> SeedSoftDeletedCustomerAsync()
    {
        var customer = Customer.Create("John", "Smith", $"john.{Guid.NewGuid()}@example.com", "0827654321");
        customer.SoftDelete();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        return customer;
    }

    private async Task<Booking> SeedBookingAsync(
        Vehicle vehicle,
        Customer customer,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        var booking = Booking.Create(
            vehicle.Id,
            customer.Id,
            startDate ?? new DateOnly(2026, 10, 1),
            endDate ?? new DateOnly(2026, 10, 5),
            1400m);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();

        return booking;
    }

    /// <summary>
    /// Booking still has no public Complete() transition (out of scope -- see domain-model.md/
    /// Booking.cs), and even <see cref="Domain.Booking.Cancel"/> (spec-4-3) can only ever reach
    /// <c>Cancelled</c>, never <c>Completed</c> -- so this flips the "Status" column directly via raw
    /// SQL after a normal (always-Active) seed, exactly as a future Complete-sweep feature eventually
    /// would, so spec-4-2's Completed-blocks/Cancelled-doesn't-block and spec-4-3's already-Completed/
    /// already-Cancelled I/O-matrix rows can all be proven through the real API.
    /// </summary>
    private async Task<Booking> SeedBookingWithStatusAsync(
        Vehicle vehicle, Customer customer, DateOnly startDate, DateOnly endDate, BookingStatus status)
    {
        var booking = await SeedBookingAsync(vehicle, customer, startDate, endDate);

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE "Bookings" SET "Status" = @status WHERE "Id" = @id""";
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("id", booking.Id);
        await command.ExecuteNonQueryAsync();

        return booking;
    }

    /// <summary>
    /// Regression test (spec-search-multi-word): a multi-word booking search used to return nothing.
    /// Each word must now match the joined Vehicle's Make/Model/RegistrationNumber or the Customer's
    /// FirstName/LastName -- so "Ericka U" finds Ericka Ullrich's bookings, and "Toyota Ericka" mixes
    /// a vehicle word with a customer word.
    /// </summary>
    [Theory]
    [InlineData("Ericka U", 2)]
    [InlineData("Toyota Ericka", 1)]
    [InlineData("Ullrich Honda", 1)]
    [InlineData("Toyota Bob", 1)]
    [InlineData("Honda Bob", 0)]
    [InlineData("Toyota — Ericka", 1)]
    [InlineData("Toyota - Ericka", 1)]
    public async Task Get_SearchWithSeveralWords_EveryWordMustMatchAVehicleOrCustomerField(
        string search, int expectedCount)
    {
        var toyota = Vehicle.Create("CA700001", "Toyota", "Corolla", 2023, 350m);
        var honda = Vehicle.Create("CA700002", "Honda", "Civic", 2023, 350m);
        var ericka = Customer.Create("Ericka", "Ullrich", "ericka.u@example.com", "0821110201");
        var bob = Customer.Create("Bob", "Smith", "bob.s@example.com", "0821110202");

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Vehicles.AddRange(toyota, honda);
            dbContext.Customers.AddRange(ericka, bob);
            await dbContext.SaveChangesAsync();
        }

        await SeedBookingAsync(toyota, ericka, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));
        await SeedBookingAsync(honda, ericka, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));
        await SeedBookingAsync(toyota, bob, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12));

        using var request = AuthenticatedGet(
            $"/api/bookings?page=1&pageSize=20&search={Uri.EscapeDataString(search)}");
        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(expectedCount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task SeedBookingCreatedAtAsync(
        Vehicle vehicle, Customer customer, DateOnly startDate, DateOnly endDate, DateTimeOffset createdAt)
    {
        var booking = Booking.Create(
            vehicle.Id, customer.Id, startDate, endDate, 1400m, new FixedTimeProvider(createdAt));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Regression test (spec-new-rows-visible-first): lists used to be ordered oldest-first, so a
    /// booking created through the UI landed on the LAST page and looked as if nothing had been
    /// created. The list must now be newest-first, so the new row is always on page 1.
    /// </summary>
    [Fact]
    public async Task Get_AfterCreatingANewBooking_ListsNewestFirst_SoItAppearsOnPageOne()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        await SeedBookingCreatedAtAsync(vehicle, customer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await SeedBookingCreatedAtAsync(vehicle, customer, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await SeedBookingCreatedAtAsync(vehicle, customer, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 22), new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        using var createRequest = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = customer.Id,
            startDate = new DateOnly(2026, 12, 1),
            endDate = new DateOnly(2026, 12, 3),
        });
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var listRequest = AuthenticatedGet("/api/bookings?page=1&pageSize=2");
        var listResponse = await _client.SendAsync(listRequest);
        var json = await listResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var startDates = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("startDate").GetString())
            .ToList();

        startDates.Should().Equal(
            new[] { "2026-12-01", "2026-11-20" },
            "the list is newest-first, so the just-created booking is first");
        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Get_BookingsExist_ReturnsPagedResultShapeWithVehicleAndCustomerFields()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        await SeedBookingAsync(vehicle, customer);

        using var request = AuthenticatedGet("/api/bookings?page=1&pageSize=20");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        var item = document.RootElement.GetProperty("items")[0];

        item.GetProperty("vehicleMake").GetString().Should().Be("Toyota");
        item.GetProperty("vehicleModel").GetString().Should().Be("Corolla");
        item.GetProperty("vehicleRegistrationNumber").GetString().Should().Be("CA123456");
        item.GetProperty("customerFirstName").GetString().Should().Be("Jane");
        item.GetProperty("customerLastName").GetString().Should().Be("Doe");
        item.GetProperty("customerIsAnonymized").GetBoolean().Should().BeFalse();
        item.GetProperty("status").GetString().Should().Be("Active");
        item.GetProperty("totalPrice").GetDecimal().Should().Be(1400m);
    }

    [Fact]
    public async Task Get_BookingWhoseVehicleIsNowSoftDeleted_StillAppearsInList()
    {
        var vehicle = await SeedSoftDeletedVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        await SeedBookingAsync(vehicle, customer);

        using var request = AuthenticatedGet("/api/bookings?page=1&pageSize=20");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("vehicleRegistrationNumber").GetString()
            .Should().Be("CA654321");
    }

    [Fact]
    public async Task Get_BookingWhoseCustomerIsNowAnonymized_StillAppearsInListWithIsAnonymizedTrue()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        await SeedBookingAsync(vehicle, customer);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trackedCustomer = await dbContext.Customers.IgnoreQueryFilters()
                .SingleAsync(c => c.Id == customer.Id);
            trackedCustomer.Anonymize();
            await dbContext.SaveChangesAsync();
        }

        using var request = AuthenticatedGet("/api/bookings?page=1&pageSize=20");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("customerIsAnonymized").GetBoolean()
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("page=0&pageSize=20")]
    [InlineData("page=1&pageSize=500")]
    public async Task Get_InvalidPagingParameters_Returns400WithFieldNamedInErrors(string queryString)
    {
        using var request = AuthenticatedGet($"/api/bookings?{queryString}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        document.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Get_MissingApiKey_Returns401_FallbackPolicyStillApplies()
    {
        var response = await _client.GetAsync("/api/bookings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Proves the additive <c>vehicleId</c> filter (spec-4-5's Scope decision 1) only returns
    /// bookings for that vehicle, leaving the unfiltered list's own behavior untouched -- backs
    /// Vehicle Detail's booking-history section.
    /// </summary>
    [Fact]
    public async Task Get_FilteredByVehicleId_ReturnsOnlyThatVehiclesBookings()
    {
        var vehicleOne = await SeedActiveVehicleAsync(registrationNumber: "CA111111");
        var vehicleTwo = await SeedActiveVehicleAsync(registrationNumber: "CA222222");
        var customer = await SeedActiveCustomerAsync();
        var bookingForVehicleOne = await SeedBookingAsync(vehicleOne, customer);
        await SeedBookingAsync(vehicleTwo, customer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));

        using var request = AuthenticatedGet($"/api/bookings?page=1&pageSize=20&vehicleId={vehicleOne.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid()
            .Should().Be(bookingForVehicleOne.Id);
    }

    [Fact]
    public async Task Get_VehicleIdOmitted_ReturnsEveryBookingUnfiltered()
    {
        var vehicleOne = await SeedActiveVehicleAsync(registrationNumber: "CA111111");
        var vehicleTwo = await SeedActiveVehicleAsync(registrationNumber: "CA222222");
        var customer = await SeedActiveCustomerAsync();
        await SeedBookingAsync(vehicleOne, customer);
        await SeedBookingAsync(vehicleTwo, customer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));

        using var request = AuthenticatedGet("/api/bookings?page=1&pageSize=20");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// Covers spec-bookings-search's own I/O &amp; Edge-Case Matrix through the real API: a term
    /// matching a vehicle's Make/Model/RegistrationNumber returns only that vehicle's bookings; a term
    /// matching a customer's FirstName/LastName returns only that customer's bookings; an ambiguous
    /// term matching both an unrelated vehicle and an unrelated customer returns the union (OR, not
    /// AND); an empty/whitespace search behaves identically to no search at all; and the pre-existing
    /// <c>vehicleId</c> filter (Vehicle Detail's booking-history section, which never sets
    /// <c>search</c>) remains completely unaffected.
    /// </summary>
    [Fact]
    public async Task Get_SearchMatchesVehicleMake_ReturnsOnlyThatVehiclesBookings()
    {
        var ferrari = await SeedActiveVehicleAsync(registrationNumber: "CA111111");
        var toyota = await SeedActiveVehicleAsync(registrationNumber: "CA222222");
        var customer = await SeedActiveCustomerAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trackedFerrari = await dbContext.Vehicles.SingleAsync(v => v.Id == ferrari.Id);
        trackedFerrari.Update("CA111111", "Ferrari", "F8", 2024, 5000m);
        await dbContext.SaveChangesAsync();

        var ferrariBooking = await SeedBookingAsync(ferrari, customer);
        await SeedBookingAsync(toyota, customer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));

        using var request = AuthenticatedGet("/api/bookings?page=1&pageSize=20&search=ferrari");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(ferrariBooking.Id);
    }

    [Fact]
    public async Task Get_SearchMatchesVehicleRegistrationNumber_ReturnsOnlyThatVehiclesBookings()
    {
        var vehicleOne = await SeedActiveVehicleAsync(registrationNumber: "CA999999");
        var vehicleTwo = await SeedActiveVehicleAsync(registrationNumber: "CA222222");
        var customer = await SeedActiveCustomerAsync();
        var bookingOne = await SeedBookingAsync(vehicleOne, customer);
        await SeedBookingAsync(vehicleTwo, customer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));

        using var request = AuthenticatedGet("/api/bookings?page=1&pageSize=20&search=CA9999");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(bookingOne.Id);
    }

    [Fact]
    public async Task Get_SearchMatchesCustomerName_ReturnsOnlyThatCustomersBookings()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var nkosi = await SeedActiveCustomerAsync();
        var otherCustomer = await SeedActiveCustomerAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trackedNkosi = await dbContext.Customers.SingleAsync(c => c.Id == nkosi.Id);
        trackedNkosi.Update("Thabo", "Nkosi", trackedNkosi.Email, trackedNkosi.PhoneNumber);
        await dbContext.SaveChangesAsync();

        var nkosiBooking = await SeedBookingAsync(vehicle, nkosi);
        await SeedBookingAsync(vehicle, otherCustomer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));

        using var request = AuthenticatedGet("/api/bookings?page=1&pageSize=20&search=nkosi");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(nkosiBooking.Id);
    }

    [Fact]
    public async Task Get_SearchMatchesBothAnUnrelatedVehicleAndCustomer_ReturnsTheUnion()
    {
        var vehicle = await SeedActiveVehicleAsync(registrationNumber: "CA111111");
        var unrelatedVehicle = await SeedActiveVehicleAsync(registrationNumber: "CA222222");
        var unrelatedCustomer = await SeedActiveCustomerAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trackedVehicle = await dbContext.Vehicles.SingleAsync(v => v.Id == unrelatedVehicle.Id);
        trackedVehicle.Update("CA222222", "Nkosi Motors", "Cruiser", 2024, 900m);
        await dbContext.SaveChangesAsync();

        var matchingCustomer = await SeedActiveCustomerAsync();
        using (var innerScope = _factory.Services.CreateScope())
        {
            var innerDbContext = innerScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trackedCustomer = await innerDbContext.Customers.SingleAsync(c => c.Id == matchingCustomer.Id);
            trackedCustomer.Update("Thabo", "Nkosi", trackedCustomer.Email, trackedCustomer.PhoneNumber);
            await innerDbContext.SaveChangesAsync();
        }

        var vehicleMatchBooking = await SeedBookingAsync(unrelatedVehicle, unrelatedCustomer);
        var customerMatchBooking = await SeedBookingAsync(
            vehicle, matchingCustomer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));

        using var request = AuthenticatedGet("/api/bookings?page=1&pageSize=20&search=nkosi");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var ids = document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        ids.Should().BeEquivalentTo(new[] { vehicleMatchBooking.Id, customerMatchBooking.Id });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Get_EmptyOrWhitespaceSearch_ReturnsEveryBookingUnfiltered(string search)
    {
        var vehicleOne = await SeedActiveVehicleAsync(registrationNumber: "CA111111");
        var vehicleTwo = await SeedActiveVehicleAsync(registrationNumber: "CA222222");
        var customer = await SeedActiveCustomerAsync();
        await SeedBookingAsync(vehicleOne, customer);
        await SeedBookingAsync(vehicleTwo, customer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));

        using var request = AuthenticatedGet(
            $"/api/bookings?page=1&pageSize=20&search={Uri.EscapeDataString(search)}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Get_NoMatchingSearchTerm_ReturnsEmptyResult()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        await SeedBookingAsync(vehicle, customer);

        using var request = AuthenticatedGet("/api/bookings?page=1&pageSize=20&search=nonexistentterm");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Regression test for the adversarial-found bug (spec-search-wildcard-escaping): the raw
    /// <c>search</c> string used to be interpolated straight into the <c>ILIKE</c> pattern, so a
    /// bare <c>"%"</c> became the pattern <c>"%%%"</c> (matches anything -- confirmed live: 59/59
    /// bookings on the running app) and a bare <c>"_"</c> became <c>"%_%"</c> (also matches any
    /// non-empty string). Both must now match only rows whose joined Vehicle/Customer fields
    /// contain that character literally. Mirrors
    /// <see cref="VehiclesEndpointTests.Get_SearchWithLiteralPercentOrUnderscore_TreatsThemLiterally_NeverAsWildcards"/>.
    /// </summary>
    [Fact]
    public async Task Get_SearchWithLiteralPercentOrUnderscore_TreatsThemLiterally_NeverAsWildcards()
    {
        var percentVehicle = await SeedActiveVehicleAsync(registrationNumber: "CA400001");
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await dbContext.Vehicles.SingleAsync(v => v.Id == percentVehicle.Id);
            tracked.Update("CA400001", "50% Off Deals", "Corolla", 2023, 350m);
            await dbContext.SaveChangesAsync();
        }

        var underscoreVehicle = await SeedActiveVehicleAsync(registrationNumber: "CA_400003");
        var decoyVehicle = await SeedActiveVehicleAsync(registrationNumber: "CAX400003");
        var plainVehicle = await SeedActiveVehicleAsync(registrationNumber: "CA400002");
        var customer = await SeedActiveCustomerAsync();

        var percentBooking = await SeedBookingAsync(percentVehicle, customer);
        var underscoreBooking = await SeedBookingAsync(
            underscoreVehicle, customer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));
        await SeedBookingAsync(decoyVehicle, customer, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 7));
        await SeedBookingAsync(plainVehicle, customer, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 11));

        // A bare "%" must match only the booking whose vehicle has a literal "%" -- not every
        // booking (4 seeded here).
        using var percentOnlyRequest =
            AuthenticatedGet($"/api/bookings?page=1&pageSize=20&search={Uri.EscapeDataString("%")}");
        var percentOnlyResponse = await _client.SendAsync(percentOnlyRequest);
        var percentOnlyJson = await percentOnlyResponse.Content.ReadAsStringAsync();
        using var percentOnlyDocument = JsonDocument.Parse(percentOnlyJson);

        percentOnlyDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        percentOnlyDocument.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid()
            .Should().Be(percentBooking.Id);

        // A bare "_" must match only the booking whose vehicle registration has a literal "_" --
        // not the same-length decoy registration "CAX400003" that a single-char wildcard would
        // otherwise also match.
        using var underscoreOnlyRequest =
            AuthenticatedGet($"/api/bookings?page=1&pageSize=20&search={Uri.EscapeDataString("_")}");
        var underscoreOnlyResponse = await _client.SendAsync(underscoreOnlyRequest);
        var underscoreOnlyJson = await underscoreOnlyResponse.Content.ReadAsStringAsync();
        using var underscoreOnlyDocument = JsonDocument.Parse(underscoreOnlyJson);

        underscoreOnlyDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        underscoreOnlyDocument.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid()
            .Should().Be(underscoreBooking.Id);
    }

    /// <summary>
    /// Proves <c>search</c> and the pre-existing <c>vehicleId</c> filter compose (AND, not that either
    /// overrides the other) -- Vehicle Detail's own booking-history call site never sets <c>search</c>,
    /// so its behavior stays completely unaffected by this story.
    /// </summary>
    [Fact]
    public async Task Get_VehicleIdSetAndSearchNull_BehavesExactlyAsBeforeThisStory()
    {
        var vehicleOne = await SeedActiveVehicleAsync(registrationNumber: "CA111111");
        var vehicleTwo = await SeedActiveVehicleAsync(registrationNumber: "CA222222");
        var customer = await SeedActiveCustomerAsync();
        var bookingForVehicleOne = await SeedBookingAsync(vehicleOne, customer);
        await SeedBookingAsync(vehicleTwo, customer, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));

        using var request = AuthenticatedGet($"/api/bookings?page=1&pageSize=20&vehicleId={vehicleOne.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid()
            .Should().Be(bookingForVehicleOne.Id);
    }

    [Fact]
    public async Task Post_ValidVehicleCustomerAndDateRange_Returns201WithActiveStatusAndCorrectTotalPrice_AndAppearsInList()
    {
        var vehicle = await SeedActiveVehicleAsync(dailyRate: 350m);
        var customer = await SeedActiveCustomerAsync();

        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = customer.Id,
            startDate = "2030-10-01",
            endDate = "2030-10-05",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("status").GetString().Should().Be("Active");
        // 4 days x 350 = 1400.
        document.RootElement.GetProperty("totalPrice").GetDecimal().Should().Be(1400m);
        document.RootElement.TryGetProperty("id", out var idProperty).Should().BeTrue();

        var createdId = idProperty.GetGuid();
        response.Headers.Location!.OriginalString.Should().Be($"/api/bookings/{createdId}");

        // Prove the write really committed -- a subsequent GET must see it, not merely that the
        // POST response body looked right.
        using var getRequest = AuthenticatedGet("/api/bookings?page=1&pageSize=20");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        getDocument.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(createdId);
    }

    [Fact]
    public async Task Post_EndDateEqualToStartDate_Returns400()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();

        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = customer.Id,
            startDate = "2030-10-01",
            endDate = "2030-10-01",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty("EndDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_EndDateBeforeStartDate_Returns400()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();

        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = customer.Id,
            startDate = "2030-10-05",
            endDate = "2030-10-01",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_StartDateInThePast_Returns400WithStartDateError()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = customer.Id,
            startDate = yesterday,
            endDate = yesterday.AddDays(3),
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty("StartDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_BookingLongerThanTheMaximumDuration_Returns400WithEndDateError()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();

        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = customer.Id,
            startDate = "2030-01-01",
            endDate = "9999-12-31",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty("EndDate", out _).Should().BeTrue();
    }

    /// <summary>
    /// <c>PUT /api/bookings/{id}</c> reschedules an UPCOMING booking: new dates and a rescaled price
    /// (at the booking's original daily rate), persisted; it may move into its own old range; and every
    /// refusal (past start, bad range, clash with another booking, cancelled/started/completed booking,
    /// unknown id) is a clean 4xx. Seeded bookings are 4 days for 1400 => 350/day.
    /// </summary>
    private async Task<(Vehicle Vehicle, Customer Customer, Booking Booking)> SeedUpcomingBookingAsync()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var booking = await SeedBookingAsync(vehicle, customer, new DateOnly(2030, 3, 10), new DateOnly(2030, 3, 14));
        return (vehicle, customer, booking);
    }

    [Fact]
    public async Task Put_UpcomingBooking_Returns200WithNewDatesAndRescaledPrice_AndPersistsIt()
    {
        var (_, _, booking) = await SeedUpcomingBookingAsync();

        // Overlaps its own old range (10-14 Mar) -- must not be treated as a clash.
        using var request = AuthenticatedPut($"/api/bookings/{booking.Id}", new
        {
            startDate = "2030-03-12",
            endDate = "2030-03-19",
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("startDate").GetString().Should().Be("2030-03-12");
        document.RootElement.GetProperty("endDate").GetString().Should().Be("2030-03-19");
        document.RootElement.GetProperty("totalPrice").GetDecimal().Should().Be(2450m); // 7 days x 350
        document.RootElement.GetProperty("status").GetString().Should().Be("Active");

        using var getRequest = AuthenticatedGet($"/api/bookings/{booking.Id}");
        var getResponse = await _client.SendAsync(getRequest);
        using var getDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        getDocument.RootElement.GetProperty("startDate").GetString().Should().Be("2030-03-12");
        getDocument.RootElement.GetProperty("totalPrice").GetDecimal().Should().Be(2450m);
    }

    [Fact]
    public async Task Put_NewRangeOverlapsAnotherBookingOnTheVehicle_Returns409WithThatBookingsDates()
    {
        var (vehicle, _, booking) = await SeedUpcomingBookingAsync();
        var otherCustomer = await SeedActiveCustomerAsync();
        await SeedBookingAsync(vehicle, otherCustomer, new DateOnly(2030, 4, 1), new DateOnly(2030, 4, 5));

        using var request = AuthenticatedPut($"/api/bookings/{booking.Id}", new
        {
            startDate = "2030-03-30",
            endDate = "2030-04-02",
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This vehicle is already booked 1 Apr – 5 Apr");

        // The failed edit changed nothing.
        using var getRequest = AuthenticatedGet($"/api/bookings/{booking.Id}");
        var getResponse = await _client.SendAsync(getRequest);
        using var getDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        getDocument.RootElement.GetProperty("startDate").GetString().Should().Be("2030-03-10");
    }

    [Fact]
    public async Task Put_NewRangeTouchingAnotherBookingsEndDate_SameDayTurnover_Returns200()
    {
        var (vehicle, _, booking) = await SeedUpcomingBookingAsync();
        var otherCustomer = await SeedActiveCustomerAsync();
        await SeedBookingAsync(vehicle, otherCustomer, new DateOnly(2030, 4, 1), new DateOnly(2030, 4, 5));

        using var request = AuthenticatedPut($"/api/bookings/{booking.Id}", new
        {
            startDate = "2030-04-05",
            endDate = "2030-04-08",
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Put_StartDateInThePast_Returns400WithStartDateError()
    {
        var (_, _, booking) = await SeedUpcomingBookingAsync();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        using var request = AuthenticatedPut($"/api/bookings/{booking.Id}", new
        {
            startDate = yesterday,
            endDate = yesterday.AddDays(3),
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("errors").TryGetProperty("StartDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Put_EndDateNotAfterStartDate_Returns400WithEndDateError()
    {
        var (_, _, booking) = await SeedUpcomingBookingAsync();

        using var request = AuthenticatedPut($"/api/bookings/{booking.Id}", new
        {
            startDate = "2030-03-12",
            endDate = "2030-03-12",
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("errors").TryGetProperty("EndDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Put_CancelledBooking_Returns409CannotEdit()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var booking = await SeedBookingWithStatusAsync(
            vehicle, customer, new DateOnly(2030, 3, 10), new DateOnly(2030, 3, 14), BookingStatus.Cancelled);

        using var request = AuthenticatedPut($"/api/bookings/{booking.Id}", new
        {
            startDate = "2030-03-20",
            endDate = "2030-03-22",
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("detail").GetString().Should().Be("Cannot edit — booking is cancelled.");
    }

    [Fact]
    public async Task Put_BookingThatHasAlreadyStarted_Returns409CannotEdit()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var booking = await SeedBookingAsync(vehicle, customer, today, today.AddDays(5));

        using var request = AuthenticatedPut($"/api/bookings/{booking.Id}", new
        {
            startDate = today.AddDays(10),
            endDate = today.AddDays(12),
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("Cannot edit — booking has already started.");
    }

    [Fact]
    public async Task Put_NonexistentBookingId_Returns404()
    {
        using var request = AuthenticatedPut($"/api/bookings/{Guid.NewGuid()}", new
        {
            startDate = "2030-03-20",
            endDate = "2030-03-22",
        });
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_SoftDeletedVehicleSelected_Returns409WithExactDetailMessage()
    {
        var vehicle = await SeedSoftDeletedVehicleAsync();
        var customer = await SeedActiveCustomerAsync();

        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = customer.Id,
            startDate = "2030-10-01",
            endDate = "2030-10-05",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString().Should().Be("This vehicle is not available.");
    }

    [Fact]
    public async Task Post_NonexistentVehicleId_Returns404()
    {
        var customer = await SeedActiveCustomerAsync();

        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = Guid.NewGuid(),
            customerId = customer.Id,
            startDate = "2030-10-01",
            endDate = "2030-10-05",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_NonexistentCustomerId_Returns404()
    {
        var vehicle = await SeedActiveVehicleAsync();

        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = Guid.NewGuid(),
            startDate = "2030-10-01",
            endDate = "2030-10-05",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_InactiveSoftDeletedCustomerSelected_Returns404()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedSoftDeletedCustomerAsync();

        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = customer.Id,
            startDate = "2030-10-01",
            endDate = "2030-10-05",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_OverlapsAnExistingActiveBooking_Returns409WithExactDetailMessage()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        // Existing Active booking: 2 Sep - 4 Sep (matches the AC's own example verbatim).
        await SeedBookingAsync(vehicle, customer, new DateOnly(2030, 9, 2), new DateOnly(2030, 9, 4));

        var otherCustomer = await SeedActiveCustomerAsync();
        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = otherCustomer.Id,
            startDate = "2030-09-03",
            endDate = "2030-09-06",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This vehicle is already booked 2 Sep – 4 Sep");
    }

    [Fact]
    public async Task Post_StartDateEqualsExistingBookingsEndDate_SameDayTurnover_Returns201()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        // Existing booking ends exactly on 5 Oct -- the new request starts exactly there too.
        await SeedBookingAsync(vehicle, customer, new DateOnly(2030, 10, 1), new DateOnly(2030, 10, 5));

        var otherCustomer = await SeedActiveCustomerAsync();
        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = otherCustomer.Id,
            startDate = "2030-10-05",
            endDate = "2030-10-09",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Post_OverlapsAnExistingCompletedBooking_Returns409WithSameMessageShape()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        await SeedBookingWithStatusAsync(
            vehicle, customer, new DateOnly(2030, 9, 2), new DateOnly(2030, 9, 4), BookingStatus.Completed);

        var otherCustomer = await SeedActiveCustomerAsync();
        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = otherCustomer.Id,
            startDate = "2030-09-03",
            endDate = "2030-09-06",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This vehicle is already booked 2 Sep – 4 Sep");
    }

    [Fact]
    public async Task Post_OverlapsAnExistingCancelledBooking_Returns201_CancelledNeverBlocks()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        await SeedBookingWithStatusAsync(
            vehicle, customer, new DateOnly(2030, 9, 2), new DateOnly(2030, 9, 4), BookingStatus.Cancelled);

        var otherCustomer = await SeedActiveCustomerAsync();
        using var request = AuthenticatedPost("/api/bookings", new
        {
            vehicleId = vehicle.Id,
            customerId = otherCustomer.Id,
            startDate = "2030-09-03",
            endDate = "2030-09-06",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Covers every row of spec-4-3's I/O &amp; Edge-Case Matrix through the real API: a future
    /// Active booking cancels successfully and is never physically removed; an already-Completed,
    /// already-Cancelled, or still-Active-but-past-StartDate booking each returns 409 with the exact
    /// message for that state (StartDate-based rule per spec-booking-form-error-handling-fixes.md); a
    /// nonexistent id returns 404. Completed/past-Active rows are seeded via
    /// <see cref="SeedBookingWithStatusAsync"/>, mirroring spec-4-2's own raw-SQL status-flip pattern.
    /// </summary>
    [Fact]
    public async Task Cancel_FutureActiveBooking_Returns204_SetsStatusToCancelled_RowNeverRemoved()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var booking = await SeedBookingAsync(
            vehicle, customer, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5));

        using var request = AuthenticatedPost($"/api/bookings/{booking.Id}/cancel");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await dbContext.Bookings.SingleAsync(b => b.Id == booking.Id);

        persisted.Status.Should().Be(BookingStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_AlreadyCompletedBooking_Returns409WithExactDetailMessage()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var booking = await SeedBookingWithStatusAsync(
            vehicle, customer, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), BookingStatus.Completed);

        using var request = AuthenticatedPost($"/api/bookings/{booking.Id}/cancel");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("Cannot cancel — booking has already started.");
    }

    [Fact]
    public async Task Cancel_AlreadyCancelledBooking_Returns409WithExactDetailMessage()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var booking = await SeedBookingWithStatusAsync(
            vehicle, customer, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), BookingStatus.Cancelled);

        using var request = AuthenticatedPost($"/api/bookings/{booking.Id}/cancel");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("Cannot cancel — booking already cancelled.");
    }

    [Fact]
    public async Task Cancel_StillActiveBookingWithPastEndDate_Returns409WithSameMessageAsAlreadyCompleted()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        // Seeded as Active (the default status), but StartDate/EndDate are both already in the past
        // relative to "today" -- proves the guard treats an unswept past-StartDate booking exactly
        // like Completed (bugfix: spec-booking-form-error-handling-fixes.md's StartDate-based rule).
        var booking = await SeedBookingAsync(
            vehicle, customer, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 5));

        using var request = AuthenticatedPost($"/api/bookings/{booking.Id}/cancel");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("Cannot cancel — booking has already started.");
    }

    [Fact]
    public async Task Cancel_NonexistentBookingId_Returns404()
    {
        using var request = AuthenticatedPost($"/api/bookings/{Guid.NewGuid()}/cancel");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The QA-found race this fix exists for: two near-simultaneous Cancel requests against the SAME
    /// Active, eligible booking (e.g. a double-click), both reading <c>Status = Active</c> before
    /// either commits. Before the fix, both would silently succeed (double-processing); after it, the
    /// xmin concurrency token makes the loser's <c>SaveChangesAsync</c> throw
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>, which
    /// <c>CancelBookingCommandHandler</c> catches, re-fetches, and re-calls <c>Booking.Cancel()</c>
    /// on -- now correctly seeing the booking as already cancelled and throwing the identical 409 the
    /// sequential-attempt case already returns.
    /// </summary>
    [Fact]
    public async Task Cancel_TwoConcurrentRequestsOnSameActiveBooking_ExactlyOneSucceeds_TheOtherReturns409()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var booking = await SeedBookingAsync(
            vehicle, customer, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5));

        using var requestA = AuthenticatedPost($"/api/bookings/{booking.Id}/cancel");
        using var requestB = AuthenticatedPost($"/api/bookings/{booking.Id}/cancel");

        var responses = await Task.WhenAll(_client.SendAsync(requestA), _client.SendAsync(requestB));

        responses.Should().NotContain(response => response.StatusCode == HttpStatusCode.InternalServerError);
        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).Should().Be(
            1, "exactly one of the two concurrently-racing cancels must win");

        var rejected = responses.Single(response => response.StatusCode != HttpStatusCode.NoContent);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await rejected.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("Cannot cancel — booking already cancelled.");

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// Covers spec-4-5's own I/O &amp; Edge-Case Matrix through the real API: a valid booking id
    /// resolves the full detail shape (vehicle, customer, dates, TotalPrice, Status); a booking whose
    /// customer has since been anonymized still resolves with <c>customerIsAnonymized</c> true, the
    /// exact same dual-<c>IgnoreQueryFilters()</c> join the list already proves; a booking whose
    /// vehicle has since been soft-deleted still resolves; a stale/invalid id returns 404.
    /// </summary>
    [Fact]
    public async Task GetById_BookingExists_Returns200WithFullDtoShape()
    {
        var vehicle = await SeedActiveVehicleAsync(dailyRate: 350m);
        var customer = await SeedActiveCustomerAsync();
        var booking = await SeedBookingAsync(vehicle, customer);

        using var request = AuthenticatedGet($"/api/bookings/{booking.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("id").GetGuid().Should().Be(booking.Id);
        document.RootElement.GetProperty("vehicleMake").GetString().Should().Be("Toyota");
        document.RootElement.GetProperty("vehicleModel").GetString().Should().Be("Corolla");
        document.RootElement.GetProperty("vehicleRegistrationNumber").GetString().Should().Be("CA123456");
        document.RootElement.GetProperty("customerFirstName").GetString().Should().Be("Jane");
        document.RootElement.GetProperty("customerLastName").GetString().Should().Be("Doe");
        document.RootElement.GetProperty("customerIsAnonymized").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("status").GetString().Should().Be("Active");
        document.RootElement.GetProperty("totalPrice").GetDecimal().Should().Be(1400m);
    }

    [Fact]
    public async Task GetById_BookingsCustomerIsNowAnonymized_Returns200WithCustomerIsAnonymizedTrue()
    {
        var vehicle = await SeedActiveVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var booking = await SeedBookingAsync(vehicle, customer);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trackedCustomer = await dbContext.Customers.IgnoreQueryFilters()
                .SingleAsync(c => c.Id == customer.Id);
            trackedCustomer.Anonymize();
            await dbContext.SaveChangesAsync();
        }

        using var request = AuthenticatedGet($"/api/bookings/{booking.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("customerIsAnonymized").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetById_BookingsVehicleIsNowSoftDeleted_Returns200Regardless()
    {
        var vehicle = await SeedSoftDeletedVehicleAsync();
        var customer = await SeedActiveCustomerAsync();
        var booking = await SeedBookingAsync(vehicle, customer);

        using var request = AuthenticatedGet($"/api/bookings/{booking.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("vehicleRegistrationNumber").GetString().Should().Be("CA654321");
    }

    [Fact]
    public async Task GetById_NonexistentBookingId_Returns404()
    {
        using var request = AuthenticatedGet($"/api/bookings/{Guid.NewGuid()}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
