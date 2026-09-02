using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BrunoVehicleHire.Api.Auth;

/// <summary>
/// AD-11's custom authentication scheme: reads the <c>X-Api-Key</c> header and compares it,
/// in constant time, against the configured key. Uses the built-in
/// <see cref="AuthenticationSchemeOptions"/> (no scheme-specific configuration is needed beyond
/// the shared <see cref="ApiKeyOptions"/> read via DI -- see the spec's Design Notes).
///
/// Never logs or includes in any failure message the raw submitted header value or the raw
/// configured key -- only generic, non-identifying failure reasons.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ApiKeyOptions _apiKeyOptions;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ApiKeyOptions> apiKeyOptions)
        : base(options, logger, encoder)
    {
        _apiKeyOptions = apiKeyOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key header is missing."));
        }

        var providedKey = headerValues.ToString();

        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key header is missing."));
        }

        var configuredKey = _apiKeyOptions.Key ?? string.Empty;

        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);

        // Constant-time comparison avoids a timing side-channel on key comparison. Different
        // lengths short-circuit to false without a byte-by-byte comparison, which is fine: only
        // the *content* of the key is secret, not its length.
        var isMatch = CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);

        if (!isMatch)
        {
            Logger.LogWarning("API key authentication failed: submitted key did not match the configured key.");
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, ApiKeyDefaults.Scheme) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
