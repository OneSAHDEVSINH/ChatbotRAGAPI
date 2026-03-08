using System.Security.Claims;
using System.Text.Encodings.Web;
using ChatbotRAGAPI.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ChatbotRAGAPI.Services;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";

    private readonly RagOptions _options;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<RagOptions> ragOptions)
        : base(options, logger, encoder)
    {
        _options = ragOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_options.Security.Enabled)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue(_options.Security.HeaderName, out var headerValues) || string.IsNullOrWhiteSpace(headerValues.FirstOrDefault()))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing API key."));
        }

        var presentedKey = headerValues.First()!;
        var matchedKey = _options.Security.ApiKeys.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.Key) &&
            string.Equals(candidate.Key, presentedKey, StringComparison.Ordinal));

        if (matchedKey is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, matchedKey.Name),
            new(ClaimTypes.Name, matchedKey.Name)
        };

        var allowedSources = matchedKey.AllowedSources.Length == 0
            ? [SourceAccessService.WildcardSource]
            : matchedKey.AllowedSources;

        claims.AddRange(allowedSources.Select(source => new Claim(SourceAccessService.SourceClaimType, source)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
