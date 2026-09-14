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
/// End-to-end proof of Story 3.1's Customer slice against a real ASP.NET Core pipeline via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> -- not a mock -- backed by its own ephemeral
/// Testcontainers Postgres instance (mirrors <see cref="VehiclesEndpointTests"/>'s pattern exactly).
/// Covers every backend row of spec-3-1's I/O &amp; Edge-Case Matrix, including the AC's own explicit
/// verification method: a test reading the raw "Customers" row via a direct
/// <see cref="NpgsqlConnection"/> (bypassing EF Core entirely, mirroring
/// <see cref="VehicleMigrationTests"/>'s raw-connection pattern) to confirm Email/PhoneNumber are
/// stored encrypted, never plaintext.
/// </summary>
public class CustomersEndpointTests : IAsyncLifetime
{
    private const string ConfiguredKey = "customers-endpoint-test-key";

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

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

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

    private HttpRequestMessage AuthenticatedPost(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(ApiKeyDefaults.HeaderName, ConfiguredKey);
        return request;
    }

    private HttpRequestMessage AuthenticatedDelete(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add(ApiKeyDefaults.HeaderName, ConfiguredKey);
        return request;
    }

    private async Task SeedCustomersAsync(params Customer[] customers)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Customers.AddRange(customers);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a real, FK-valid <see cref="Booking"/> referencing <paramref name="customer"/> --
    /// Postgres rejects an orphaned FK reference (see <c>BookingMigrationTests</c>), so this first
    /// creates and persists a real <see cref="Vehicle"/> row for the booking to reference too.
    /// </summary>
    private async Task SeedBookingForCustomerAsync(Customer customer)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vehicle = Vehicle.Create("CA999999", "Toyota", "Corolla", 2023, 350m);
        dbContext.Vehicles.Add(vehicle);

        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1500m);
        dbContext.Bookings.Add(booking);

        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_CustomersExist_ReturnsPagedResultShape()
    {
        await SeedCustomersAsync(
            Customer.Create("Jane", "Doe", "jane.one@example.com", "0821111111"),
            Customer.Create("John", "Smith", "john.two@example.com", "0822222222"));

        using var request = AuthenticatedGet("/api/customers?page=1&pageSize=20");
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
        firstItem.TryGetProperty("firstName", out _).Should().BeTrue();
        firstItem.TryGetProperty("lastName", out _).Should().BeTrue();
        firstItem.TryGetProperty("email", out _).Should().BeTrue();
        firstItem.TryGetProperty("phoneNumber", out _).Should().BeTrue();
        firstItem.TryGetProperty("createdDate", out _).Should().BeTrue();
        firstItem.TryGetProperty("isDeleted", out _).Should().BeTrue();
        firstItem.TryGetProperty("isAnonymized", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_SearchMatchesFirstOrLastName_CaseInsensitivePartialMatch()
    {
        await SeedCustomersAsync(
            Customer.Create("Alice", "Anderson", "alice@example.com", "0821111111"),
            Customer.Create("Bob", "Alison", "bob@example.com", "0822222222"),
            Customer.Create("Carol", "Carter", "carol@example.com", "0823333333"));

        using var request = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=ali");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var firstNames = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("firstName").GetString())
            .ToList();

        firstNames.Should().Contain("Alice");
        firstNames.Should().Contain("Bob");
        firstNames.Should().NotContain("Carol");
    }

    [Theory]
    [InlineData("page=0&pageSize=20")]
    [InlineData("page=1&pageSize=500")]
    public async Task Get_InvalidPagingParameters_Returns400WithFieldNamedInErrors(string queryString)
    {
        using var request = AuthenticatedGet($"/api/customers?{queryString}");
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
        var response = await _client.GetAsync("/api/customers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_ValidCommand_Returns201WithCustomerDto_AndCustomerIsThenRetrievableViaGet()
    {
        using var request = AuthenticatedPost("/api/customers", new
        {
            firstName = "Nomvula",
            lastName = "Mokoena",
            email = "nomvula.mokoena@example.com",
            phoneNumber = "0831234567",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("firstName").GetString().Should().Be("Nomvula");
        document.RootElement.GetProperty("lastName").GetString().Should().Be("Mokoena");
        document.RootElement.GetProperty("email").GetString().Should().Be("nomvula.mokoena@example.com");
        document.RootElement.GetProperty("phoneNumber").GetString().Should().Be("0831234567");
        document.RootElement.TryGetProperty("id", out var idProperty).Should().BeTrue();

        var createdId = idProperty.GetGuid();
        response.Headers.Location!.OriginalString.Should().Be($"/api/customers/{createdId}");

        // Prove the write really committed -- a subsequent GET must see it, not merely that the
        // POST response body looked right.
        using var getRequest = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=Nomvula");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        getDocument.RootElement.GetProperty("items")[0].GetProperty("email").GetString()
            .Should().Be("nomvula.mokoena@example.com");
    }

    [Fact]
    public async Task Post_DuplicateEmail_Returns409WithExactDetailMessage()
    {
        await SeedCustomersAsync(Customer.Create("Existing", "Customer", "existing@example.com", "0821112222"));

        using var request = AuthenticatedPost("/api/customers", new
        {
            firstName = "Another",
            lastName = "Person",
            email = "existing@example.com",
            phoneNumber = "0823334444",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This email address is already in use.");
    }

    [Fact]
    public async Task Post_DuplicateEmailDifferentCaseAndWhitespace_StillReturns409_ProvingNormalization()
    {
        await SeedCustomersAsync(Customer.Create("Existing", "Customer", "normalized@example.com", "0821112222"));

        using var request = AuthenticatedPost("/api/customers", new
        {
            firstName = "Another",
            lastName = "Person",
            email = " Normalized@Example.com ",
            phoneNumber = "0823334444",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Post_MalformedEmail_Returns400WithEmailInErrors()
    {
        using var request = AuthenticatedPost("/api/customers", new
        {
            firstName = "Jane",
            lastName = "Doe",
            email = "not-an-email",
            phoneNumber = "0821234567",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty("Email", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("   ", "Doe", "jane@example.com", "0821234567", "FirstName")]
    [InlineData("Jane", "   ", "jane@example.com", "0821234567", "LastName")]
    [InlineData("Jane", "Doe", "jane@example.com", "   ", "PhoneNumber")]
    public async Task Post_BlankRequiredField_Returns400WithFieldInErrors(
        string firstName, string lastName, string email, string phoneNumber, string expectedField)
    {
        using var request = AuthenticatedPost("/api/customers", new
        {
            firstName,
            lastName,
            email,
            phoneNumber,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty(expectedField, out _).Should().BeTrue();
    }

    /// <summary>
    /// The AC's own explicit, mandatory verification method: after creating a customer via the
    /// real HTTP API (going through the full pipeline, including the Data Protection value
    /// converter), connect directly to Postgres with a raw <see cref="NpgsqlConnection"/> --
    /// completely bypassing EF Core and its value converters -- and assert the raw Email/
    /// PhoneNumber column values are neither equal to, nor contain as a substring, the plaintext
    /// that was submitted.
    /// </summary>
    [Fact]
    public async Task Post_ValidCommand_RawDatabaseRow_HasEncryptedEmailAndPhoneNumber_NotPlaintext()
    {
        const string plaintextEmail = "raw-db-check@example.com";
        const string plaintextPhoneNumber = "0839998877";

        using var request = AuthenticatedPost("/api/customers", new
        {
            firstName = "Raw",
            lastName = "Checker",
            email = plaintextEmail,
            phoneNumber = plaintextPhoneNumber,
        });

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var createdId = document.RootElement.GetProperty("id").GetGuid();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Email", "PhoneNumber"
            FROM "Customers"
            WHERE "Id" = @id
            """;
        command.Parameters.AddWithValue("id", createdId);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        var rawEmail = reader.GetString(0);
        var rawPhoneNumber = reader.GetString(1);

        rawEmail.Should().NotBe(plaintextEmail);
        rawEmail.Should().NotContain(plaintextEmail);
        rawPhoneNumber.Should().NotBe(plaintextPhoneNumber);
        rawPhoneNumber.Should().NotContain(plaintextPhoneNumber);
    }

    [Fact]
    public async Task Put_ValidEdit_Returns200WithUpdatedDto_AndChangeIsRetrievableViaGet()
    {
        var customer = Customer.Create("Nomvula", "Mokoena", "nomvula@example.com", "0831111111");
        await SeedCustomersAsync(customer);

        using var request = AuthenticatedPut($"/api/customers/{customer.Id}", new
        {
            firstName = "Nomvula",
            lastName = "Mokoena",
            email = "nomvula@example.com",
            phoneNumber = "0839999999",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("id").GetGuid().Should().Be(customer.Id);
        document.RootElement.GetProperty("phoneNumber").GetString().Should().Be("0839999999");

        // Prove the write really committed -- a subsequent GET must see it too.
        using var getRequest = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=Nomvula");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("items")[0].GetProperty("phoneNumber").GetString()
            .Should().Be("0839999999");
    }

    [Fact]
    public async Task Put_DuplicateEmailAgainstADifferentCustomer_Returns409WithExactDetailMessage()
    {
        var otherCustomer = Customer.Create("Existing", "Customer", "existing@example.com", "0821112222");
        var customerBeingEdited = Customer.Create("Another", "Person", "another@example.com", "0823334444");
        await SeedCustomersAsync(otherCustomer, customerBeingEdited);

        using var request = AuthenticatedPut($"/api/customers/{customerBeingEdited.Id}", new
        {
            firstName = "Another",
            lastName = "Person",
            email = "existing@example.com",
            phoneNumber = "0823334444",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This email address is already in use.");
    }

    [Fact]
    public async Task Put_SubmittingCustomersOwnUnchangedEmail_Returns200NotConflict()
    {
        // Regression-proving row: the exclude-self check must not flag a customer's own current
        // Email as a duplicate of itself.
        var customer = Customer.Create("Sipho", "Ndlovu", "sipho@example.com", "0827778888");
        await SeedCustomersAsync(customer);

        using var request = AuthenticatedPut($"/api/customers/{customer.Id}", new
        {
            firstName = "Sipho",
            lastName = "Ndlovu",
            email = "sipho@example.com",
            phoneNumber = "0829990000",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("phoneNumber").GetString().Should().Be("0829990000");
    }

    [Fact]
    public async Task Put_NonexistentCustomerId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedPut($"/api/customers/{missingId}", new
        {
            firstName = "Jane",
            lastName = "Doe",
            email = "jane.doe@example.com",
            phoneNumber = "0821234567",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_BlankFirstName_Returns400WithFirstNameInErrors()
    {
        var customer = Customer.Create("Jane", "Doe", "jane.doe2@example.com", "0821234567");
        await SeedCustomersAsync(customer);

        using var request = AuthenticatedPut($"/api/customers/{customer.Id}", new
        {
            firstName = "   ",
            lastName = "Doe",
            email = "jane.doe2@example.com",
            phoneNumber = "0821234567",
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty("FirstName", out _).Should().BeTrue();
    }

    /// <summary>
    /// The AC's own explicit, mandatory post-edit verification: after seeding a customer and
    /// editing their PhoneNumber via a real HTTP PUT (going through the full pipeline, including the
    /// Data Protection value converter), connect directly to Postgres with a raw
    /// <see cref="NpgsqlConnection"/> -- completely bypassing EF Core -- and assert the raw
    /// PhoneNumber column value is neither equal to, nor contains as a substring, the NEW plaintext
    /// that was submitted. Mirrors <see cref="Post_ValidCommand_RawDatabaseRow_HasEncryptedEmailAndPhoneNumber_NotPlaintext"/>,
    /// but for an Update rather than a Create -- proving encryption still applies after an edit, not
    /// just on initial insert.
    /// </summary>
    [Fact]
    public async Task Put_ValidEdit_RawDatabaseRow_HasNewEncryptedPhoneNumber_NotPlaintext()
    {
        var customer = Customer.Create("Raw", "Checker", "raw-edit-check@example.com", "0831112222");
        await SeedCustomersAsync(customer);

        const string newPlaintextPhoneNumber = "0839998877";

        using var request = AuthenticatedPut($"/api/customers/{customer.Id}", new
        {
            firstName = "Raw",
            lastName = "Checker",
            email = "raw-edit-check@example.com",
            phoneNumber = newPlaintextPhoneNumber,
        });

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "PhoneNumber"
            FROM "Customers"
            WHERE "Id" = @id
            """;
        command.Parameters.AddWithValue("id", customer.Id);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        var rawPhoneNumber = reader.GetString(0);

        rawPhoneNumber.Should().NotBe(newPlaintextPhoneNumber);
        rawPhoneNumber.Should().NotContain(newPlaintextPhoneNumber);
    }

    [Fact]
    public async Task Delete_CustomerWithNoBookings_Returns204_AndCustomerIsPermanentlyGone()
    {
        var customer = Customer.Create("Delete", "Me", "delete.me@example.com", "0821239999");
        await SeedCustomersAsync(customer);

        using var request = AuthenticatedDelete($"/api/customers/{customer.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Prove the removal really committed -- a subsequent GET-list must no longer see it.
        using var getRequest = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=Delete");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Delete_CustomerWithAtLeastOneBooking_Returns409WithExactDetailMessage()
    {
        var customer = Customer.Create("Has", "Bookings", "has.bookings@example.com", "0821238888");
        await SeedCustomersAsync(customer);
        await SeedBookingForCustomerAsync(customer);

        using var request = AuthenticatedDelete($"/api/customers/{customer.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This customer has bookings — deactivate or erase their data instead.");

        // Prove the customer was never removed despite the failed delete attempt.
        using var getRequest = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=Bookings");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Delete_NonexistentCustomerId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedDelete($"/api/customers/{missingId}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deactivate_ActiveCustomer_Returns204_AndCustomerDisappearsFromDefaultList()
    {
        var customer = Customer.Create("Deactivate", "Me", "deactivate.me@example.com", "0821112233");
        await SeedCustomersAsync(customer);

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/deactivate");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Prove the write really committed -- a subsequent GET must no longer see it.
        using var getRequest = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=Deactivate");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
        getDocument.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);

        using var verifyScope = _factory.Services.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var isDeleted = await dbContext.Customers
            .IgnoreQueryFilters()
            .Where(c => c.Id == customer.Id)
            .Select(c => c.IsDeleted)
            .SingleAsync();
        isDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_NonexistentCustomerId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedPost($"/api/customers/{missingId}/deactivate");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deactivate_AlreadyDeactivatedCustomer_Returns404NotSilent204()
    {
        // Idempotency-adjacent: the query filter already excludes a soft-deleted customer, so a
        // second deactivate attempt on the same customer must 404, not silently succeed again.
        var customer = Customer.Create("Already", "Deactivated", "already.deactivated@example.com", "0821114444");
        customer.SoftDelete();
        await SeedCustomersAsync(customer);

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/deactivate");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Mandatory proof (spec-3-4's Critical correctness requirement 4): deactivating a customer must
    /// NEVER re-encrypt or otherwise touch the Email/PhoneNumber columns -- <c>SoftDelete()</c> only
    /// flips <c>IsDeleted</c>, and no <c>SaveChanges</c> during a deactivate touches those columns at
    /// all. Captures the raw ciphertext via <see cref="NpgsqlConnection"/> (mirrors
    /// <see cref="Post_ValidCommand_RawDatabaseRow_HasEncryptedEmailAndPhoneNumber_NotPlaintext"/>'s
    /// technique) BEFORE deactivating, then again AFTER, and asserts the two are byte-identical --
    /// the strongest possible proof, since re-encrypting the same plaintext would almost certainly
    /// produce different ciphertext (Data Protection's non-deterministic IV).
    /// </summary>
    [Fact]
    public async Task Deactivate_DoesNotTouchEncryptedPiiColumns_CiphertextIsByteIdenticalBeforeAndAfter()
    {
        var customer = Customer.Create("Pii", "Untouched", "pii.untouched@example.com", "0821115555");
        await SeedCustomersAsync(customer);

        async Task<(string Email, string PhoneNumber)> ReadRawRowAsync()
        {
            await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "Email", "PhoneNumber"
                FROM "Customers"
                WHERE "Id" = @id
                """;
            command.Parameters.AddWithValue("id", customer.Id);

            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();

            return (reader.GetString(0), reader.GetString(1));
        }

        var beforeDeactivate = await ReadRawRowAsync();

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/deactivate");
        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDeactivate = await ReadRawRowAsync();

        afterDeactivate.Email.Should().Be(beforeDeactivate.Email);
        afterDeactivate.PhoneNumber.Should().Be(beforeDeactivate.PhoneNumber);
        afterDeactivate.Email.Should().NotBe("pii.untouched@example.com");
        afterDeactivate.PhoneNumber.Should().NotBe("0821115555");
    }

    [Fact]
    public async Task Get_ShowInactiveTrue_ReturnsBothActiveAndSoftDeletedCustomers_WithIsDeletedFlag()
    {
        var activeCustomer = Customer.Create("Active", "Customer", "active.customer@example.com", "0821116666");
        var deletedCustomer = Customer.Create("Deleted", "Customer", "deleted.customer@example.com", "0821117777");
        deletedCustomer.SoftDelete();

        await SeedCustomersAsync(activeCustomer, deletedCustomer);

        using var request = AuthenticatedGet("/api/customers?page=1&pageSize=20&showInactive=true");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);

        var deletedItem = items.Single(item => item.GetProperty("lastName").GetString() == "Customer"
            && item.GetProperty("firstName").GetString() == "Deleted");
        deletedItem.GetProperty("isDeleted").GetBoolean().Should().BeTrue();

        var activeItem = items.Single(item => item.GetProperty("firstName").GetString() == "Active");
        activeItem.GetProperty("isDeleted").GetBoolean().Should().BeFalse();

        // The DTO's Email is decrypted plaintext (EF's value converter decrypts on read) -- the
        // showInactive=true path still returns the correct, fully-usable customer record, not a
        // broken/partial one. The raw-ciphertext proof lives in the dedicated
        // Deactivate_DoesNotTouchEncryptedPiiColumns test below, which reads the column directly.
        deletedItem.GetProperty("email").GetString().Should().Be("deleted.customer@example.com");
    }

    [Fact]
    public async Task Get_ShowInactiveOmittedOrFalse_StillExcludesSoftDeletedCustomers()
    {
        // Regression: the default (Story 3.1) behavior must stay exactly as before.
        var activeCustomer = Customer.Create("StillActive", "One", "stillactive.one@example.com", "0821118888");
        var deletedCustomer = Customer.Create("StillActive", "Two", "stillactive.two@example.com", "0821119999");
        deletedCustomer.SoftDelete();

        await SeedCustomersAsync(activeCustomer, deletedCustomer);

        using var omittedRequest = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=StillActive");
        var omittedResponse = await _client.SendAsync(omittedRequest);
        var omittedJson = await omittedResponse.Content.ReadAsStringAsync();
        using var omittedDocument = JsonDocument.Parse(omittedJson);

        omittedDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        omittedDocument.RootElement.GetProperty("items")[0].GetProperty("lastName").GetString()
            .Should().Be("One");

        using var explicitFalseRequest =
            AuthenticatedGet("/api/customers?page=1&pageSize=20&search=StillActive&showInactive=false");
        var explicitFalseResponse = await _client.SendAsync(explicitFalseRequest);
        var explicitFalseJson = await explicitFalseResponse.Content.ReadAsStringAsync();
        using var explicitFalseDocument = JsonDocument.Parse(explicitFalseJson);

        explicitFalseDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Restore_SoftDeletedCustomer_Returns204_AndReappearsInDefaultListing()
    {
        var customer = Customer.Create("Restore", "Me", "restore.me@example.com", "0821221111");
        customer.SoftDelete();
        await SeedCustomersAsync(customer);

        using var restoreRequest = AuthenticatedPost($"/api/customers/{customer.Id}/restore");
        var restoreResponse = await _client.SendAsync(restoreRequest);

        restoreResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var inactiveGetRequest =
            AuthenticatedGet("/api/customers?page=1&pageSize=20&showInactive=true&search=Restore");
        var inactiveGetResponse = await _client.SendAsync(inactiveGetRequest);
        var inactiveJson = await inactiveGetResponse.Content.ReadAsStringAsync();
        using var inactiveDocument = JsonDocument.Parse(inactiveJson);

        inactiveDocument.RootElement.GetProperty("items")[0].GetProperty("isDeleted").GetBoolean()
            .Should().BeFalse();

        using var defaultGetRequest = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=Restore");
        var defaultGetResponse = await _client.SendAsync(defaultGetRequest);
        var defaultJson = await defaultGetResponse.Content.ReadAsStringAsync();
        using var defaultDocument = JsonDocument.Parse(defaultJson);

        defaultDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        defaultDocument.RootElement.GetProperty("items")[0].GetProperty("lastName").GetString()
            .Should().Be("Me");
    }

    [Fact]
    public async Task Restore_AlreadyActiveCustomer_Returns409WithExactDetailMessage()
    {
        var customer = Customer.Create("Already", "Active", "already.active@example.com", "0821222222");
        await SeedCustomersAsync(customer);

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/restore");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString().Should().Be("Already active.");
    }

    [Fact]
    public async Task Restore_NonexistentCustomerId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedPost($"/api/customers/{missingId}/restore");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
