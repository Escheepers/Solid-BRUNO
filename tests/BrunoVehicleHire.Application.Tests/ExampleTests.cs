using BrunoVehicleHire.Application;
using FluentAssertions;

namespace BrunoVehicleHire.Application.Tests;

/// <summary>
/// Placeholder proving the Application.Tests -> Application reference and the
/// FluentAssertions/NSubstitute tooling both work end-to-end. Replaced by real
/// CQRS handler tests starting with later stories.
/// </summary>
public class ExampleTests
{
    [Fact]
    public void ApplicationProject_IsReferencedAndInstantiable()
    {
        var instance = new Class1();

        instance.Should().NotBeNull();
    }
}
