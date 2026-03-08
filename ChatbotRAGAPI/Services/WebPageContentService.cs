using System.Net;
using HtmlAgilityPack;
using ChatbotRAGAPI.Options;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ChatbotRAGAPI.Services;

public sealed class WebPageContentService : IWebPageContentService
{
    private readonly HttpClient _httpClient;
    private readonly RagOptions _options;

    public WebPageContentService(HttpClient httpClient, IOptions<RagOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<(string Title, string Content)> FetchAsync(string url, CancellationToken cancellationToken)
    {
        var html = await RetryHelper.ExecuteAsync(token => _httpClient.GetStringAsync(url, token), _options.Resilience, cancellationToken);
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var nodesToRemove = document.DocumentNode.SelectNodes("//script|//style|//noscript");
        if (nodesToRemove is not null)
        {
            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        var title = WebUtility.HtmlDecode(document.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? url);
        var bodyText = WebUtility.HtmlDecode(document.DocumentNode.InnerText);
        var normalized = string.Join("\n", bodyText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line)));

        return (title, normalized);
    }
}
