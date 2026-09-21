using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace BrunoVehicleHire.Application.Common;

/// <summary>
/// AD-10's shared MediatR pipeline stage: runs every registered <see cref="IValidator{T}"/> for
/// <typeparamref name="TRequest"/>, aggregates every <see cref="ValidationFailure"/> across all of
/// them, and -- if any exist -- throws <see cref="ValidationException"/> BEFORE calling
/// <c>next</c>, short-circuiting the handler entirely. <c>GlobalExceptionHandler</c>
/// (Api layer) catches that exception and maps it to a <c>400 Bad Request</c>
/// <c>ValidationProblemDetails</c> response. No handler ever validates its own request manually.
/// </summary>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);

            var validationResults = await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = validationResults
                .SelectMany(result => result.Errors)
                .Where(failure => failure is not null)
                .ToList();

            if (failures.Count > 0)
            {
                throw new ValidationException(failures);
            }
        }

        return await next(cancellationToken);
    }
}
