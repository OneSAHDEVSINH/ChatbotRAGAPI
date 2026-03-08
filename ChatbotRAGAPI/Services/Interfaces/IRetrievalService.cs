using ChatbotRAGAPI.Domain;

namespace ChatbotRAGAPI.Services.Interfaces;

public interface IRetrievalService
{
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string question, string? sourceId, CancellationToken cancellationToken);
}
