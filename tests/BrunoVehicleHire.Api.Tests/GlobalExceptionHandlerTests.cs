using BrunoVehicleHire.Api.ErrorHandling;
using BrunoVehicleHire.Domain.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace BrunoVehicleHire.Api.Tests;

/// <summary>
/// Unit tests for <see cref="GlobalExceptionHandler"/> run in isolation against a bare
/// <see cref="DefaultHttpContext"/> -- no ASP.NET Core host, no TestServer (matching
/// <see cref="ApiKeyAuthenticationHandlerTests"/>'s isolation pattern). <see cref="IProblemDetailsService"/>
/// is faked via NSubstitute so the exact <see cref="ProblemDetails"/> shape the handler builds can
/// be captured and asserted without a full DI container.
/// </summary>
public class GlobalExceptionHandlerTests
{
    private const string ProhibitedInternalDetail = "some internal detail that must never reach the client";

    private static (GlobalExceptionHandler Handler, IProblemDetailsService ProblemDetailsService) CreateHandler()
    {
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(new ValueTask<bool>(true));

        return (new GlobalExceptionHandler(problemDetailsService), problemDetailsService);
    }

    private static ProblemDetailsContext GetWrittenContext(IProblemDetailsService problemDetailsService)
    {
        var call = problemDetailsService.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IProblemDetailsService.TryWriteAsync));
        return (ProblemDetailsContext)call.GetArguments()[0]!;
    }

    [Fact]
    public async Task TryHandleAsync_DomainRuleViolationException_Returns409WithConflictProblemDetails()
    {
        var (handler, problemDetailsService) = CreateHandler();
        var httpContext = new DefaultHttpContext();
        var exception = new DomainRuleViolationException(
            entity: "Vehicle",
            rule: "RegistrationNumber",
            message: "RegistrationNumber must not be blank.");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        var context = GetWrittenContext(problemDetailsService);
        context.HttpContext.Should().BeSameAs(httpContext);
        context.Exception.Should().BeSameAs(exception);
        context.ProblemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
        context.ProblemDetails.Type.Should().Be("urn:bruno:vehicle:registration-number");
        context.ProblemDetails.Detail.Should().Be(exception.Message);
    }

    [Fact]
    public async Task TryHandleAsync_DomainRuleViolationException_TypeUriMatchesProblemTypeUrisFor()
    {
        var (handler, problemDetailsService) = CreateHandler();
        var httpContext = new DefaultHttpContext();
        var exception = new DomainRuleViolationException("Vehicle", "Year", "Year must be between 1900 and next year.");

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        var context = GetWrittenContext(problemDetailsService);
        context.ProblemDetails.Type.Should().Be(ProblemTypeUris.For(exception.Entity, exception.Rule));
    }

    [Fact]
    public async Task TryHandleAsync_GenericException_Returns500WithFixedGenericProblemDetails()
    {
        var (handler, problemDetailsService) = CreateHandler();
        var httpContext = new DefaultHttpContext();
        var exception = new InvalidOperationException(ProhibitedInternalDetail);

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        var context = GetWrittenContext(problemDetailsService);
        context.ProblemDetails.Status.Should().Be(StatusCodes.Status500InternalServerError);
        context.ProblemDetails.Type.Should().Be(ProblemTypeUris.UnexpectedError);
        context.ProblemDetails.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TryHandleAsync_GenericException_NeverLeaksOriginalExceptionMessage()
    {
        var (handler, problemDetailsService) = CreateHandler();
        var httpContext = new DefaultHttpContext();
        var exception = new InvalidOperationException(ProhibitedInternalDetail);

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        var context = GetWrittenContext(problemDetailsService);
        context.ProblemDetails.Detail.Should().NotContain(ProhibitedInternalDetail);
        context.ProblemDetails.Title.Should().NotContain(ProhibitedInternalDetail);
        context.ProblemDetails.Type.Should().NotContain(ProhibitedInternalDetail);
    }

    [Fact]
    public async Task TryHandleAsync_AnyUnknownExceptionType_UsesSameGenericFallback_NotOnlyInvalidOperationException()
    {
        var (handler, problemDetailsService) = CreateHandler();
        var httpContext = new DefaultHttpContext();
        var exception = new NotSupportedException("irrelevant, must never be echoed either");

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        var context = GetWrittenContext(problemDetailsService);
        context.ProblemDetails.Status.Should().Be(StatusCodes.Status500InternalServerError);
        context.ProblemDetails.Type.Should().Be(ProblemTypeUris.UnexpectedError);
        context.ProblemDetails.Detail.Should().NotContain(exception.Message);
    }
}

/// <summary>
/// Unit tests for <see cref="ProblemTypeUris"/> -- the single shared kebab-casing helper that
/// produces every RFC 9457 <c>type</c> URI this API emits, so no handler ever hand-types one.
/// </summary>

/// <summary>
/// Unit tests for <see cref="ProblemTypeUris"/> -- the single shared kebab-casing helper that
/// produces every RFC 9457 <c>type</c> URI this API emits, so no handler ever hand-types one.
/// </summary>
public class ProblemTypeUrisTests
{
    [Fact]
    public void For_VehicleAndRegistrationNumber_ProducesExpectedKebabCaseUri()
    {
        var result = ProblemTypeUris.For("Vehicle", "RegistrationNumber");

        result.Should().Be("urn:bruno:vehicle:registration-number");
    }

    [Fact]
    public void For_VehicleAndYear_ProducesExpectedKebabCaseUri()
    {
        var result = ProblemTypeUris.For("Vehicle", "Year");

        result.Should().Be("urn:bruno:vehicle:year");
    }

    [Fact]
    public void For_SingleWordSegments_LowercasesWithoutHyphens()
    {
        var result = ProblemTypeUris.For("Booking", "Status");

        result.Should().Be("urn:bruno:booking:status");
    }

    [Fact]
    public void For_MultiWordPascalCaseSegments_InsertsHyphenBeforeEachWord()
    {
        var result = ProblemTypeUris.For("CustomerAccount", "PhoneNumberFormat");

        result.Should().Be("urn:bruno:customer-account:phone-number-format");
    }

    [Fact]
    public void UnexpectedError_IsTheFixedServerErrorUri()
    {
        ProblemTypeUris.UnexpectedError.Should().Be("urn:bruno:server:unexpected-error");
    }
}
