using System.ComponentModel.DataAnnotations;

namespace ChatbotRAGAPI.Contracts;

public sealed class IngestTextRequest
{
    [Required]
    public string Content { get; set; } = string.Empty;

    [Required]
    public string SourceName { get; set; } = string.Empty;

    public string? SourceId { get; set; }

    public string? SourceLocation { get; set; }
}
