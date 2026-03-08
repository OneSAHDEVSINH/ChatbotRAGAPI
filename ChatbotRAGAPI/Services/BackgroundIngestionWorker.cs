using System.Diagnostics;
using ChatbotRAGAPI.Domain;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChatbotRAGAPI.Services;

public sealed class BackgroundIngestionWorker : BackgroundService
{
    private readonly IAppTelemetry _appTelemetry;
    private readonly IIngestionJobQueue _ingestionJobQueue;
    private readonly ILogger<BackgroundIngestionWorker> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public BackgroundIngestionWorker(IAppTelemetry appTelemetry, IIngestionJobQueue ingestionJobQueue, ILogger<BackgroundIngestionWorker> logger, IServiceScopeFactory serviceScopeFactory)
    {
        _appTelemetry = appTelemetry;
        _ingestionJobQueue = ingestionJobQueue;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _ingestionJobQueue.ReadAllAsync(stoppingToken))
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Processing ingestion job {JobId} of type {JobKind}.", job.JobId, job.Kind);
            await _ingestionJobQueue.MarkProcessingAsync(job.JobId, stoppingToken);

            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var results = await ProcessJobAsync(scope.ServiceProvider, job, stoppingToken);
                await _ingestionJobQueue.MarkCompletedAsync(job.JobId, results, stoppingToken);
                _appTelemetry.TrackBackgroundJob(stopwatch.Elapsed);
                _logger.LogInformation("Completed ingestion job {JobId} with {ResultCount} result(s).", job.JobId, results.Count);
            }
            catch (Exception exception)
            {
                await _ingestionJobQueue.MarkFailedAsync(job.JobId, exception.Message, stoppingToken);
                _appTelemetry.TrackBackgroundJob(stopwatch.Elapsed);
                _logger.LogError(exception, "Ingestion job {JobId} failed.", job.JobId);
            }
        }
    }

    private static async Task<IReadOnlyList<Contracts.IngestionResponse>> ProcessJobAsync(IServiceProvider serviceProvider, BackgroundIngestionJob job, CancellationToken cancellationToken)
    {
        var ingestionService = serviceProvider.GetRequiredService<IIngestionService>();

        return job.Kind switch
        {
            IngestionJobKind.Text => [await ingestionService.IngestTextAsync(job.Content ?? string.Empty, job.SourceName ?? "Unnamed", job.SourceType, job.SourceId, job.SourceLocation, cancellationToken)],
            IngestionJobKind.WebPage => await ProcessWebPageJobAsync(serviceProvider, ingestionService, job, cancellationToken),
            IngestionJobKind.Files => await ProcessFilesJobAsync(serviceProvider, ingestionService, job, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported ingestion job kind: {job.Kind}")
        };
    }

    private static async Task<IReadOnlyList<Contracts.IngestionResponse>> ProcessWebPageJobAsync(IServiceProvider serviceProvider, IIngestionService ingestionService, BackgroundIngestionJob job, CancellationToken cancellationToken)
    {
        var webPageContentService = serviceProvider.GetRequiredService<IWebPageContentService>();
        var (title, content) = await webPageContentService.FetchAsync(job.Url ?? string.Empty, cancellationToken);

        return [await ingestionService.IngestTextAsync(content, job.SourceName ?? title, "webpage", job.SourceId, job.SourceLocation ?? job.Url, cancellationToken)];
    }

    private static async Task<IReadOnlyList<Contracts.IngestionResponse>> ProcessFilesJobAsync(IServiceProvider serviceProvider, IIngestionService ingestionService, BackgroundIngestionJob job, CancellationToken cancellationToken)
    {
        var uploadedDocumentTextExtractor = serviceProvider.GetRequiredService<IUploadedDocumentTextExtractor>();
        var results = new List<Contracts.IngestionResponse>(job.Files.Count);

        foreach (var file in job.Files)
        {
            if (!uploadedDocumentTextExtractor.CanExtract(file.FileName))
            {
                throw new InvalidOperationException($"Unsupported file type: {file.FileName}");
            }

            await using var stream = new MemoryStream(file.Content, writable: false);
            IFormFile formFile = new FormFile(stream, 0, file.Content.Length, file.FileName, file.FileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = file.ContentType
            };

            var content = await uploadedDocumentTextExtractor.ExtractAsync(formFile, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException($"No readable content could be extracted from: {file.FileName}");
            }

            var result = await ingestionService.IngestTextAsync(content, file.FileName, "file", job.SourceId, file.FileName, cancellationToken);
            results.Add(result);
        }

        return results;
    }
}
