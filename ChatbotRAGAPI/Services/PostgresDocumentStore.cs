using ChatbotRAGAPI.Domain;
using ChatbotRAGAPI.Options;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ChatbotRAGAPI.Services;

public sealed class PostgresDocumentStore : IDocumentStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresOptions _options;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _isInitialized;

    public PostgresDocumentStore(NpgsqlDataSource dataSource, IOptions<RagOptions> options)
    {
        _dataSource = dataSource;
        _options = options.Value.Postgres;
    }

    public async ValueTask UpsertAsync(StoredDocument document, IReadOnlyCollection<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureCreatedAsync(cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await UpsertDocumentAsync(connection, transaction, document, cancellationToken);
        await DeleteChunksAsync(connection, transaction, document.DocumentId, cancellationToken);

        foreach (var chunk in chunks)
        {
            await InsertChunkAsync(connection, transaction, chunk, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<StoredDocument>> GetDocumentsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureCreatedAsync(cancellationToken);

        var documents = new List<StoredDocument>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT document_id, source_id, source_name, source_type, source_location, content, ingested_at_utc
            FROM {DocumentsTable}
            ORDER BY source_name, ingested_at_utc DESC
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new StoredDocument
            {
                DocumentId = reader.GetString(0),
                SourceId = reader.GetString(1),
                SourceName = reader.GetString(2),
                SourceType = reader.GetString(3),
                SourceLocation = reader.IsDBNull(4) ? null : reader.GetString(4),
                Content = reader.GetString(5),
                IngestedAtUtc = reader.GetFieldValue<DateTimeOffset>(6)
            });
        }

        return documents;
    }

    public async ValueTask<IReadOnlyCollection<DocumentChunk>> GetChunksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureCreatedAsync(cancellationToken);

        var chunks = new List<DocumentChunk>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT chunk_id, document_id, source_id, source_name, source_location, content
            FROM {ChunksTable}
            ORDER BY source_name, chunk_id
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(new DocumentChunk
            {
                ChunkId = reader.GetString(0),
                DocumentId = reader.GetString(1),
                SourceId = reader.GetString(2),
                SourceName = reader.GetString(3),
                SourceLocation = reader.IsDBNull(4) ? null : reader.GetString(4),
                Content = reader.GetString(5)
            });
        }

        return chunks;
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

                CREATE TABLE IF NOT EXISTS {DocumentsTable}
                (
                    document_id TEXT PRIMARY KEY,
                    source_id TEXT NOT NULL,
                    source_name TEXT NOT NULL,
                    source_type TEXT NOT NULL,
                    source_location TEXT NULL,
                    content TEXT NOT NULL,
                    ingested_at_utc TIMESTAMPTZ NOT NULL
                );

                CREATE TABLE IF NOT EXISTS {ChunksTable}
                (
                    chunk_id TEXT PRIMARY KEY,
                    document_id TEXT NOT NULL REFERENCES {DocumentsTable}(document_id) ON DELETE CASCADE,
                    source_id TEXT NOT NULL,
                    source_name TEXT NOT NULL,
                    source_location TEXT NULL,
                    content TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS {QuoteIdentifier($"{_options.DocumentsTableName}_source_id_idx")}
                ON {DocumentsTable}(source_id);

                CREATE INDEX IF NOT EXISTS {QuoteIdentifier($"{_options.ChunksTableName}_source_id_idx")}
                ON {ChunksTable}(source_id);

                CREATE INDEX IF NOT EXISTS {QuoteIdentifier($"{_options.ChunksTableName}_document_id_idx")}
                ON {ChunksTable}(document_id);
                """, connection);

            await command.ExecuteNonQueryAsync(cancellationToken);
            _isInitialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task UpsertDocumentAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, StoredDocument document, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {DocumentsTable}
                (document_id, source_id, source_name, source_type, source_location, content, ingested_at_utc)
            VALUES
                (@document_id, @source_id, @source_name, @source_type, @source_location, @content, @ingested_at_utc)
            ON CONFLICT (document_id) DO UPDATE
            SET source_id = EXCLUDED.source_id,
                source_name = EXCLUDED.source_name,
                source_type = EXCLUDED.source_type,
                source_location = EXCLUDED.source_location,
                content = EXCLUDED.content,
                ingested_at_utc = EXCLUDED.ingested_at_utc
            """, connection, transaction);

        command.Parameters.AddWithValue("document_id", document.DocumentId);
        command.Parameters.AddWithValue("source_id", document.SourceId);
        command.Parameters.AddWithValue("source_name", document.SourceName);
        command.Parameters.AddWithValue("source_type", document.SourceType);
        command.Parameters.AddWithValue("source_location", (object?)document.SourceLocation ?? DBNull.Value);
        command.Parameters.AddWithValue("content", document.Content);
        command.Parameters.AddWithValue("ingested_at_utc", document.IngestedAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DeleteChunksAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string documentId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"DELETE FROM {ChunksTable} WHERE document_id = @document_id", connection, transaction);
        command.Parameters.AddWithValue("document_id", documentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertChunkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DocumentChunk chunk, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {ChunksTable}
                (chunk_id, document_id, source_id, source_name, source_location, content)
            VALUES
                (@chunk_id, @document_id, @source_id, @source_name, @source_location, @content)
            """, connection, transaction);

        command.Parameters.AddWithValue("chunk_id", chunk.ChunkId);
        command.Parameters.AddWithValue("document_id", chunk.DocumentId);
        command.Parameters.AddWithValue("source_id", chunk.SourceId);
        command.Parameters.AddWithValue("source_name", chunk.SourceName);
        command.Parameters.AddWithValue("source_location", (object?)chunk.SourceLocation ?? DBNull.Value);
        command.Parameters.AddWithValue("content", chunk.Content);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string SchemaName => QuoteIdentifier(_options.Schema);

    private string DocumentsTable => $"{SchemaName}.{QuoteIdentifier(_options.DocumentsTableName)}";

    private string ChunksTable => $"{SchemaName}.{QuoteIdentifier(_options.ChunksTableName)}";

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
