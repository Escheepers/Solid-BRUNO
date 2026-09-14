using BrunoVehicleHire.Infrastructure.Security;
using FluentAssertions;

namespace BrunoVehicleHire.Infrastructure.Tests.Security;

/// <summary>
/// Proves <see cref="EmailHasher.Compute"/>'s normalization: two emails that are the same
/// real-world address but differ only in case and/or surrounding whitespace must hash identically,
/// so <c>AppDbContext</c>'s <c>EmailHash</c> unique index actually catches the duplicate (spec-3-1's
/// whole reason for this type existing). Written test-first, before <see cref="EmailHasher"/> existed.
/// </summary>
public class EmailHasherTests
{
    [Fact]
    public void Compute_SameEmailTwice_ReturnsSameHash()
    {
        var first = EmailHasher.Compute("jane@example.com");
        var second = EmailHasher.Compute("jane@example.com");

        first.Should().Be(second);
    }

    [Fact]
    public void Compute_DifferentCaseAndPaddedWhitespace_ReturnsSameHashAsNormalizedForm()
    {
        var normalized = EmailHasher.Compute("jane@example.com");
        var differentCase = EmailHasher.Compute("Jane@Example.com");
        var paddedWhitespace = EmailHasher.Compute(" jane@example.com ");

        differentCase.Should().Be(normalized);
        paddedWhitespace.Should().Be(normalized);
    }

    [Fact]
    public void Compute_DifferentEmails_ReturnDifferentHashes()
    {
        var first = EmailHasher.Compute("jane@example.com");
        var second = EmailHasher.Compute("john@example.com");

        first.Should().NotBe(second);
    }

    [Fact]
    public void Compute_ReturnsNonEmptyHexString()
    {
        var hash = EmailHasher.Compute("jane@example.com");

        hash.Should().NotBeNullOrEmpty();
        hash.Should().MatchRegex("^[0-9A-F]+$");
    }
}
