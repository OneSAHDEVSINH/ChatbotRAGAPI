using System.ComponentModel.DataAnnotations;

namespace ChatbotRAGAPI.Contracts;

public sealed class IngestWebPageRequest
{
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    public string? SourceId { get; set; }

    public string? SourceName { get; set; }
}
