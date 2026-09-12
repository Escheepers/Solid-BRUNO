using System.Text;

namespace BrunoVehicleHire.Api.ExceptionHandling;

/// <summary>
/// Single shared source of RFC 9457 <c>type</c> URIs for every ProblemDetails response this API
/// produces (AD-8) -- no handler or controller ever hand-types a <c>urn:bruno:...</c> literal.
/// </summary>
public static class ProblemTypeUris
{
    /// <summary>
    /// Fixed <c>type</c> URI for the generic 500 fallback branch of <see cref="GlobalExceptionHandler"/>.
    /// </summary>
    public const string UnexpectedError = "urn:bruno:server:unexpected-error";

    /// <summary>
    /// Fixed <c>type</c> URI for every <c>400 Bad Request</c> validation-failure response
    /// (<see cref="GlobalExceptionHandler"/>'s <c>FluentValidation.ValidationException</c> branch).
    /// Unlike the per-entity/per-rule <see cref="For"/> template used for <c>409</c>s, every
    /// validation failure uses this single fixed URI: the field-level detail already lives in
    /// <c>ValidationProblemDetails.Errors</c>, so a per-field URI would be redundant.
    /// </summary>
    public const string ValidationFailure = "urn:bruno:validation:invalid-request";

    /// <summary>
    /// Fixed <c>type</c> URI for every <c>404 Not Found</c> response
    /// (<see cref="GlobalExceptionHandler"/>'s <c>NotFoundException</c> branch). One fixed URI,
    /// mirroring <see cref="ValidationFailure"/>'s reasoning -- the specific entity/id lives in
    /// <c>Detail</c>, so a per-entity URI would be redundant.
    /// </summary>
    public const string NotFound = "urn:bruno:server:not-found";

    /// <summary>
    /// Builds a <c>urn:bruno:{entity-kebab-case}:{rule-kebab-case}</c> type URI, e.g.
    /// <c>For("Vehicle", "RegistrationNumber")</c> -&gt; <c>"urn:bruno:vehicle:registration-number"</c>.
    /// </summary>
    public static string For(string entity, string rule) =>
        $"urn:bruno:{ToKebabCase(entity)}:{ToKebabCase(rule)}";

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 4);

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];

            if (char.IsUpper(current) && i > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
