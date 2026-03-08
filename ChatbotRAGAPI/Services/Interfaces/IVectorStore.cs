using ChatbotRAGAPI.Domain;

namespace ChatbotRAGAPI.Services.Interfaces;

public interface IVectorStore
{
    bool IsConfigured { get; }

    Task UpsertAsync(IReadOnlyList<(DocumentChunk Chunk, float[] Embedding)> entries, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(float[] embedding, string? sourceId, int limit, CancellationToken cancellationToken);
}
