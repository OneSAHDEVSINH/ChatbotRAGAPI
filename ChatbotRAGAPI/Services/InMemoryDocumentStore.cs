using System.Collections.Concurrent;
using ChatbotRAGAPI.Domain;
using ChatbotRAGAPI.Services.Interfaces;

namespace ChatbotRAGAPI.Services;

public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<string, StoredDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DocumentChunk> _chunks = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask UpsertAsync(StoredDocument document, IReadOnlyCollection<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _documents[document.DocumentId] = document;

        var existingChunkIds = _chunks.Values
            .Where(chunk => string.Equals(chunk.DocumentId, document.DocumentId, StringComparison.OrdinalIgnoreCase))
            .Select(chunk => chunk.ChunkId)
            .ToArray();

        foreach (var chunkId in existingChunkIds)
        {
            _chunks.TryRemove(chunkId, out _);
        }

        foreach (var chunk in chunks)
        {
            _chunks[chunk.ChunkId] = chunk;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<StoredDocument>> GetDocumentsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<StoredDocument>>(_documents.Values.OrderBy(x => x.SourceName).ToArray());
    }

    public ValueTask<IReadOnlyCollection<DocumentChunk>> GetChunksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<DocumentChunk>>(_chunks.Values.ToArray());
    }
}
