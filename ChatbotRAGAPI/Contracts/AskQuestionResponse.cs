namespace ChatbotRAGAPI.Contracts;

public sealed class AskQuestionResponse
{
    public string Answer { get; set; } = string.Empty;

    public bool Grounded { get; set; }

    public bool InsufficientContext { get; set; }

    public double Confidence { get; set; }

    public IReadOnlyList<CitationResponse> Citations { get; set; } = [];
}

public sealed class CitationResponse
{
    public string ChunkId { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public string? SourceLocation { get; set; }

    public string Snippet { get; set; } = string.Empty;

    public double Score { get; set; }
}
