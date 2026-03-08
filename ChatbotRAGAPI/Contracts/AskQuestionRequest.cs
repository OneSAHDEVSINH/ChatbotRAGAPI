using System.ComponentModel.DataAnnotations;

namespace ChatbotRAGAPI.Contracts;

public sealed class AskQuestionRequest
{
    [Required]
    public string Question { get; set; } = string.Empty;

    public string? SourceId { get; set; }

    [Range(1, 10)]
    public int MaxCitations { get; set; } = 3;
}
