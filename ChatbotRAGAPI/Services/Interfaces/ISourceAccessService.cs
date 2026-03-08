namespace ChatbotRAGAPI.Services.Interfaces;

public interface ISourceAccessService
{
    IReadOnlySet<string>? GetAllowedSources();

    bool CanAccessSource(string? sourceId, bool requireExplicitSourceForRestrictedWrites);
}
