using ChatbotRAGAPI.Contracts;

namespace ChatbotRAGAPI.Services.Interfaces;

public interface IIngestionService
{
    Task<IngestionResponse> IngestTextAsync(string content, string sourceName, string sourceType, string? sourceId, string? sourceLocation, CancellationToken cancellationToken);
}
