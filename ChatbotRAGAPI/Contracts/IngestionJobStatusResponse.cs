namespace ChatbotRAGAPI.Contracts;

public sealed class IngestionJobStatusResponse
{
    public string JobId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public DateTimeOffset QueuedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? Error { get; set; }

    public IReadOnlyList<IngestionResponse> Results { get; set; } = [];
}
