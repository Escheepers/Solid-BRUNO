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
/// End-to-end proof of Story 5.1's read-only Customer Summary endpoint against a real ASP.NET Core
/// pipeline via <see cref="WebApplicationFactory{TEntryPoint}"/> -- not a mock -- backed by its own
/// ephemeral Testcontainers Postgres instance (mirrors <see cref="BookingsEndpointTests"/>/
/// <see cref="CustomersEndpointTests"/>'s exact pattern): ApiClient -> API-key auth ->
/// <c>GetCustomerSummaryQuery</c> -> <c>ICustomerRepository</c>/<c>IBookingRepository</c> ->
/// PostgreSQL -> a real <c>CustomerSummaryDto</c>/404 response. Covers every backend row of
/// spec-5-1's own I/O &amp; Edge-Case Matrix.
/// </summary>
public class CustomerSummaryEndpointTests : IAsyncLifetime
{
    private const string ConfiguredKey = "customer-summary-endpoint-test-key";

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
        // test method instances, mirroring BookingsEndpointTests's exact reasoning. Delete Bookings
        // first -- its FK constraints (Restrict) would otherwise reject deleting a referenced
        // Vehicle/Customer row.
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

    private async Task<Vehicle> SeedVehicleAsync(bool softDeleted = false, string registrationNumber = "CA123456")
    {
        var vehicle = Vehicle.Create(registrationNumber, "Toyota", "Corolla", 2023, 350m);
        if (softDeleted)
        {
            vehicle.SoftDelete();
        }

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Vehicles.Add(vehicle);
        await dbContext.SaveChangesAsync();

        return vehicle;
    }

    private async Task<Customer> SeedCustomerAsync(bool softDeleted = false, bool anonymized = false)
    {
        var customer = Customer.Create(
            "Jane", "Doe", $"jane.{Guid.NewGuid()}@example.com", "0821234567");

        if (anonymized)
        {
            customer.Anonymize();
        }
        else if (softDeleted)
        {
            customer.SoftDelete();
        }

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        return customer;
    }

    private async Task<Booking> SeedBookingAsync(
        Vehicle vehicle, Customer customer, DateOnly? startDate = null, DateOnly? endDate = null)
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

    [Fact]
    public async Task Get_CustomerWithBookingHistory_ReturnsIdentityContactAndFullBookingList()
    {
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleAsync();
        await SeedBookingAsync(vehicle, customer);

        using var request = AuthenticatedGet($"/api/customers/{customer.Id}/summary");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("id").GetGuid().Should().Be(customer.Id);
        document.RootElement.GetProperty("firstName").GetString().Should().Be("Jane");
        document.RootElement.GetProperty("lastName").GetString().Should().Be("Doe");
        document.RootElement.GetProperty("email").GetString().Should().Be(customer.Email);
        document.RootElement.GetProperty("phoneNumber").GetString().Should().Be(customer.PhoneNumber);
        document.RootElement.GetProperty("isAnonymized").GetBoolean().Should().BeFalse();

        var bookings = document.RootElement.GetProperty("bookings");
        bookings.GetArrayLength().Should().Be(1);

        var bookingItem = bookings[0];
        bookingItem.GetProperty("vehicleMake").GetString().Should().Be("Toyota");
        bookingItem.GetProperty("vehicleModel").GetString().Should().Be("Corolla");
        bookingItem.GetProperty("vehicleRegistrationNumber").GetString().Should().Be("CA123456");
        bookingItem.GetProperty("status").GetString().Should().Be("Active");
        bookingItem.GetProperty("totalPrice").GetDecimal().Should().Be(1400m);
    }

    [Fact]
    public async Task Get_CustomerWithZeroBookings_ReturnsIdentityContactWithEmptyBookingList()
    {
        var customer = await SeedCustomerAsync();

        using var request = AuthenticatedGet($"/api/customers/{customer.Id}/summary");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("firstName").GetString().Should().Be("Jane");
        document.RootElement.GetProperty("bookings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Get_AnonymizedCustomer_ReturnsIsAnonymizedTrue_AndStillIncludesBookingHistory()
    {
        var customer = await SeedCustomerAsync(anonymized: true);
        var vehicle = await SeedVehicleAsync();
        await SeedBookingAsync(vehicle, customer);

        using var request = AuthenticatedGet($"/api/customers/{customer.Id}/summary");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("isAnonymized").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("bookings").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Get_DeactivatedCustomer_StillReturns200WithSummary()
    {
        var customer = await SeedCustomerAsync(softDeleted: true);

        using var request = AuthenticatedGet($"/api/customers/{customer.Id}/summary");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("id").GetGuid().Should().Be(customer.Id);
    }

    [Fact]
    public async Task Get_BookingWhoseVehicleIsNowSoftDeleted_StillAppearsInBookingHistory()
    {
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleAsync(softDeleted: true, registrationNumber: "CA654321");
        await SeedBookingAsync(vehicle, customer);

        using var request = AuthenticatedGet($"/api/customers/{customer.Id}/summary");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var bookings = document.RootElement.GetProperty("bookings");
        bookings.GetArrayLength().Should().Be(1);
        bookings[0].GetProperty("vehicleRegistrationNumber").GetString().Should().Be("CA654321");
    }

    [Fact]
    public async Task Get_NonexistentCustomerId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedGet($"/api/customers/{missingId}/summary");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_MissingApiKey_Returns401_FallbackPolicyStillApplies()
    {
        var customer = await SeedCustomerAsync();

        var response = await _client.GetAsync($"/api/customers/{customer.Id}/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
