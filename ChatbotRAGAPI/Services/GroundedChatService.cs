using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using ChatbotRAGAPI.Contracts;
using ChatbotRAGAPI.Domain;
using ChatbotRAGAPI.Services.Interfaces;

namespace ChatbotRAGAPI.Services;

public sealed class GroundedChatService : IChatService
{
    private const string InsufficientContextMessage = "I don't have enough context to answer that.";
    private static readonly Regex SentenceSplitRegex = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private readonly IAppTelemetry _appTelemetry;
    private readonly IRetrievalService _retrievalService;
    private readonly ILlmClient _llmClient;

    public GroundedChatService(IAppTelemetry appTelemetry, IRetrievalService retrievalService, ILlmClient llmClient)
    {
        _appTelemetry = appTelemetry;
        _retrievalService = retrievalService;
        _llmClient = llmClient;
    }

    public async Task<AskQuestionResponse> AskAsync(AskQuestionRequest request, CancellationToken cancellationToken)
    {
        return await BuildResponseAsync(request, cancellationToken);
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(AskQuestionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var retrieved = await _retrievalService.RetrieveAsync(request.Question, request.SourceId, cancellationToken);
        if (retrieved.Count == 0)
        {
            var insufficientResponse = BuildInsufficientContextResponse();
            foreach (var chunk in ChunkAnswer(insufficientResponse.Answer))
            {
                yield return new ChatStreamEvent
                {
                    Type = "delta",
                    Content = chunk
                };

                await Task.Yield();
            }

            yield return new ChatStreamEvent
            {
                Type = "complete",
                Response = insufficientResponse
            };

            _appTelemetry.TrackChat(stopwatch.Elapsed);
            yield break;
        }

        var streamedAnswer = new StringBuilder();
        var receivedProviderTokens = false;

        await foreach (var token in _llmClient.StreamGroundedAnswerAsync(request.Question, retrieved, cancellationToken))
        {
            receivedProviderTokens = true;
            streamedAnswer.Append(token);

            yield return new ChatStreamEvent
            {
                Type = "delta",
                Content = token
            };

            await Task.Yield();
        }

        var answer = receivedProviderTokens
            ? streamedAnswer.ToString().Trim()
            : await _llmClient.GenerateGroundedAnswerAsync(request.Question, retrieved, cancellationToken)
                ?? BuildExtractiveAnswer(request.Question, retrieved);

        if (!receivedProviderTokens)
        {
            foreach (var chunk in ChunkAnswer(answer))
            {
                yield return new ChatStreamEvent
                {
                    Type = "delta",
                    Content = chunk
                };

                await Task.Yield();
            }
        }

        var response = BuildResponse(answer, retrieved, request.MaxCitations);

        yield return new ChatStreamEvent
        {
            Type = "complete",
            Response = response
        };

        _appTelemetry.TrackChat(stopwatch.Elapsed);
    }

    private async Task<AskQuestionResponse> BuildResponseAsync(AskQuestionRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var retrieved = await _retrievalService.RetrieveAsync(request.Question, request.SourceId, cancellationToken);
        if (retrieved.Count == 0)
        {
            var insufficientResponse = BuildInsufficientContextResponse();

            _appTelemetry.TrackChat(stopwatch.Elapsed);
            return insufficientResponse;
        }

        var answer = await _llmClient.GenerateGroundedAnswerAsync(request.Question, retrieved, cancellationToken)
            ?? BuildExtractiveAnswer(request.Question, retrieved);
        var response = BuildResponse(answer, retrieved, request.MaxCitations);

        _appTelemetry.TrackChat(stopwatch.Elapsed);
        return response;
    }

    private static string BuildExtractiveAnswer(string question, IReadOnlyList<RetrievedChunk> retrieved)
    {
        var sentences = retrieved
            .SelectMany(result => SplitSentences(result.Chunk.Content)
                .Select(sentence => new
                {
                    Sentence = sentence.Trim(),
                    Score = ScoreSentence(question, sentence) + result.Score
                }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Sentence))
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => x.Sentence)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (sentences.Length == 0)
        {
            return InsufficientContextMessage;
        }

        return string.Join(" ", sentences);
    }

    private static IEnumerable<string> SplitSentences(string content)
    {
        return SentenceSplitRegex.Split(content).Where(sentence => !string.IsNullOrWhiteSpace(sentence));
    }

    private static double ScoreSentence(string question, string sentence)
    {
        var questionTerms = question.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (questionTerms.Length == 0)
        {
            return 0;
        }

        var matches = questionTerms.Count(term => sentence.Contains(term, StringComparison.OrdinalIgnoreCase));
        return matches / (double)questionTerms.Length;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static IEnumerable<string> ChunkAnswer(string answer)
    {
        if (string.IsNullOrEmpty(answer))
        {
            yield break;
        }

        const int chunkSize = 64;
        for (var index = 0; index < answer.Length; index += chunkSize)
        {
            yield return answer.Substring(index, Math.Min(chunkSize, answer.Length - index));
        }
    }

    private static AskQuestionResponse BuildInsufficientContextResponse()
    {
        return new AskQuestionResponse
        {
            Answer = InsufficientContextMessage,
            Grounded = false,
            InsufficientContext = true,
            Confidence = 0,
            Citations = []
        };
    }

    private static AskQuestionResponse BuildResponse(string answer, IReadOnlyList<RetrievedChunk> retrieved, int maxCitations)
    {
        var citations = retrieved
            .Take(maxCitations)
            .Select(result => new CitationResponse
            {
                ChunkId = result.Chunk.ChunkId,
                SourceId = result.Chunk.SourceId,
                SourceName = result.Chunk.SourceName,
                SourceLocation = result.Chunk.SourceLocation,
                Snippet = Truncate(result.Chunk.Content, 320),
                Score = Math.Round(result.Score, 4)
            })
            .ToArray();

        var confidence = Math.Round(retrieved.Average(x => x.Score), 4);
        var grounded = !string.Equals(answer, InsufficientContextMessage, StringComparison.Ordinal);

        return new AskQuestionResponse
        {
            Answer = answer,
            Grounded = grounded,
            InsufficientContext = !grounded,
            Confidence = grounded ? confidence : 0,
            Citations = citations
        };
    }
}
