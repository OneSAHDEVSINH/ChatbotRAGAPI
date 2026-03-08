namespace ChatbotRAGAPI.Contracts;

public sealed class IngestionResponse
{
    public string DocumentId { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public int ChunkCount { get; set; }

    public DateTimeOffset IngestedAtUtc { get; set; }
}
