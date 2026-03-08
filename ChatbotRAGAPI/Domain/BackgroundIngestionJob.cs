using ChatbotRAGAPI.Contracts;

namespace ChatbotRAGAPI.Domain;

public enum IngestionJobState
{
    Queued,
    Processing,
    Completed,
    Failed
}

public enum IngestionJobKind
{
    Text,
    WebPage,
    Files
}

public sealed class QueuedFileUpload
{
    public string FileName { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public byte[] Content { get; set; } = [];
}

public sealed class BackgroundIngestionJob
{
    public string JobId { get; set; } = string.Empty;

    public IngestionJobKind Kind { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public string? Content { get; set; }

    public string? SourceName { get; set; }

    public string? SourceId { get; set; }

    public string? SourceLocation { get; set; }

    public string? Url { get; set; }

    public IReadOnlyList<QueuedFileUpload> Files { get; set; } = [];

    public IngestionJobState State { get; set; } = IngestionJobState.Queued;

    public DateTimeOffset QueuedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? Error { get; set; }

    public IReadOnlyList<IngestionResponse> Results { get; set; } = [];
}
