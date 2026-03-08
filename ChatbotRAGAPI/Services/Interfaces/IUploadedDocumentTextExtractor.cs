namespace ChatbotRAGAPI.Services.Interfaces;

public interface IUploadedDocumentTextExtractor
{
    bool CanExtract(string fileName);

    Task<string?> ExtractAsync(IFormFile file, CancellationToken cancellationToken);
}
