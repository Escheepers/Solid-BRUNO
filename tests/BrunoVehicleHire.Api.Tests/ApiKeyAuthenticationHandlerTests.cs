using System.Text.Encodings.Web;
using BrunoVehicleHire.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BrunoVehicleHire.Api.Tests;

/// <summary>
/// Unit tests for <see cref="ApiKeyAuthenticationHandler"/> run in isolation -- no ASP.NET Core
/// host, no TestServer. The handler is instantiated directly and driven via
/// <see cref="AuthenticationHandler{TOptions}.InitializeAsync"/> against a bare
/// <see cref="DefaultHttpContext"/>, matching the Code Map's "no ASP.NET Core host" requirement.
/// </summary>
public class ApiKeyAuthenticationHandlerTests
{
    private const string ConfiguredKey = "correct-key-value";

    private static (ApiKeyAuthenticationHandler Handler, ILogger Logger) CreateHandler(string? configuredKey = ConfiguredKey)
    {
        var schemeOptionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOptionsMonitor.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        schemeOptionsMonitor.CurrentValue.Returns(new AuthenticationSchemeOptions());

        var logger = Substitute.For<ILogger>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);

        var apiKeyOptions = Options.Create(new ApiKeyOptions { Key = configuredKey ?? string.Empty });

        var handler = new ApiKeyAuthenticationHandler(
            schemeOptionsMonitor,
            loggerFactory,
            UrlEncoder.Default,
            apiKeyOptions);

        return (handler, logger);
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(ApiKeyAuthenticationHandler handler, string? headerValue, bool includeHeader = true)
    {
        var scheme = new AuthenticationScheme(ApiKeyDefaults.Scheme, ApiKeyDefaults.Scheme, typeof(ApiKeyAuthenticationHandler));
        var httpContext = new DefaultHttpContext();

        if (includeHeader)
        {
            httpContext.Request.Headers[ApiKeyDefaults.HeaderName] = headerValue;
        }

        await handler.InitializeAsync(scheme, httpContext);
        return await handler.AuthenticateAsync();
    }

    /// <summary>
    /// Pulls the fully-formatted message text out of every call NSubstitute recorded against the
    /// fake <see cref="ILogger"/>, by invoking the captured formatter delegate on the captured
    /// state -- the same way the real logging infrastructure would render the message.
    /// </summary>
    private static IReadOnlyList<string> GetLoggedMessages(ILogger logger)
    {
        var messages = new List<string>();

        foreach (var call in logger.ReceivedCalls())
        {
            if (call.GetMethodInfo().Name != nameof(ILogger.Log))
            {
                continue;
            }

            var arguments = call.GetArguments();
            var state = arguments[2];
            var exception = arguments[3] as Exception;
            var formatter = arguments[4] as Delegate;

            if (formatter is not null)
            {
                var message = (string?)formatter.DynamicInvoke(state, exception);
                if (message is not null)
                {
                    messages.Add(message);
                }
            }
        }

        return messages;
    }

    [Fact]
    public async Task AuthenticateAsync_MissingHeader_Fails()
    {
        var (handler, _) = CreateHandler();

        var result = await AuthenticateAsync(handler, headerValue: null, includeHeader: false);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_BlankHeader_Fails()
    {
        var (handler, _) = CreateHandler();

        var result = await AuthenticateAsync(handler, headerValue: "   ");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WrongKey_Fails()
    {
        var (handler, _) = CreateHandler();

        var result = await AuthenticateAsync(handler, headerValue: "totally-wrong-key");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_CorrectKey_SucceedsWithClaimsPrincipal()
    {
        var (handler, _) = CreateHandler();

        var result = await AuthenticateAsync(handler, headerValue: ConfiguredKey);

        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.Identity.Should().NotBeNull();
        result.Principal.Identity!.IsAuthenticated.Should().BeTrue();
        result.Principal.Identity.AuthenticationType.Should().Be(ApiKeyDefaults.Scheme);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongKey_NeverLogsRawConfiguredOrSubmittedKey()
    {
        var (handler, logger) = CreateHandler();
        const string submittedKey = "totally-wrong-key";

        var result = await AuthenticateAsync(handler, headerValue: submittedKey);

        result.Succeeded.Should().BeFalse();

        var loggedMessages = GetLoggedMessages(logger);
        var failureMessage = result.Failure?.Message ?? string.Empty;

        loggedMessages.Should().NotContain(m => m.Contains(submittedKey, StringComparison.Ordinal));
        loggedMessages.Should().NotContain(m => m.Contains(ConfiguredKey, StringComparison.Ordinal));
        failureMessage.Should().NotContain(submittedKey);
        failureMessage.Should().NotContain(ConfiguredKey);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingHeader_NeverLogsRawConfiguredKey()
    {
        var (handler, logger) = CreateHandler();

        var result = await AuthenticateAsync(handler, headerValue: null, includeHeader: false);

        result.Succeeded.Should().BeFalse();

        var loggedMessages = GetLoggedMessages(logger);
        var failureMessage = result.Failure?.Message ?? string.Empty;

        loggedMessages.Should().NotContain(m => m.Contains(ConfiguredKey, StringComparison.Ordinal));
        failureMessage.Should().NotContain(ConfiguredKey);
    }
}
