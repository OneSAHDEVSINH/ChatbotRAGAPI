namespace ChatbotRAGAPI.Contracts;

public sealed class ChatStreamEvent
{
    public string Type { get; set; } = string.Empty;

    public string? Content { get; set; }

    public AskQuestionResponse? Response { get; set; }
}
