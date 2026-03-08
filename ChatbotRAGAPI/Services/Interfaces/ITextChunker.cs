namespace ChatbotRAGAPI.Services.Interfaces;

public interface ITextChunker
{
    IReadOnlyList<string> Chunk(string content, int chunkSize, int chunkOverlap);
}
