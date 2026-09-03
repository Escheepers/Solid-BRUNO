using BrunoVehicleHire.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BrunoVehicleHire.Api.ExceptionHandling;

/// <summary>
/// AD-8's global exception handler: maps <see cref="DomainRuleViolationException"/> to a
/// <c>409 Conflict</c> RFC 9457 ProblemDetails response (its <c>type</c> URI built via the shared
/// <see cref="ProblemTypeUris"/> helper), and every other unhandled exception to a fixed, generic
/// <c>500 Internal Server Error</c> ProblemDetails response -- the original exception's message and
/// stack trace are never echoed to the client, only ever passed to <see cref="IProblemDetailsService"/>
/// for server-side diagnostics via the <see cref="ProblemDetailsContext.Exception"/> property.
/// </summary>
public class GlobalExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    /// <summary>
    /// Fixed, PII/stack-trace-free detail string for the 500 fallback branch. Deliberately never
    /// derived from <c>exception.Message</c> -- see Story 1.5's spec Design Notes on information
    /// disclosure.
    /// </summary>
    private const string GenericErrorDetail =
        "An unexpected error occurred while processing your request. Please try again later.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            DomainRuleViolationException domainRuleViolation => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A domain rule was violated.",
                Type = ProblemTypeUris.For(domainRuleViolation.Entity, domainRuleViolation.Rule),
                Detail = domainRuleViolation.Message,
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = ProblemTypeUris.UnexpectedError,
                Detail = GenericErrorDetail,
            },
        };

        httpContext.Response.StatusCode = problemDetails.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails,
        });
    }
}
