using ChatbotRAGAPI.Domain;

namespace ChatbotRAGAPI.Services.Interfaces;

public interface ILlmClient
{
    Task<string?> GenerateGroundedAnswerAsync(string question, IReadOnlyList<RetrievedChunk> contextChunks, CancellationToken cancellationToken);

    IAsyncEnumerable<string> StreamGroundedAnswerAsync(string question, IReadOnlyList<RetrievedChunk> contextChunks, CancellationToken cancellationToken);
}
