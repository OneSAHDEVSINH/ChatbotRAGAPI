namespace ChatbotRAGAPI.Services.Interfaces;

public interface IOcrTextExtractor
{
    bool IsConfigured { get; }

    Task<string?> ExtractAsync(IFormFile file, CancellationToken cancellationToken);
}
