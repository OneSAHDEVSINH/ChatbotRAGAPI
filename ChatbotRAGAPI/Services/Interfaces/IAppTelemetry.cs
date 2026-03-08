using ChatbotRAGAPI.Contracts;

namespace ChatbotRAGAPI.Services.Interfaces;

public interface IAppTelemetry
{
    void TrackIngestion(TimeSpan duration);

    void TrackRetrieval(TimeSpan duration);

    void TrackChat(TimeSpan duration);

    void TrackBackgroundJob(TimeSpan duration);

    DiagnosticsSummaryResponse GetSummary();
}
