using BrunoVehicleHire.Domain.Exceptions;

namespace BrunoVehicleHire.Domain.Tests;

/// <summary>
/// Covers every row of the Story 3.1 I/O matrix for <see cref="Customer"/>. Written test-first:
/// each of these failed (either "type/member does not exist" at compile time, or a wrong-behavior
/// assertion failure) before <c>Customer.Create</c> was implemented. Mirrors <c>VehicleTests.cs</c>'s
/// structure. Deliberately does NOT assert anything about email FORMAT -- that stays
/// <c>CreateCustomerCommandValidator</c>'s job, never a <see cref="Customer"/> domain invariant (see
/// spec-3-1's Boundaries).
/// </summary>
public class CustomerTests
{
    private const string ValidFirstName = "Jane";
    private const string ValidLastName = "Doe";
    private const string ValidEmail = "jane.doe@example.com";
    private const string ValidPhoneNumber = "0821234567";

    /// <summary>Minimal fixed-clock test double -- avoids adding a mocking package to Domain.Tests.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Customer CreateValidCustomer(TimeProvider? timeProvider = null) =>
        Customer.Create(ValidFirstName, ValidLastName, ValidEmail, ValidPhoneNumber, timeProvider);

    [Fact]
    public void Create_WithValidInput_ReturnsCustomerWithExpectedDefaults()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);

        var customer = CreateValidCustomer(timeProvider);

        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.False(customer.IsDeleted);
        Assert.False(customer.IsAnonymized);
        Assert.Equal(fixedNow.UtcDateTime, customer.CreatedDate);
        Assert.Equal(ValidFirstName, customer.FirstName);
        Assert.Equal(ValidLastName, customer.LastName);
        Assert.Equal(ValidEmail, customer.Email);
        Assert.Equal(ValidPhoneNumber, customer.PhoneNumber);
    }

    [Fact]
    public void Create_AssignsAVersion7Guid()
    {
        var customer = CreateValidCustomer();

        // Version 7 UUIDs encode version "7" in the high nibble of byte 7.
        var versionNibble = customer.Id.ToByteArray()[7] >> 4;
        Assert.Equal(7, versionNibble);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankFirstName_ThrowsDomainRuleViolationException(string blank)
    {
        var act = () => Customer.Create(blank, ValidLastName, ValidEmail, ValidPhoneNumber);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankLastName_ThrowsDomainRuleViolationException(string blank)
    {
        var act = () => Customer.Create(ValidFirstName, blank, ValidEmail, ValidPhoneNumber);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankEmail_ThrowsDomainRuleViolationException(string blank)
    {
        var act = () => Customer.Create(ValidFirstName, ValidLastName, blank, ValidPhoneNumber);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankPhoneNumber_ThrowsDomainRuleViolationException(string blank)
    {
        var act = () => Customer.Create(ValidFirstName, ValidLastName, ValidEmail, blank);

        Assert.Throws<DomainRuleViolationException>(act);
    }

    [Fact]
    public void Create_WithMalformedEmail_StillSucceeds_BecauseEmailFormatIsNotADomainInvariant()
    {
        // Deliberate: email FORMAT is a CreateCustomerCommandValidator (400) concern, not a
        // Customer.Create (409) domain invariant -- mirrors Vehicle's Year split.
        var customer = Customer.Create(ValidFirstName, ValidLastName, "not-an-email", ValidPhoneNumber);

        Assert.Equal("not-an-email", customer.Email);
    }

    private const string UpdatedFirstName = "John";
    private const string UpdatedLastName = "Smith";
    private const string UpdatedEmail = "john.smith@example.com";
    private const string UpdatedPhoneNumber = "0839876543";

    [Fact]
    public void Update_WithValidInput_ChangesPropertiesAndLeavesIdCreatedDateIsDeletedAndIsAnonymizedUnchanged()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        var customer = CreateValidCustomer(timeProvider);
        var originalId = customer.Id;
        var originalCreatedDate = customer.CreatedDate;

        customer.Update(UpdatedFirstName, UpdatedLastName, UpdatedEmail, UpdatedPhoneNumber);

        Assert.Equal(UpdatedFirstName, customer.FirstName);
        Assert.Equal(UpdatedLastName, customer.LastName);
        Assert.Equal(UpdatedEmail, customer.Email);
        Assert.Equal(UpdatedPhoneNumber, customer.PhoneNumber);
        Assert.Equal(originalId, customer.Id);
        Assert.Equal(originalCreatedDate, customer.CreatedDate);
        Assert.False(customer.IsDeleted);
        Assert.False(customer.IsAnonymized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankFirstName_ThrowsDomainRuleViolationException_AndLeavesCustomerUnchanged(string blank)
    {
        var customer = CreateValidCustomer();

        var act = () => customer.Update(blank, UpdatedLastName, UpdatedEmail, UpdatedPhoneNumber);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidFirstName, customer.FirstName);
        Assert.Equal(ValidLastName, customer.LastName);
        Assert.Equal(ValidEmail, customer.Email);
        Assert.Equal(ValidPhoneNumber, customer.PhoneNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankLastName_ThrowsDomainRuleViolationException_AndLeavesCustomerUnchanged(string blank)
    {
        var customer = CreateValidCustomer();

        var act = () => customer.Update(UpdatedFirstName, blank, UpdatedEmail, UpdatedPhoneNumber);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidFirstName, customer.FirstName);
        Assert.Equal(ValidLastName, customer.LastName);
        Assert.Equal(ValidEmail, customer.Email);
        Assert.Equal(ValidPhoneNumber, customer.PhoneNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankEmail_ThrowsDomainRuleViolationException_AndLeavesCustomerUnchanged(string blank)
    {
        var customer = CreateValidCustomer();

        var act = () => customer.Update(UpdatedFirstName, UpdatedLastName, blank, UpdatedPhoneNumber);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidFirstName, customer.FirstName);
        Assert.Equal(ValidLastName, customer.LastName);
        Assert.Equal(ValidEmail, customer.Email);
        Assert.Equal(ValidPhoneNumber, customer.PhoneNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankPhoneNumber_ThrowsDomainRuleViolationException_AndLeavesCustomerUnchanged(string blank)
    {
        var customer = CreateValidCustomer();

        var act = () => customer.Update(UpdatedFirstName, UpdatedLastName, UpdatedEmail, blank);

        Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal(ValidFirstName, customer.FirstName);
        Assert.Equal(ValidLastName, customer.LastName);
        Assert.Equal(ValidEmail, customer.Email);
        Assert.Equal(ValidPhoneNumber, customer.PhoneNumber);
    }

    [Fact]
    public void Update_WithMalformedEmail_StillSucceeds_BecauseEmailFormatIsNotADomainInvariant()
    {
        var customer = CreateValidCustomer();

        customer.Update(UpdatedFirstName, UpdatedLastName, "not-an-email", UpdatedPhoneNumber);

        Assert.Equal("not-an-email", customer.Email);
    }

    [Fact]
    public void SoftDelete_MarksCustomerAsDeleted()
    {
        var customer = CreateValidCustomer();

        customer.SoftDelete();

        Assert.True(customer.IsDeleted);
    }

    [Fact]
    public void SoftDelete_CalledTwice_StaysDeletedAndDoesNotThrow()
    {
        var customer = CreateValidCustomer();

        customer.SoftDelete();
        var act = () => customer.SoftDelete();

        var exception = Record.Exception(act);
        Assert.Null(exception);
        Assert.True(customer.IsDeleted);
    }

    [Fact]
    public void Restore_SoftDeletedCustomer_ClearsIsDeletedAndLeavesOtherFieldsUnchanged()
    {
        var customer = CreateValidCustomer();
        customer.SoftDelete();

        customer.Restore();

        Assert.False(customer.IsDeleted);
        Assert.Equal(ValidFirstName, customer.FirstName);
        Assert.Equal(ValidLastName, customer.LastName);
        Assert.Equal(ValidEmail, customer.Email);
        Assert.Equal(ValidPhoneNumber, customer.PhoneNumber);
    }

    [Fact]
    public void Restore_AlreadyActiveCustomer_ThrowsDomainRuleViolationException_AndLeavesIsDeletedUnchanged()
    {
        var customer = CreateValidCustomer();

        var act = () => customer.Restore();

        var exception = Assert.Throws<DomainRuleViolationException>(act);
        Assert.Equal("Already active.", exception.Message);
        Assert.Equal(nameof(Customer), exception.Entity);
        Assert.Equal(nameof(Customer.IsDeleted), exception.Rule);
        Assert.False(customer.IsDeleted);
    }

    [Fact]
    public void Anonymize_ActiveCustomer_ScrubsPiiToPlaceholdersAndSetsIsAnonymized()
    {
        var customer = CreateValidCustomer();
        var id = customer.Id;

        customer.Anonymize();

        Assert.Equal("Anonymized", customer.FirstName);
        Assert.Equal("Customer", customer.LastName);
        Assert.Equal($"erased-{id}@anonymized.local", customer.Email);
        Assert.Equal("0000000000", customer.PhoneNumber);
        Assert.True(customer.IsAnonymized);
    }

    [Fact]
    public void Anonymize_SoftDeletedCustomer_ScrubsPiiButLeavesIsDeletedUnchanged()
    {
        var customer = CreateValidCustomer();
        customer.SoftDelete();

        customer.Anonymize();

        Assert.True(customer.IsDeleted);
        Assert.True(customer.IsAnonymized);
        Assert.Equal("Anonymized", customer.FirstName);
    }

    [Fact]
    public void Anonymize_CalledTwice_StaysAnonymizedAndDoesNotThrow()
    {
        var customer = CreateValidCustomer();
        customer.Anonymize();
        var emailAfterFirstCall = customer.Email;

        var act = () => customer.Anonymize();

        var exception = Record.Exception(act);
        Assert.Null(exception);
        Assert.True(customer.IsAnonymized);
        Assert.Equal(emailAfterFirstCall, customer.Email);
        Assert.Equal("Anonymized", customer.FirstName);
        Assert.Equal("Customer", customer.LastName);
        Assert.Equal("0000000000", customer.PhoneNumber);
    }

    [Fact]
    public void Restore_AfterAnonymize_ClearsIsDeletedButLeavesIsAnonymizedUnchanged()
    {
        var customer = CreateValidCustomer();
        customer.SoftDelete();
        customer.Anonymize();

        customer.Restore();

        Assert.False(customer.IsDeleted);
        Assert.True(customer.IsAnonymized);
    }

    [Fact]
    public void Customer_HasNoPublicSetters()
    {
        var propertiesWithPublicSetters = typeof(Customer)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }
}
