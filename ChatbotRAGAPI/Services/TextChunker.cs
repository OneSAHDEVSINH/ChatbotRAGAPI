using ChatbotRAGAPI.Services.Interfaces;

namespace ChatbotRAGAPI.Services;

public sealed class TextChunker : ITextChunker
{
    public IReadOnlyList<string> Chunk(string content, int chunkSize, int chunkOverlap)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var normalized = content.Replace("\r\n", "\n").Trim();
        if (normalized.Length <= chunkSize)
        {
            return [normalized];
        }

        var chunks = new List<string>();
        var start = 0;

        while (start < normalized.Length)
        {
            var targetLength = Math.Min(chunkSize, normalized.Length - start);
            var end = FindBoundary(normalized, start, targetLength);
            var chunk = normalized[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            if (end >= normalized.Length)
            {
                break;
            }

            start = Math.Max(0, end - chunkOverlap);
        }

        return chunks;
    }

    private static int FindBoundary(string content, int start, int targetLength)
    {
        var end = start + targetLength;
        if (end >= content.Length)
        {
            return content.Length;
        }

        var paragraphBoundary = content.LastIndexOf("\n\n", end, Math.Min(targetLength, end - start), StringComparison.Ordinal);
        if (paragraphBoundary > start)
        {
            return paragraphBoundary + 2;
        }

        var sentenceBoundary = content.LastIndexOfAny(['.', '!', '?', '\n'], end);
        if (sentenceBoundary > start)
        {
            return sentenceBoundary + 1;
        }

        return end;
    }
}
