using ChatbotRAGAPI.Contracts;
using ChatbotRAGAPI.Domain;

namespace ChatbotRAGAPI.Services.Interfaces;

public interface IIngestionJobQueue
{
    ValueTask<IngestionJobAcceptedResponse> EnqueueAsync(BackgroundIngestionJob job, CancellationToken cancellationToken);

    IAsyncEnumerable<BackgroundIngestionJob> ReadAllAsync(CancellationToken cancellationToken);

    Task MarkProcessingAsync(string jobId, CancellationToken cancellationToken);

    Task MarkCompletedAsync(string jobId, IReadOnlyList<IngestionResponse> results, CancellationToken cancellationToken);

    Task MarkFailedAsync(string jobId, string error, CancellationToken cancellationToken);

    ValueTask<IngestionJobStatusResponse?> GetStatusAsync(string jobId, CancellationToken cancellationToken);
}
