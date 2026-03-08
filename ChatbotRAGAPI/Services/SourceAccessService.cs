using ChatbotRAGAPI.Options;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ChatbotRAGAPI.Services;

public sealed class SourceAccessService : ISourceAccessService
{
    public const string SourceClaimType = "source_access";
    public const string WildcardSource = "*";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly RagOptions _options;

    public SourceAccessService(IHttpContextAccessor httpContextAccessor, IOptions<RagOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    public IReadOnlySet<string>? GetAllowedSources()
    {
        if (!_options.Security.Enabled)
        {
            return null;
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var allowedSources = principal.FindAll(SourceClaimType)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allowedSources.Contains(WildcardSource)
            ? null
            : allowedSources;
    }

    public bool CanAccessSource(string? sourceId, bool requireExplicitSourceForRestrictedWrites)
    {
        var allowedSources = GetAllowedSources();
        if (allowedSources is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return !requireExplicitSourceForRestrictedWrites;
        }

        return allowedSources.Contains(sourceId);
    }
}
