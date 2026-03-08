using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatbotRAGAPI.Domain;
using ChatbotRAGAPI.Options;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ChatbotRAGAPI.Services;

public sealed class QdrantVectorStore : IVectorStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly RagOptions _options;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _isInitialized;

    public QdrantVectorStore(HttpClient httpClient, IOptions<RagOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured => _options.VectorStore.IsConfigured;

    public async Task UpsertAsync(IReadOnlyList<(DocumentChunk Chunk, float[] Embedding)> entries, CancellationToken cancellationToken)
    {
        if (!IsConfigured || entries.Count == 0)
        {
            return;
        }

        try
        {
            await RetryHelper.ExecuteAsync(async token =>
            {
                await EnsureCollectionAsync(token);

                using var message = CreateRequest(HttpMethod.Put, $"/collections/{_options.VectorStore.CollectionName}/points?wait=true");
                var request = new UpsertPointsRequest
                {
                    Points = entries.Select(entry => new QdrantPoint
                    {
                        Id = entry.Chunk.ChunkId,
                        Vector = entry.Embedding,
                        Payload = new QdrantPayload
                        {
                            ChunkId = entry.Chunk.ChunkId,
                            DocumentId = entry.Chunk.DocumentId,
                            SourceId = entry.Chunk.SourceId,
                            SourceName = entry.Chunk.SourceName,
                            SourceLocation = entry.Chunk.SourceLocation,
                            Content = entry.Chunk.Content
                        }
                    }).ToArray()
                };

                message.Content = Serialize(request);
                using var response = await _httpClient.SendAsync(message, token);
                response.EnsureSuccessStatusCode();
            }, _options.Resilience, cancellationToken);
        }
        catch
        {
        }
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(float[] embedding, string? sourceId, int limit, CancellationToken cancellationToken)
    {
        if (!IsConfigured || embedding.Length == 0)
        {
            return [];
        }

        try
        {
            var payload = await RetryHelper.ExecuteAsync(async token =>
            {
                await EnsureCollectionAsync(token);

                using var message = CreateRequest(HttpMethod.Post, $"/collections/{_options.VectorStore.CollectionName}/points/search");
                var request = new SearchPointsRequest
                {
                    Vector = embedding,
                    Limit = limit,
                    WithPayload = true,
                    ScoreThreshold = _options.VectorStore.MinimumScore,
                    Filter = string.IsNullOrWhiteSpace(sourceId)
                        ? null
                        : new SearchFilter
                        {
                            Must =
                            [
                                new SearchCondition
                                {
                                    Key = "sourceId",
                                    Match = new MatchCondition
                                    {
                                        Value = sourceId
                                    }
                                }
                            ]
                        }
                };

                message.Content = Serialize(request);
                using var response = await _httpClient.SendAsync(message, token);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                return await JsonSerializer.DeserializeAsync<SearchResponse>(stream, SerializerOptions, token);
            }, _options.Resilience, cancellationToken);

            return payload?.Result?
                .Where(item => item.Payload is not null)
                .Select(item => new RetrievedChunk
                {
                    Chunk = new DocumentChunk
                    {
                        ChunkId = item.Payload!.ChunkId ?? item.Id,
                        DocumentId = item.Payload.DocumentId ?? string.Empty,
                        SourceId = item.Payload.SourceId ?? string.Empty,
                        SourceName = item.Payload.SourceName ?? string.Empty,
                        SourceLocation = item.Payload.SourceLocation,
                        Content = item.Payload.Content ?? string.Empty
                    },
                    Score = item.Score
                })
                .ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
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

            await RetryHelper.ExecuteAsync(async token =>
            {
                using var message = CreateRequest(HttpMethod.Put, $"/collections/{_options.VectorStore.CollectionName}");
                message.Content = Serialize(new CreateCollectionRequest
                {
                    Vectors = new VectorConfiguration
                    {
                        Size = _options.Embeddings.Dimensions,
                        Distance = _options.VectorStore.Distance
                    }
                });

                using var response = await _httpClient.SendAsync(message, token);
                response.EnsureSuccessStatusCode();
            }, _options.Resilience, cancellationToken);

            _isInitialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        if (!Uri.TryCreate(_options.VectorStore.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("Rag:VectorStore:BaseUrl must be an absolute URL.");
        }

        var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        if (!string.IsNullOrWhiteSpace(_options.VectorStore.ApiKey))
        {
            request.Headers.Add("api-key", _options.VectorStore.ApiKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.VectorStore.ApiKey);
        }

        return request;
    }

    private static StringContent Serialize<T>(T value)
    {
        return new StringContent(JsonSerializer.Serialize(value, SerializerOptions), Encoding.UTF8, "application/json");
    }

    private sealed class CreateCollectionRequest
    {
        [JsonPropertyName("vectors")]
        public VectorConfiguration Vectors { get; set; } = new();
    }

    private sealed class VectorConfiguration
    {
        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("distance")]
        public string Distance { get; set; } = "Cosine";
    }

    private sealed class UpsertPointsRequest
    {
        [JsonPropertyName("points")]
        public IReadOnlyList<QdrantPoint> Points { get; set; } = [];
    }

    private sealed class QdrantPoint
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("vector")]
        public float[] Vector { get; set; } = [];

        [JsonPropertyName("payload")]
        public QdrantPayload Payload { get; set; } = new();
    }

    private sealed class QdrantPayload
    {
        [JsonPropertyName("chunkId")]
        public string? ChunkId { get; set; }

        [JsonPropertyName("documentId")]
        public string? DocumentId { get; set; }

        [JsonPropertyName("sourceId")]
        public string? SourceId { get; set; }

        [JsonPropertyName("sourceName")]
        public string? SourceName { get; set; }

        [JsonPropertyName("sourceLocation")]
        public string? SourceLocation { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class SearchPointsRequest
    {
        [JsonPropertyName("vector")]
        public float[] Vector { get; set; } = [];

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("with_payload")]
        public bool WithPayload { get; set; }

        [JsonPropertyName("score_threshold")]
        public double ScoreThreshold { get; set; }

        [JsonPropertyName("filter")]
        public SearchFilter? Filter { get; set; }
    }

    private sealed class SearchFilter
    {
        [JsonPropertyName("must")]
        public IReadOnlyList<SearchCondition> Must { get; set; } = [];
    }

    private sealed class SearchCondition
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("match")]
        public MatchCondition Match { get; set; } = new();
    }

    private sealed class MatchCondition
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("result")]
        public List<SearchResultItem>? Result { get; set; }
    }

    private sealed class SearchResultItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("payload")]
        public QdrantPayload? Payload { get; set; }
    }
}
