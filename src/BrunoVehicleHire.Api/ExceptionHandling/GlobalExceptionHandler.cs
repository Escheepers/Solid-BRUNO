using BrunoVehicleHire.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ValidationException = FluentValidation.ValidationException;

namespace BrunoVehicleHire.Api.ExceptionHandling;

/// <summary>
/// AD-8's global exception handler: maps <see cref="DomainRuleViolationException"/> to a
/// <c>409 Conflict</c> RFC 9457 ProblemDetails response (its <c>type</c> URI built via the shared
/// <see cref="ProblemTypeUris"/> helper), a <see cref="ValidationException"/> thrown by the
/// <c>ValidationBehavior&lt;TRequest,TResponse&gt;</c> MediatR pipeline stage (Application layer) to
/// a <c>400 Bad Request</c> <see cref="ValidationProblemDetails"/> response (field-level messages
/// grouped into its <c>Errors</c> dictionary), and every other unhandled exception to a fixed,
/// generic <c>500 Internal Server Error</c> ProblemDetails response -- the original exception's
/// message and stack trace are never echoed to the client, only ever passed to
/// <see cref="IProblemDetailsService"/> for server-side diagnostics via the
/// <see cref="ProblemDetailsContext.Exception"/> property.
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
            ValidationException validationException => new ValidationProblemDetails(
                validationException.Errors
                    .GroupBy(failure => failure.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(failure => failure.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Type = ProblemTypeUris.ValidationFailure,
                // Every ProblemDetails-shaped response this API produces populates Detail (the
                // frontend's error-normalization interceptor requires it to recognize a
                // business-rule response) -- for validation failures the field-level detail lives
                // in Errors, so this is a fixed summary line, not per-field prose.
                Detail = "One or more fields failed validation. See the errors property for details.",
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
