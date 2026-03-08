using System.Collections.Concurrent;
using ChatbotRAGAPI.Domain;
using ChatbotRAGAPI.Services.Interfaces;

namespace ChatbotRAGAPI.Services;

public sealed class InMemoryIngestionJobStore : IIngestionJobStore
{
    private readonly ConcurrentDictionary<string, BackgroundIngestionJob> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public Task UpsertAsync(BackgroundIngestionJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _jobs[job.JobId] = job;
        return Task.CompletedTask;
    }

    public ValueTask<BackgroundIngestionJob?> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _jobs.TryGetValue(jobId, out var job);
        return ValueTask.FromResult(job);
    }

    public Task<IReadOnlyCollection<BackgroundIngestionJob>> GetPendingJobsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<BackgroundIngestionJob>>(_jobs.Values
            .Where(job => job.State is IngestionJobState.Queued or IngestionJobState.Processing)
            .OrderBy(job => job.QueuedAtUtc)
            .ToArray());
    }
}
