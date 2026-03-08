using ChatbotRAGAPI.Domain;

namespace ChatbotRAGAPI.Services.Interfaces;

public interface IDocumentStore
{
    ValueTask UpsertAsync(StoredDocument document, IReadOnlyCollection<DocumentChunk> chunks, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<StoredDocument>> GetDocumentsAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<DocumentChunk>> GetChunksAsync(CancellationToken cancellationToken);
}
