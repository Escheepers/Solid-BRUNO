using System.Reflection;
using NetArchTest.Rules;

namespace BrunoVehicleHire.Integration.Tests;

/// <summary>
/// Mechanically enforces the Clean Architecture dependency-direction rules:
/// Domain must stay dependency-free of the rest of the solution, Application
/// may depend only on Domain, and nothing in BrunoVehicleHire.Api.Controllers
/// may depend on BrunoVehicleHire.Infrastructure. Controllers must stay thin
/// and reach the Application layer only (e.g. via MediatR), never
/// Infrastructure directly.
/// </summary>
public class ArchitectureFitnessTests
{
    private static readonly Assembly ApiAssembly = Assembly.Load("BrunoVehicleHire.Api");
    private static readonly Assembly DomainAssembly = Assembly.Load("BrunoVehicleHire.Domain");
    private static readonly Assembly ApplicationAssembly = Assembly.Load("BrunoVehicleHire.Application");

    [Fact]
    public void Controllers_Should_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("BrunoVehicleHire.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOn("BrunoVehicleHire.Infrastructure")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Types violating the Controllers -> Infrastructure rule: " +
            $"{string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}");
    }

    [Fact]
    public void Domain_Should_Not_Have_Any_BrunoVehicleHire_Dependencies()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .ResideInNamespace("BrunoVehicleHire.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "BrunoVehicleHire.Application",
                "BrunoVehicleHire.Infrastructure",
                "BrunoVehicleHire.Api")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Types violating the Domain zero-dependency rule: " +
            $"{string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}");
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespace("BrunoVehicleHire.Application")
            .ShouldNot()
            .HaveDependencyOnAny(
                "BrunoVehicleHire.Infrastructure",
                "BrunoVehicleHire.Api")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Types violating the Application -> Infrastructure/Api rule: " +
            $"{string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}");
    }
}
