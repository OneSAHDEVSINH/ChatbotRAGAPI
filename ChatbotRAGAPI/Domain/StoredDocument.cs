namespace ChatbotRAGAPI.Domain;

public sealed class StoredDocument
{
    public string DocumentId { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public string? SourceLocation { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset IngestedAtUtc { get; set; }
}

public sealed class DocumentChunk
{
    public string ChunkId { get; set; } = string.Empty;

    public string DocumentId { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public string? SourceLocation { get; set; }

    public string Content { get; set; } = string.Empty;
}

public sealed class RetrievedChunk
{
    public required DocumentChunk Chunk { get; init; }

    public required double Score { get; init; }
}
