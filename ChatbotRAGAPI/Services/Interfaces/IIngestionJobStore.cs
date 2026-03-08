using ChatbotRAGAPI.Domain;

namespace ChatbotRAGAPI.Services.Interfaces;

public interface IIngestionJobStore
{
    Task UpsertAsync(BackgroundIngestionJob job, CancellationToken cancellationToken);

    ValueTask<BackgroundIngestionJob?> GetAsync(string jobId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<BackgroundIngestionJob>> GetPendingJobsAsync(CancellationToken cancellationToken);
}
