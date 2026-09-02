using BrunoVehicleHire.Domain;

namespace BrunoVehicleHire.Domain.Tests;

/// <summary>
/// Placeholder proving the Domain.Tests -> Domain reference and the xUnit
/// harness both work end-to-end. Replaced by real Domain model tests
/// starting with Story 1.3.
/// </summary>
public class ExampleTests
{
    [Fact]
    public void DomainProject_IsReferencedAndInstantiable()
    {
        var instance = new Vehicle();

        Assert.NotNull(instance);
    }
}
