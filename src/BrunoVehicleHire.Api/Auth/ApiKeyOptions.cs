namespace BrunoVehicleHire.Api.Auth;

/// <summary>
/// Options bound from the <c>"ApiKey"</c> configuration section. Deliberately a plain options
/// class (not the scheme's <c>AuthenticationSchemeOptions</c>) so the configured key is sourced
/// via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> from configuration --
/// appsettings/user-secrets/environment variable -- never a literal in code.
/// </summary>
public class ApiKeyOptions
{
    public string Key { get; set; } = string.Empty;
}
