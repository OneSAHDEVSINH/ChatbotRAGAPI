namespace ChatbotRAGAPI.Contracts;

public sealed class IngestionJobAcceptedResponse
{
    public string JobId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset QueuedAtUtc { get; set; }
}
