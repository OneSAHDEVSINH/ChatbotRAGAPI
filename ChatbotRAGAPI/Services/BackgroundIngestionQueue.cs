using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ChatbotRAGAPI.Contracts;
using ChatbotRAGAPI.Domain;
using ChatbotRAGAPI.Options;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ChatbotRAGAPI.Services;

public sealed class BackgroundIngestionQueue : IIngestionJobQueue
{
    private readonly Channel<BackgroundIngestionJob> _channel;
    private readonly ConcurrentDictionary<string, BackgroundIngestionJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly IIngestionJobStore _ingestionJobStore;
    private readonly SemaphoreSlim _restoreLock = new(1, 1);
    private volatile bool _restored;

    public BackgroundIngestionQueue(IIngestionJobStore ingestionJobStore, IOptions<RagOptions> options)
    {
        _ingestionJobStore = ingestionJobStore;
        var capacity = Math.Max(1, options.Value.BackgroundIngestion.QueueCapacity);
        _channel = Channel.CreateBounded<BackgroundIngestionJob>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public async ValueTask<IngestionJobAcceptedResponse> EnqueueAsync(BackgroundIngestionJob job, CancellationToken cancellationToken)
    {
        job.JobId = string.IsNullOrWhiteSpace(job.JobId) ? $"job_{Guid.NewGuid():N}" : job.JobId;
        job.State = IngestionJobState.Queued;
        job.QueuedAtUtc = DateTimeOffset.UtcNow;
        job.StartedAtUtc = null;
        job.CompletedAtUtc = null;
        job.Error = null;
        job.Results = [];

        _jobs[job.JobId] = job;
        await _ingestionJobStore.UpsertAsync(job, cancellationToken);
        await _channel.Writer.WriteAsync(job, cancellationToken);

        return new IngestionJobAcceptedResponse
        {
            JobId = job.JobId,
            Status = job.State.ToString(),
            QueuedAtUtc = job.QueuedAtUtc
        };
    }

    public async IAsyncEnumerable<BackgroundIngestionJob> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await RestorePendingJobsAsync(cancellationToken);

        await foreach (var job in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return job;
        }
    }

    public async Task MarkProcessingAsync(string jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.State = IngestionJobState.Processing;
            job.StartedAtUtc = DateTimeOffset.UtcNow;
            job.Error = null;
            await _ingestionJobStore.UpsertAsync(job, cancellationToken);
        }
    }

    public async Task MarkCompletedAsync(string jobId, IReadOnlyList<IngestionResponse> results, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.State = IngestionJobState.Completed;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.Results = results;
            job.Content = null;
            job.Url = null;
            job.Files = [];
            await _ingestionJobStore.UpsertAsync(job, cancellationToken);
        }
    }

    public async Task MarkFailedAsync(string jobId, string error, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.State = IngestionJobState.Failed;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.Error = error;
            job.Content = null;
            job.Url = null;
            job.Files = [];
            await _ingestionJobStore.UpsertAsync(job, cancellationToken);
        }
    }

    public async ValueTask<IngestionJobStatusResponse?> GetStatusAsync(string jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            job = await _ingestionJobStore.GetAsync(jobId, cancellationToken);
            if (job is null)
            {
                return null;
            }

            _jobs[jobId] = job;
        }

        return new IngestionJobStatusResponse
        {
            JobId = job.JobId,
            Status = job.State.ToString(),
            SourceType = job.SourceType,
            QueuedAtUtc = job.QueuedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            Error = job.Error,
            Results = job.Results
        };
    }

    private async Task RestorePendingJobsAsync(CancellationToken cancellationToken)
    {
        if (_restored)
        {
            return;
        }

        await _restoreLock.WaitAsync(cancellationToken);
        try
        {
            if (_restored)
            {
                return;
            }

            var pendingJobs = await _ingestionJobStore.GetPendingJobsAsync(cancellationToken);
            foreach (var job in pendingJobs.OrderBy(job => job.QueuedAtUtc))
            {
                _jobs[job.JobId] = job;
                await _channel.Writer.WriteAsync(job, cancellationToken);
            }

            _restored = true;
        }
        finally
        {
            _restoreLock.Release();
        }
    }
}
