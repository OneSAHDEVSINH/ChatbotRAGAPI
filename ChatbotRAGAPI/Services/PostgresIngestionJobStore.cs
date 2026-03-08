using System.Text.Json;
using ChatbotRAGAPI.Contracts;
using ChatbotRAGAPI.Domain;
using ChatbotRAGAPI.Options;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ChatbotRAGAPI.Services;

public sealed class PostgresIngestionJobStore : IIngestionJobStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresOptions _options;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _isInitialized;

    public PostgresIngestionJobStore(NpgsqlDataSource dataSource, IOptions<RagOptions> options)
    {
        _dataSource = dataSource;
        _options = options.Value.Postgres;
    }

    public async Task UpsertAsync(BackgroundIngestionJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {JobsTable}
                (job_id, kind, source_type, content, source_name, source_id, source_location, url, files_json, state, queued_at_utc, started_at_utc, completed_at_utc, error, results_json)
            VALUES
                (@job_id, @kind, @source_type, @content, @source_name, @source_id, @source_location, @url, CAST(@files_json AS jsonb), @state, @queued_at_utc, @started_at_utc, @completed_at_utc, @error, CAST(@results_json AS jsonb))
            ON CONFLICT (job_id) DO UPDATE
            SET kind = EXCLUDED.kind,
                source_type = EXCLUDED.source_type,
                content = EXCLUDED.content,
                source_name = EXCLUDED.source_name,
                source_id = EXCLUDED.source_id,
                source_location = EXCLUDED.source_location,
                url = EXCLUDED.url,
                files_json = EXCLUDED.files_json,
                state = EXCLUDED.state,
                queued_at_utc = EXCLUDED.queued_at_utc,
                started_at_utc = EXCLUDED.started_at_utc,
                completed_at_utc = EXCLUDED.completed_at_utc,
                error = EXCLUDED.error,
                results_json = EXCLUDED.results_json
            """, connection);

        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("kind", job.Kind.ToString());
        command.Parameters.AddWithValue("source_type", job.SourceType);
        command.Parameters.AddWithValue("content", (object?)job.Content ?? DBNull.Value);
        command.Parameters.AddWithValue("source_name", (object?)job.SourceName ?? DBNull.Value);
        command.Parameters.AddWithValue("source_id", (object?)job.SourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("source_location", (object?)job.SourceLocation ?? DBNull.Value);
        command.Parameters.AddWithValue("url", (object?)job.Url ?? DBNull.Value);
        command.Parameters.AddWithValue("files_json", JsonSerializer.Serialize(job.Files, SerializerOptions));
        command.Parameters.AddWithValue("state", job.State.ToString());
        command.Parameters.AddWithValue("queued_at_utc", job.QueuedAtUtc);
        command.Parameters.AddWithValue("started_at_utc", (object?)job.StartedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("completed_at_utc", (object?)job.CompletedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)job.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("results_json", JsonSerializer.Serialize(job.Results, SerializerOptions));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<BackgroundIngestionJob?> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT job_id, kind, source_type, content, source_name, source_id, source_location, url, files_json, state, queued_at_utc, started_at_utc, completed_at_utc, error, results_json
            FROM {JobsTable}
            WHERE job_id = @job_id
            """, connection);

        command.Parameters.AddWithValue("job_id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadJob(reader)
            : null;
    }

    public async Task<IReadOnlyCollection<BackgroundIngestionJob>> GetPendingJobsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureCreatedAsync(cancellationToken);

        var jobs = new List<BackgroundIngestionJob>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT job_id, kind, source_type, content, source_name, source_id, source_location, url, files_json, state, queued_at_utc, started_at_utc, completed_at_utc, error, results_json
            FROM {JobsTable}
            WHERE state IN ('Queued', 'Processing')
            ORDER BY queued_at_utc
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadJob(reader));
        }

        return jobs;
    }

    private BackgroundIngestionJob ReadJob(NpgsqlDataReader reader)
    {
        var kind = Enum.Parse<IngestionJobKind>(reader.GetString(1));
        var state = Enum.Parse<IngestionJobState>(reader.GetString(9));
        var files = Deserialize<IReadOnlyList<QueuedFileUpload>>(reader.GetString(8)) ?? [];
        var results = Deserialize<IReadOnlyList<IngestionResponse>>(reader.GetString(14)) ?? [];

        return new BackgroundIngestionJob
        {
            JobId = reader.GetString(0),
            Kind = kind,
            SourceType = reader.GetString(2),
            Content = reader.IsDBNull(3) ? null : reader.GetString(3),
            SourceName = reader.IsDBNull(4) ? null : reader.GetString(4),
            SourceId = reader.IsDBNull(5) ? null : reader.GetString(5),
            SourceLocation = reader.IsDBNull(6) ? null : reader.GetString(6),
            Url = reader.IsDBNull(7) ? null : reader.GetString(7),
            Files = files,
            State = state,
            QueuedAtUtc = reader.GetFieldValue<DateTimeOffset>(10),
            StartedAtUtc = reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            CompletedAtUtc = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            Error = reader.IsDBNull(13) ? null : reader.GetString(13),
            Results = results
        };
    }

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"""
                CREATE SCHEMA IF NOT EXISTS {SchemaName};

                CREATE TABLE IF NOT EXISTS {JobsTable}
                (
                    job_id TEXT PRIMARY KEY,
                    kind TEXT NOT NULL,
                    source_type TEXT NOT NULL,
                    content TEXT NULL,
                    source_name TEXT NULL,
                    source_id TEXT NULL,
                    source_location TEXT NULL,
                    url TEXT NULL,
                    files_json JSONB NOT NULL,
                    state TEXT NOT NULL,
                    queued_at_utc TIMESTAMPTZ NOT NULL,
                    started_at_utc TIMESTAMPTZ NULL,
                    completed_at_utc TIMESTAMPTZ NULL,
                    error TEXT NULL,
                    results_json JSONB NOT NULL
                );

                CREATE INDEX IF NOT EXISTS {QuoteIdentifier($"{_options.JobsTableName}_state_idx")}
                ON {JobsTable}(state);

                CREATE INDEX IF NOT EXISTS {QuoteIdentifier($"{_options.JobsTableName}_queued_at_idx")}
                ON {JobsTable}(queued_at_utc);
                """, connection);

            await command.ExecuteNonQueryAsync(cancellationToken);
            _isInitialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    private string SchemaName => QuoteIdentifier(_options.Schema);

    private string JobsTable => $"{SchemaName}.{QuoteIdentifier(_options.JobsTableName)}";

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
