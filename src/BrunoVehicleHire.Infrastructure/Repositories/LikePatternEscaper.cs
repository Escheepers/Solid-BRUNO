namespace BrunoVehicleHire.Infrastructure.Repositories;

/// <summary>
/// Escapes a raw user-supplied search term so it can be safely wrapped in a Postgres
/// <c>ILIKE</c>/<c>LIKE</c> wildcard pattern (e.g. <c>$"%{escaped}%"</c>) without the term's own
/// literal <c>%</c>/<c>_</c>/<c>\</c> characters being misinterpreted as wildcards. Prefixing each of
/// the three special characters with a backslash -- backslash itself first, so escaping doesn't
/// double-escape the escape character -- makes them match themselves literally, PROVIDED the query
/// also passes <see cref="EscapeCharacter"/> to <see cref="Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions"/>'
/// three-argument <c>ILike(matchExpression, pattern, escapeCharacter)</c> overload. The two-argument
/// overload is NOT sufficient: EF Core's Npgsql provider translates it to
/// <c>ILIKE @pattern ESCAPE ''</c> -- an explicit empty-string escape clause that, per Postgres's own
/// semantics, disables the escape mechanism entirely (there is then no way to make <c>%</c>/<c>_</c>
/// literal at all), rather than falling back to Postgres's own default backslash escape as the
/// two-argument overload's name might suggest. Confirmed empirically via the query logged by
/// <c>VehiclesEndpointTests</c>/<c>CustomersEndpointTests</c>/<c>BookingsEndpointTests</c>'s own
/// literal-wildcard regression tests. Shared by
/// <c>VehicleRepository</c>/<c>CustomerRepository</c>/<c>BookingRepository</c>'s <c>GetPagedAsync</c>
/// (the "second/third real consumer" DRY threshold, mirroring e.g. <c>EmailHasher</c>) rather than
/// three independently-written copies of this subtle escape-ordering logic.
/// </summary>
public static class LikePatternEscaper
{
    /// <summary>
    /// Must be passed as the third argument to every <c>EF.Functions.ILike</c> call whose pattern was
    /// built from <see cref="Escape"/>'s output -- see this class's own doc comment for why the
    /// two-argument overload silently disables escaping instead of defaulting to it.
    /// </summary>
    public const string EscapeCharacter = "\\";

    public static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }
}
