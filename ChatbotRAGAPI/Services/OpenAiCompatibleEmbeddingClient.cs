using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatbotRAGAPI.Options;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ChatbotRAGAPI.Services;

public sealed class OpenAiCompatibleEmbeddingClient : IEmbeddingClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly RagOptions _options;

    public OpenAiCompatibleEmbeddingClient(HttpClient httpClient, IOptions<RagOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured => _options.Embeddings.IsConfigured;

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        if (!IsConfigured || inputs.Count == 0)
        {
            return [];
        }

        try
        {
            return await RetryHelper.ExecuteAsync(async token =>
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, BuildRequestUri());
                if (!string.IsNullOrWhiteSpace(_options.Embeddings.ApiKey))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Embeddings.ApiKey);
                }

                var request = new EmbeddingsRequest
                {
                    Model = _options.Embeddings.Model!,
                    Input = inputs
                };

                message.Content = new StringContent(JsonSerializer.Serialize(request, SerializerOptions), Encoding.UTF8, "application/json");
                using var response = await _httpClient.SendAsync(message, token);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                var payload = await JsonSerializer.DeserializeAsync<EmbeddingsResponse>(stream, SerializerOptions, token);

                return payload?.Data?
                    .OrderBy(item => item.Index)
                    .Select(item => item.Embedding)
                    .ToArray() ?? [];
            }, _options.Resilience, cancellationToken);
        }
        catch
        {
            return [];
        }
    }

    public async Task<float[]?> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken)
    {
        var embeddings = await GenerateEmbeddingsAsync([input], cancellationToken);
        return embeddings.FirstOrDefault();
    }

    private Uri BuildRequestUri()
    {
        var baseUrl = _options.Embeddings.BaseUrl!;
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var absolute)
            ? new Uri(absolute, _options.Embeddings.EmbeddingsPath)
            : throw new InvalidOperationException("Rag:Embeddings:BaseUrl must be an absolute URL.");
    }

    private sealed class EmbeddingsRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public IReadOnlyList<string> Input { get; set; } = [];
    }

    private sealed class EmbeddingsResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingItem>? Data { get; set; }
    }

    private sealed class EmbeddingItem
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
