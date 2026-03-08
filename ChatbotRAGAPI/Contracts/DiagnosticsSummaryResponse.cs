namespace ChatbotRAGAPI.Contracts;

public sealed class DiagnosticsSummaryResponse
{
    public long TotalIngestions { get; set; }

    public long TotalRetrievedQueries { get; set; }

    public long TotalChatRequests { get; set; }

    public long TotalBackgroundJobsProcessed { get; set; }

    public double AverageIngestionDurationMs { get; set; }

    public double AverageRetrievalDurationMs { get; set; }

    public double AverageChatDurationMs { get; set; }

    public double AverageBackgroundJobDurationMs { get; set; }
}
