namespace ChatbotRAGAPI.Services.Interfaces;

public interface IEmbeddingClient
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);

    Task<float[]?> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken);
}
