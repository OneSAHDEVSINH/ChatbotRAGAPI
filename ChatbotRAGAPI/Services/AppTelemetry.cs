using ChatbotRAGAPI.Contracts;
using ChatbotRAGAPI.Services.Interfaces;

namespace ChatbotRAGAPI.Services;

public sealed class AppTelemetry : IAppTelemetry
{
    private long _totalIngestions;
    private long _totalRetrievedQueries;
    private long _totalChatRequests;
    private long _totalBackgroundJobsProcessed;
    private long _totalIngestionDurationTicks;
    private long _totalRetrievalDurationTicks;
    private long _totalChatDurationTicks;
    private long _totalBackgroundJobDurationTicks;

    public void TrackIngestion(TimeSpan duration)
    {
        Interlocked.Increment(ref _totalIngestions);
        Interlocked.Add(ref _totalIngestionDurationTicks, duration.Ticks);
    }

    public void TrackRetrieval(TimeSpan duration)
    {
        Interlocked.Increment(ref _totalRetrievedQueries);
        Interlocked.Add(ref _totalRetrievalDurationTicks, duration.Ticks);
    }

    public void TrackChat(TimeSpan duration)
    {
        Interlocked.Increment(ref _totalChatRequests);
        Interlocked.Add(ref _totalChatDurationTicks, duration.Ticks);
    }

    public void TrackBackgroundJob(TimeSpan duration)
    {
        Interlocked.Increment(ref _totalBackgroundJobsProcessed);
        Interlocked.Add(ref _totalBackgroundJobDurationTicks, duration.Ticks);
    }

    public DiagnosticsSummaryResponse GetSummary()
    {
        var totalIngestions = Interlocked.Read(ref _totalIngestions);
        var totalRetrievedQueries = Interlocked.Read(ref _totalRetrievedQueries);
        var totalChatRequests = Interlocked.Read(ref _totalChatRequests);
        var totalBackgroundJobsProcessed = Interlocked.Read(ref _totalBackgroundJobsProcessed);

        return new DiagnosticsSummaryResponse
        {
            TotalIngestions = totalIngestions,
            TotalRetrievedQueries = totalRetrievedQueries,
            TotalChatRequests = totalChatRequests,
            TotalBackgroundJobsProcessed = totalBackgroundJobsProcessed,
            AverageIngestionDurationMs = ToAverageMilliseconds(Interlocked.Read(ref _totalIngestionDurationTicks), totalIngestions),
            AverageRetrievalDurationMs = ToAverageMilliseconds(Interlocked.Read(ref _totalRetrievalDurationTicks), totalRetrievedQueries),
            AverageChatDurationMs = ToAverageMilliseconds(Interlocked.Read(ref _totalChatDurationTicks), totalChatRequests),
            AverageBackgroundJobDurationMs = ToAverageMilliseconds(Interlocked.Read(ref _totalBackgroundJobDurationTicks), totalBackgroundJobsProcessed)
        };
    }

    private static double ToAverageMilliseconds(long totalTicks, long count)
    {
        return count == 0 ? 0 : Math.Round(TimeSpan.FromTicks(totalTicks / count).TotalMilliseconds, 2);
    }
}
