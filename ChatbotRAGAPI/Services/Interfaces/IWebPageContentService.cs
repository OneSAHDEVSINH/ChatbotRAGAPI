namespace ChatbotRAGAPI.Services.Interfaces;

public interface IWebPageContentService
{
    Task<(string Title, string Content)> FetchAsync(string url, CancellationToken cancellationToken);
}
