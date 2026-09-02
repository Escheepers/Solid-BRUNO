using System.Text;

namespace BrunoVehicleHire.Api.ErrorHandling;

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
