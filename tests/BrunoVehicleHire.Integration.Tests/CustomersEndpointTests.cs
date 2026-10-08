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
    private async Task SeedBookingForCustomerAsync(Customer customer, BookingStatus status = BookingStatus.Active)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vehicle = Vehicle.Create("CA999999", "Toyota", "Corolla", 2023, 350m);
        dbContext.Vehicles.Add(vehicle);

        var booking = Booking.Create(
            vehicle.Id, customer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), 1500m);
        dbContext.Bookings.Add(booking);

        await dbContext.SaveChangesAsync();

        if (status != BookingStatus.Active)
        {
            // Booking.Create always starts Active; flip the column directly to reach a history state
            // (exactly as BookingsEndpointTests.SeedBookingWithStatusAsync does).
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Bookings" SET "Status" = {status.ToString()} WHERE "Id" = {booking.Id}""");
        }
    }

    /// <summary>
    /// Regression test (spec-search-multi-word): a search with a space ("Ericka U") used to return
    /// nothing, because the WHOLE term was tested against FirstName and LastName separately and is a
    /// substring of neither. Now each space-separated word must match FirstName or LastName, in any
    /// order and case, so "Ericka U" finds "Ericka Ullrich" (and only her).
    /// </summary>
    [Theory]
    [InlineData("Ericka U")]
    [InlineData("ericka u")]
    [InlineData("  Ericka    Ull  ")]
    [InlineData("Ullrich Ericka")]
    public async Task Get_SearchWithSeveralWords_EveryWordMustMatchFirstOrLastName_InAnyOrderAndCase(string search)
    {
        await SeedCustomersAsync(
            Customer.Create("Ericka", "Ullrich", "ericka.ullrich@example.com", "0821110101"),
            Customer.Create("Ericka", "Smith", "ericka.smith@example.com", "0821110102"),
            Customer.Create("Bob", "Ullrich", "bob.ullrich@example.com", "0821110103"));

        using var request = AuthenticatedGet(
            $"/api/customers?page=1&pageSize=20&search={Uri.EscapeDataString(search)}");
        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        var item = document.RootElement.GetProperty("items")[0];
        item.GetProperty("firstName").GetString().Should().Be("Ericka");
        item.GetProperty("lastName").GetString().Should().Be("Ullrich");
    }

    [Fact]
    public async Task Get_SearchWithOneWord_StillMatchesEitherName()
    {
        await SeedCustomersAsync(
            Customer.Create("Ericka", "Ullrich", "ericka.ullrich@example.com", "0821110101"),
            Customer.Create("Ericka", "Smith", "ericka.smith@example.com", "0821110102"),
            Customer.Create("Bob", "Ullrich", "bob.ullrich@example.com", "0821110103"));

        using var request = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=Ullrich");
        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Regression test (spec-new-rows-visible-first): lists used to be ordered oldest-first, so a
    /// customer created through the UI landed on the LAST page and looked as if nothing had been
    /// created. The list must now be newest-first, so the new row is always on page 1.
    /// </summary>
    [Fact]
    public async Task Get_AfterCreatingANewCustomer_ListsNewestFirst_SoItAppearsOnPageOne()
    {
        await SeedCustomersAsync(
            Customer.Create("Old", "One", "old.one@example.com", "0821111111", new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))),
            Customer.Create("Old", "Two", "old.two@example.com", "0822222222", new FixedTimeProvider(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero))),
            Customer.Create("Old", "Three", "old.three@example.com", "0823333333", new FixedTimeProvider(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero))));

        using var createRequest = AuthenticatedPost("/api/customers", new
        {
            firstName = "Brand",
            lastName = "New",
            email = "brand.new@example.com",
            phoneNumber = "0824444444",
        });
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var listRequest = AuthenticatedGet("/api/customers?page=1&pageSize=2");
        var listResponse = await _client.SendAsync(listRequest);
        var json = await listResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var emails = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("email").GetString())
            .ToList();

        emails.Should().Equal(
            new[] { "brand.new@example.com", "old.three@example.com" },
            "the list is newest-first, so the just-created customer is first");
        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(4);
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

    /// <summary>
    /// Regression test for the adversarial-found bug (spec-search-wildcard-escaping): the raw
    /// <c>search</c> string used to be interpolated straight into the <c>ILIKE</c> pattern, so a
    /// bare <c>"%"</c> became the pattern <c>"%%%"</c> (matches anything) and a bare <c>"_"</c>
    /// became <c>"%_%"</c> (also matches any non-empty string, since <c>_</c> matches any single
    /// character). Both must now match only rows containing that character literally. Mirrors
    /// <see cref="VehiclesEndpointTests.Get_SearchWithLiteralPercentOrUnderscore_TreatsThemLiterally_NeverAsWildcards"/>.
    /// </summary>
    [Fact]
    public async Task Get_SearchWithLiteralPercentOrUnderscore_TreatsThemLiterally_NeverAsWildcards()
    {
        var percentCustomer = Customer.Create("50%", "Off", "percent@example.com", "0821110001");
        var underscoreCustomer = Customer.Create("CA_123", "Test", "underscore@example.com", "0821110002");
        var decoyCustomer = Customer.Create("CAX123", "Similar", "decoy@example.com", "0821110003");
        var plainCustomer = Customer.Create("Alice", "Anderson", "alice.plain@example.com", "0821110004");

        await SeedCustomersAsync(percentCustomer, underscoreCustomer, decoyCustomer, plainCustomer);

        // A bare "%" must match only the row with a literal "%" -- not every row (4 seeded here).
        using var percentOnlyRequest =
            AuthenticatedGet($"/api/customers?page=1&pageSize=20&search={Uri.EscapeDataString("%")}");
        var percentOnlyResponse = await _client.SendAsync(percentOnlyRequest);
        var percentOnlyJson = await percentOnlyResponse.Content.ReadAsStringAsync();
        using var percentOnlyDocument = JsonDocument.Parse(percentOnlyJson);

        percentOnlyDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        percentOnlyDocument.RootElement.GetProperty("items")[0].GetProperty("firstName").GetString()
            .Should().Be("50%");

        // A bare "_" must match only the row with a literal "_" -- not the same-length decoy
        // first name "CAX123" that a single-char wildcard would otherwise also match.
        using var underscoreOnlyRequest =
            AuthenticatedGet($"/api/customers?page=1&pageSize=20&search={Uri.EscapeDataString("_")}");
        var underscoreOnlyResponse = await _client.SendAsync(underscoreOnlyRequest);
        var underscoreOnlyJson = await underscoreOnlyResponse.Content.ReadAsStringAsync();
        using var underscoreOnlyDocument = JsonDocument.Parse(underscoreOnlyJson);

        underscoreOnlyDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        underscoreOnlyDocument.RootElement.GetProperty("items")[0].GetProperty("firstName").GetString()
            .Should().Be("CA_123");

        // The full literal first name containing "_" must match itself exactly, and must NOT also
        // match the decoy "CAX123" (same length, "_" replaced with a real character).
        using var underscoreFullRequest =
            AuthenticatedGet($"/api/customers?page=1&pageSize=20&search={Uri.EscapeDataString("CA_123")}");
        var underscoreFullResponse = await _client.SendAsync(underscoreFullRequest);
        var underscoreFullJson = await underscoreFullResponse.Content.ReadAsStringAsync();
        using var underscoreFullDocument = JsonDocument.Parse(underscoreFullJson);

        underscoreFullDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        underscoreFullDocument.RootElement.GetProperty("items")[0].GetProperty("firstName").GetString()
            .Should().Be("CA_123");
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

    /// <summary>
    /// The QA-found race this fix exists for: two near-simultaneous Create requests for the SAME
    /// email address, neither of which existed before either request started, fired concurrently via
    /// <see cref="Task.WhenAll(Task[])"/> against the real ASP.NET Core pipeline and its own
    /// ephemeral Postgres container (mirrors <c>VehiclesEndpointTests</c>'s own equivalent proof).
    /// Before the fix, the loser crashed with an unhandled 500 from the EmailHash unique-index
    /// violation; after it, the loser must get the identical 409 the sequential-duplicate case
    /// already returns.
    /// </summary>
    [Fact]
    public async Task Post_TwoConcurrentRequestsWithSameEmail_ExactlyOneSucceeds_TheOtherReturns409NotServerError()
    {
        object Body() => new
        {
            firstName = "Race",
            lastName = "Condition",
            email = "race.condition@example.com",
            phoneNumber = "0821239876",
        };

        using var requestA = AuthenticatedPost("/api/customers", Body());
        using var requestB = AuthenticatedPost("/api/customers", Body());

        var responses = await Task.WhenAll(_client.SendAsync(requestA), _client.SendAsync(requestB));

        responses.Should().NotContain(response => response.StatusCode == HttpStatusCode.InternalServerError);
        responses.Count(response => response.StatusCode == HttpStatusCode.Created).Should().Be(
            1, "exactly one of the two concurrently-racing creates must win");

        var rejected = responses.Single(response => response.StatusCode != HttpStatusCode.Created);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await rejected.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This email address is already in use.");

        foreach (var response in responses)
        {
            response.Dispose();
        }
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
    [InlineData("08212345a7")]
    [InlineData("082 123 4567")]
    [InlineData("08212345678")]
    public async Task Post_PhoneNumberWithLettersOrMoreThanTenDigits_Returns400WithPhoneNumberInErrors(
        string phoneNumber)
    {
        using var request = AuthenticatedPost("/api/customers", new
        {
            firstName = "Jane",
            lastName = "Doe",
            email = "jane.phone@example.com",
            phoneNumber,
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("errors").TryGetProperty("PhoneNumber", out _).Should().BeTrue();
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
    public async Task Delete_CustomerWithOnlyFinishedBookings_Returns409WithDeactivateOrEraseMessage()
    {
        var customer = Customer.Create("Has", "Bookings", "has.bookings@example.com", "0821238888");
        await SeedCustomersAsync(customer);
        await SeedBookingForCustomerAsync(customer, BookingStatus.Completed);

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
    public async Task Delete_CustomerWithAnActiveBooking_Returns409TellingTheUserToCancelItFirst()
    {
        var customer = Customer.Create("Active", "Hirer", "active.hirer@example.com", "0821238889");
        await SeedCustomersAsync(customer);
        await SeedBookingForCustomerAsync(customer);

        using var request = AuthenticatedDelete($"/api/customers/{customer.Id}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("This customer has an active or upcoming booking — cancel it first, or wait for it to complete.");
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

    /// <summary>
    /// Counts raw "Customers" rows for the given id via a direct <see cref="NpgsqlConnection"/> --
    /// used only to confirm a repeated Anonymize call updates the existing row in place rather than
    /// somehow duplicating it.
    /// </summary>
    private async Task<int> CountCustomerRowsAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM "Customers"
            WHERE "Id" = @id
            """;
        command.Parameters.AddWithValue("id", id);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Anonymize_ActiveCustomer_Returns204_ScrubsPiiAndSetsIsAnonymized_AndDisappearsFromDefaultList()
    {
        var customer = Customer.Create("Erase", "Me", "erase.me@example.com", "0821223333");
        await SeedCustomersAsync(customer);

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/anonymize");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Prove the write really committed -- a subsequent GET must no longer see it by default.
        using var getRequest = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=Erase");
        var getResponse = await _client.SendAsync(getRequest);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDocument = JsonDocument.Parse(getJson);

        getDocument.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);

        using var verifyScope = _factory.Services.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var anonymized = await dbContext.Customers
            .IgnoreQueryFilters()
            .Where(c => c.Id == customer.Id)
            .SingleAsync();

        anonymized.IsAnonymized.Should().BeTrue();
        anonymized.IsDeleted.Should().BeFalse();
        anonymized.FirstName.Should().Be("Anonymized");
        anonymized.LastName.Should().Be("Customer");
        anonymized.Email.Should().Be($"erased-{customer.Id}@anonymized.local");
        anonymized.PhoneNumber.Should().Be("0000000000");
    }

    [Fact]
    public async Task Anonymize_DeactivatedCustomer_Returns204_ScrubsPii_AndLeavesIsDeletedTrue()
    {
        var customer = Customer.Create("Erase", "Deactivated", "erase.deactivated@example.com", "0821224444");
        customer.SoftDelete();
        await SeedCustomersAsync(customer);

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/anonymize");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var verifyScope = _factory.Services.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var anonymized = await dbContext.Customers
            .IgnoreQueryFilters()
            .Where(c => c.Id == customer.Id)
            .SingleAsync();

        anonymized.IsAnonymized.Should().BeTrue();
        anonymized.IsDeleted.Should().BeTrue();
        anonymized.FirstName.Should().Be("Anonymized");
        anonymized.Email.Should().Be($"erased-{customer.Id}@anonymized.local");
    }

    [Fact]
    public async Task Anonymize_CalledTwice_Returns204BothTimes_NoErrorAndNoFurtherChange()
    {
        // The mandatory DB-level proof (Design Notes): the second anonymize call must not throw
        // DbUpdateException from the unfiltered EmailHash unique index, since the placeholder email
        // embeds the customer's own Id and therefore hashes uniquely per row even across repeats.
        var customer = Customer.Create("Erase", "Twice", "erase.twice@example.com", "0821225555");
        await SeedCustomersAsync(customer);

        using var firstRequest = AuthenticatedPost($"/api/customers/{customer.Id}/anonymize");
        var firstResponse = await _client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var secondRequest = AuthenticatedPost($"/api/customers/{customer.Id}/anonymize");
        var secondResponse = await _client.SendAsync(secondRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await CountCustomerRowsAsync(customer.Id)).Should().Be(1);

        using var verifyScope = _factory.Services.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var anonymized = await dbContext.Customers
            .IgnoreQueryFilters()
            .Where(c => c.Id == customer.Id)
            .SingleAsync();

        anonymized.IsAnonymized.Should().BeTrue();
        anonymized.Email.Should().Be($"erased-{customer.Id}@anonymized.local");
    }

    /// <summary>
    /// Two DIFFERENT customers anonymized back-to-back: proves the unfiltered EmailHash unique
    /// index tolerates anonymizing multiple customers, not just the same one twice -- each
    /// placeholder email embeds its own customer's Id, so the hashes never collide with each other
    /// (Design Notes).
    /// </summary>
    [Fact]
    public async Task Anonymize_TwoDifferentCustomersBackToBack_BothSucceed_EmailHashUniqueIndexTolerates()
    {
        var first = Customer.Create("Erase", "First", "erase.first@example.com", "0821226666");
        var second = Customer.Create("Erase", "Second", "erase.second@example.com", "0821227777");
        await SeedCustomersAsync(first, second);

        using var firstRequest = AuthenticatedPost($"/api/customers/{first.Id}/anonymize");
        var firstResponse = await _client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var secondRequest = AuthenticatedPost($"/api/customers/{second.Id}/anonymize");
        var secondResponse = await _client.SendAsync(secondRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Email"
            FROM "Customers"
            WHERE "Id" = ANY(@ids)
            """;
        command.Parameters.AddWithValue("ids", new[] { first.Id, second.Id });

        await using var reader = await command.ExecuteReaderAsync();
        var rawEmails = new List<string>();
        while (await reader.ReadAsync())
        {
            rawEmails.Add(reader.GetString(0));
        }

        rawEmails.Should().HaveCount(2);
        rawEmails[0].Should().NotBe(rawEmails[1]);
    }

    [Fact]
    public async Task Anonymize_NonexistentCustomerId_Returns404()
    {
        var missingId = Guid.NewGuid();

        using var request = AuthenticatedPost($"/api/customers/{missingId}/anonymize");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_ShowInactiveTrue_ReturnsAnonymizedCustomerWithIsAnonymizedFlag()
    {
        var customer = Customer.Create("Anonymized", "Listing", "anonymized.listing@example.com", "0821228888");
        customer.Anonymize();
        await SeedCustomersAsync(customer);

        using var request =
            AuthenticatedGet("/api/customers?page=1&pageSize=20&showInactive=true&search=Customer");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
        var anonymizedItem = items.Single(item => item.GetProperty("id").GetGuid() == customer.Id);

        anonymizedItem.GetProperty("isAnonymized").GetBoolean().Should().BeTrue();
        anonymizedItem.GetProperty("firstName").GetString().Should().Be("Anonymized");
        anonymizedItem.GetProperty("lastName").GetString().Should().Be("Customer");
    }

    [Fact]
    public async Task Get_ShowInactiveOmittedOrFalse_StillExcludesAnonymizedCustomers()
    {
        var activeCustomer = Customer.Create("StillVisible", "One", "stillvisible.one@example.com", "0821229999");
        var anonymizedCustomer = Customer.Create(
            "StillVisible", "Two", "stillvisible.two@example.com", "0821220000");
        anonymizedCustomer.Anonymize();

        await SeedCustomersAsync(activeCustomer, anonymizedCustomer);

        using var request = AuthenticatedGet("/api/customers?page=1&pageSize=20&search=StillVisible");
        var response = await _client.SendAsync(request);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("items")[0].GetProperty("lastName").GetString()
            .Should().Be("One");
    }

    // ---- A customer with an Active booking can be neither deactivated nor erased (mirrors the vehicle
    // rule). Booking history (Completed/Cancelled) never blocks either action.

    private const string ActiveBookingBlocksMessage =
        "This customer has an active or upcoming booking — cancel it first, or wait for it to complete.";

    private async Task<JsonElement> ListedCustomerAsync(Guid id, string search)
    {
        using var request = AuthenticatedGet(
            $"/api/customers?page=1&pageSize=20&showInactive=true&search={Uri.EscapeDataString(search)}");
        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == id).Clone();
    }

    [Fact]
    public async Task Deactivate_CustomerWithActiveBooking_Returns409_AndTheCustomerStaysActive()
    {
        var customer = Customer.Create("Blocked", "Deactivate", "blocked.deactivate@example.com", "0821110055");
        await SeedCustomersAsync(customer);
        await SeedBookingForCustomerAsync(customer);

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/deactivate");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await DetailOf(response)).Should().Be(ActiveBookingBlocksMessage);

        (await ListedCustomerAsync(customer.Id, "Blocked")).GetProperty("isDeleted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Anonymize_CustomerWithActiveBooking_Returns409_AndPersonalDataIsUntouched()
    {
        var customer = Customer.Create("Blocked", "Erase", "blocked.erase@example.com", "0821110066");
        await SeedCustomersAsync(customer);
        await SeedBookingForCustomerAsync(customer);

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/anonymize");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await DetailOf(response)).Should().Be(ActiveBookingBlocksMessage);

        var listed = await ListedCustomerAsync(customer.Id, "Blocked");
        listed.GetProperty("isAnonymized").GetBoolean().Should().BeFalse();
        listed.GetProperty("firstName").GetString().Should().Be("Blocked");
        listed.GetProperty("email").GetString().Should().Be("blocked.erase@example.com");
    }

    [Theory]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Completed)]
    public async Task Deactivate_CustomerWithOnlyBookingHistory_Returns204(BookingStatus status)
    {
        var customer = Customer.Create("History", "Deactivate", "history.deactivate@example.com", "0821110077");
        await SeedCustomersAsync(customer);
        await SeedBookingForCustomerAsync(customer, status);

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/deactivate");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Completed)]
    public async Task Anonymize_CustomerWithOnlyBookingHistory_Returns204_AndTheHistoryIsKept(BookingStatus status)
    {
        var customer = Customer.Create("History", "Erase", "history.erase@example.com", "0821110088");
        await SeedCustomersAsync(customer);
        await SeedBookingForCustomerAsync(customer, status);

        using var request = AuthenticatedPost($"/api/customers/{customer.Id}/anonymize");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListedCustomerAsync(customer.Id, "Anonymized")).GetProperty("isAnonymized").GetBoolean()
            .Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await dbContext.Bookings.CountAsync(b => b.CustomerId == customer.Id)).Should().Be(1);
    }

    // ---- Customer now carries the xmin concurrency token (like Vehicle/Booking): a double-click on any
    // mutation must give the loser a clean 4xx, never a 500 and never a silent second "success".

    private static async Task<string> DetailOf(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("detail").GetString()!;
    }

    [Fact]
    public async Task Restore_TwoConcurrentRequestsOnSameSoftDeletedCustomer_ExactlyOneSucceeds_TheOtherReturns409()
    {
        var customer = Customer.Create("Race", "Restore", "race.restore@example.com", "0821110011");
        customer.SoftDelete();
        await SeedCustomersAsync(customer);

        using var requestA = AuthenticatedPost($"/api/customers/{customer.Id}/restore");
        using var requestB = AuthenticatedPost($"/api/customers/{customer.Id}/restore");

        var responses = await Task.WhenAll(_client.SendAsync(requestA), _client.SendAsync(requestB));

        responses.Should().NotContain(response => response.StatusCode == HttpStatusCode.InternalServerError);
        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).Should().Be(
            1, "exactly one of the two concurrently-racing restores must win");

        var rejected = responses.Single(response => response.StatusCode != HttpStatusCode.NoContent);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await DetailOf(rejected)).Should().Be("Already active.");

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Deactivate_TwoConcurrentRequestsOnSameCustomer_ExactlyOneSucceeds_TheOtherIs404Or409()
    {
        var customer = Customer.Create("Race", "Deactivate", "race.deactivate@example.com", "0821110022");
        await SeedCustomersAsync(customer);

        using var requestA = AuthenticatedPost($"/api/customers/{customer.Id}/deactivate");
        using var requestB = AuthenticatedPost($"/api/customers/{customer.Id}/deactivate");

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

    [Fact]
    public async Task Delete_TwoConcurrentRequestsOnSameCustomer_ExactlyOneSucceeds_TheOtherIs404Or409()
    {
        var customer = Customer.Create("Race", "Delete", "race.delete@example.com", "0821110033");
        await SeedCustomersAsync(customer);

        using var requestA = AuthenticatedDelete($"/api/customers/{customer.Id}");
        using var requestB = AuthenticatedDelete($"/api/customers/{customer.Id}");

        var responses = await Task.WhenAll(_client.SendAsync(requestA), _client.SendAsync(requestB));

        responses.Should().NotContain(response => response.StatusCode == HttpStatusCode.InternalServerError);
        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).Should().Be(
            1, "exactly one of the two concurrently-racing deletes must win");
        responses.Single(response => response.StatusCode != HttpStatusCode.NoContent).StatusCode
            .Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Conflict);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Anonymize_TwoConcurrentRequestsOnSameCustomer_NeverA500_AndTheCustomerEndsUpAnonymized()
    {
        var customer = Customer.Create("Race", "Erase", "race.erase@example.com", "0821110044");
        await SeedCustomersAsync(customer);

        using var requestA = AuthenticatedPost($"/api/customers/{customer.Id}/anonymize");
        using var requestB = AuthenticatedPost($"/api/customers/{customer.Id}/anonymize");

        var responses = await Task.WhenAll(_client.SendAsync(requestA), _client.SendAsync(requestB));

        responses.Should().NotContain(response => response.StatusCode == HttpStatusCode.InternalServerError);
        responses.Should().Contain(response => response.StatusCode == HttpStatusCode.NoContent);
        responses.Where(response => response.StatusCode != HttpStatusCode.NoContent)
            .Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Conflict);

        using var listRequest = AuthenticatedGet("/api/customers?page=1&pageSize=20&showInactive=true&search=Anonymized");
        var listResponse = await _client.SendAsync(listRequest);
        var json = await listResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == customer.Id)
            .GetProperty("isAnonymized").GetBoolean().Should().BeTrue();

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
