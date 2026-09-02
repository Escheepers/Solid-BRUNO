namespace BrunoVehicleHire.Api.Auth;

/// <summary>
/// Constants for the custom API-key authentication scheme (AD-11): the scheme name registered
/// with <c>AddAuthentication</c>/<c>AddScheme</c>, and the HTTP request header the key is read
/// from.
/// </summary>
public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";

    public const string HeaderName = "X-Api-Key";
}
