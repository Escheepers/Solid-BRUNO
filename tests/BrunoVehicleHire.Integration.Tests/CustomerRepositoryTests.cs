using BrunoVehicleHire.Domain;
using BrunoVehicleHire.Infrastructure.Persistence;
using BrunoVehicleHire.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// Proves <see cref="CustomerRepository"/> against a real, ephemeral Postgres container -- not a
/// mock. Written test-first (before <c>CustomerRepository</c> existed). Two things specifically
/// need a real database rather than an in-memory provider: <c>ExistsByEmailAsync</c>'s normalization
/// (proving a differently-cased/whitespace-padded duplicate email is still caught by the real
/// EmailHash unique index + <c>AppDbContext.SaveChangesAsync</c>'s override), and
/// <c>GetPagedAsync</c>'s search, which must match FirstName/LastName only -- never Email/
/// PhoneNumber, since those are encrypted and pattern-matching ciphertext would be meaningless
/// (spec-3-1's Scope decision 2).
/// </summary>
public class CustomerRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        // An ephemeral provider is fine here -- these tests never restart the process, so key
        // persistence across instances is unnecessary; a fresh CreateDbContext() call within the
        // same test still shares this instance's keys as long as the caller reuses it correctly
        // (each test creates its own dbContext per phase, matching VehicleMigrationTests' pattern).
        return new AppDbContext(options, _dataProtectionProvider);
    }

    private readonly IDataProtectionProvider _dataProtectionProvider = new EphemeralDataProtectionProvider();

    private static Customer CreateValidCustomer(
        string firstName = "Jane",
        string lastName = "Doe",
        string email = "jane.doe@example.com",
        string phoneNumber = "0821234567") =>
        Customer.Create(firstName, lastName, email, phoneNumber);

    [Fact]
    public async Task ExistsByEmailAsync_NoMatchingEmail_ReturnsFalse()
    {
        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var exists = await repository.ExistsByEmailAsync("nobody@example.com", excludingId: null, CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsByEmailAsync_ExactMatchingEmail_ReturnsTrue()
    {
        await using var seedContext = CreateDbContext();
        seedContext.Customers.Add(CreateValidCustomer(email: "duplicate@example.com"));
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var exists = await repository.ExistsByEmailAsync("duplicate@example.com", excludingId: null, CancellationToken.None);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsByEmailAsync_DifferentCaseAndPaddedWhitespace_StillReturnsTrue_ProvingNormalization()
    {
        await using var seedContext = CreateDbContext();
        seedContext.Customers.Add(CreateValidCustomer(email: "Jane.Doe@Example.com"));
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var exists = await repository.ExistsByEmailAsync(" jane.doe@example.com ", excludingId: null, CancellationToken.None);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsByEmailAsync_ExcludingOwnId_ReturnsFalse()
    {
        await using var seedContext = CreateDbContext();
        var customer = CreateValidCustomer(email: "self@example.com");
        seedContext.Customers.Add(customer);
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var exists = await repository.ExistsByEmailAsync("self@example.com", excludingId: customer.Id, CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_ThenGetPaged_ReturnsTheAddedCustomer()
    {
        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        await repository.AddAsync(CreateValidCustomer(email: "added@example.com"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var (items, totalCount) = await repository.GetPagedAsync(1, 20, search: null, includeInactive: false, CancellationToken.None);

        totalCount.Should().Be(1);
        items.Should().ContainSingle(c => c.Email == "added@example.com");
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesFirstNameOrLastName_CaseInsensitivePartialMatch()
    {
        await using var seedContext = CreateDbContext();
        seedContext.Customers.AddRange(
            CreateValidCustomer(firstName: "Alice", lastName: "Anderson", email: "alice@example.com"),
            CreateValidCustomer(firstName: "Bob", lastName: "Alison", email: "bob@example.com"),
            CreateValidCustomer(firstName: "Carol", lastName: "Carter", email: "carol@example.com"));
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var (items, totalCount) = await repository.GetPagedAsync(1, 20, search: "ali", includeInactive: false, CancellationToken.None);

        totalCount.Should().Be(2);
        items.Select(c => c.Email).Should().BeEquivalentTo("alice@example.com", "bob@example.com");
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchingOnlyEmailOrPhoneNumber_ReturnsNoResults_ProvingEncryptedFieldsAreNeverSearched()
    {
        await using var seedContext = CreateDbContext();
        seedContext.Customers.Add(
            CreateValidCustomer(firstName: "Dana", lastName: "Diaz", email: "dana@example.com", phoneNumber: "0839998888"));
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        // "dana" matches the Email local-part and "9998888" matches PhoneNumber, but neither
        // FirstName ("Dana" matches by coincidence -- use a search term that ONLY appears in the
        // encrypted fields) matches. Use "example.com" (only in Email) and "9998888" (only in
        // PhoneNumber) to prove neither is searched.
        var (byEmailFragment, emailTotal) =
            await repository.GetPagedAsync(1, 20, search: "example.com", includeInactive: false, CancellationToken.None);
        var (byPhoneFragment, phoneTotal) =
            await repository.GetPagedAsync(1, 20, search: "9998888", includeInactive: false, CancellationToken.None);

        emailTotal.Should().Be(0);
        byEmailFragment.Should().BeEmpty();
        phoneTotal.Should().Be(0);
        byPhoneFragment.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_ExistingCustomer_ReturnsMatchingCustomer()
    {
        await using var seedContext = CreateDbContext();
        var customer = CreateValidCustomer(email: "findme@example.com");
        seedContext.Customers.Add(customer);
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var found = await repository.GetByIdAsync(customer.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(customer.Id);
        found.Email.Should().Be("findme@example.com");
    }

    [Fact]
    public async Task GetByIdAsync_NonexistentId_ReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var found = await repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_SoftDeletedCustomer_ReturnsNull_RespectingQueryFilter()
    {
        await using var seedContext = CreateDbContext();
        var customer = CreateValidCustomer(email: "deleted@example.com");
        seedContext.Customers.Add(customer);
        await seedContext.SaveChangesAsync();
        customer.SoftDelete();
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var found = await repository.GetByIdAsync(customer.Id, CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetPagedAsync_SoftDeletedCustomerSeeded_NeverAppearsInItemsOrTotalCount()
    {
        await using var seedContext = CreateDbContext();
        var activeCustomer = CreateValidCustomer(firstName: "Eve", lastName: "Evans", email: "eve@example.com");
        seedContext.Customers.Add(activeCustomer);
        await seedContext.SaveChangesAsync();
        activeCustomer.SoftDelete();
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var (items, totalCount) = await repository.GetPagedAsync(1, 20, search: null, includeInactive: false, CancellationToken.None);

        totalCount.Should().Be(0);
        items.Should().BeEmpty();
    }

    /// <summary>
    /// Proves <c>GetPagedAsync</c>'s <c>includeInactive: true</c> ignores the soft-delete query
    /// filter (Story 3.4) -- mirrors <c>VehicleRepository</c>'s exact Story-2.4 behavior.
    /// </summary>
    [Fact]
    public async Task GetPagedAsync_IncludeInactiveTrue_ReturnsBothActiveAndSoftDeletedCustomers()
    {
        await using var seedContext = CreateDbContext();
        var activeCustomer = CreateValidCustomer(firstName: "Frank", lastName: "Foster", email: "frank@example.com");
        var deletedCustomer = CreateValidCustomer(firstName: "Grace", lastName: "Green", email: "grace@example.com");
        seedContext.Customers.AddRange(activeCustomer, deletedCustomer);
        await seedContext.SaveChangesAsync();
        deletedCustomer.SoftDelete();
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var (items, totalCount) = await repository.GetPagedAsync(1, 20, search: null, includeInactive: true, CancellationToken.None);

        totalCount.Should().Be(2);
        items.Select(c => c.Email).Should().BeEquivalentTo("frank@example.com", "grace@example.com");
    }

    /// <summary>
    /// Proves <c>GetByIdIncludingSoftDeletedAsync</c> finds a soft-deleted customer that
    /// <c>GetByIdAsync</c>'s filtered query would incorrectly treat as not found -- mirrors
    /// <c>VehicleRepository</c>'s exact Story-2.4 behavior; <c>RestoreCustomerCommandHandler</c>
    /// relies on exactly this.
    /// </summary>
    [Fact]
    public async Task GetByIdIncludingSoftDeletedAsync_SoftDeletedCustomer_StillReturnsIt()
    {
        await using var seedContext = CreateDbContext();
        var customer = CreateValidCustomer(email: "restoreme@example.com");
        seedContext.Customers.Add(customer);
        await seedContext.SaveChangesAsync();
        customer.SoftDelete();
        await seedContext.SaveChangesAsync();

        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var found = await repository.GetByIdIncludingSoftDeletedAsync(customer.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(customer.Id);
        found.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdIncludingSoftDeletedAsync_NonexistentId_ReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var repository = new CustomerRepository(dbContext);

        var found = await repository.GetByIdIncludingSoftDeletedAsync(Guid.NewGuid(), CancellationToken.None);

        found.Should().BeNull();
    }
}
