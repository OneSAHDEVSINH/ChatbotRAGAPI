using System.Text.Json;
using ChatbotRAGAPI.Contracts;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotRAGAPI.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ISourceAccessService _sourceAccessService;

    public ChatController(IChatService chatService, ISourceAccessService sourceAccessService)
    {
        _chatService = chatService;
        _sourceAccessService = sourceAccessService;
    }

    [HttpPost("ask")]
    [ProducesResponseType<AskQuestionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AskQuestionResponse>> AskAsync([FromBody] AskQuestionRequest request, CancellationToken cancellationToken)
    {
        if (!_sourceAccessService.CanAccessSource(request.SourceId, requireExplicitSourceForRestrictedWrites: false))
        {
            return Forbid();
        }

        var response = await _chatService.AskAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("ask/stream")]
    public async Task StreamAsync([FromBody] AskQuestionRequest request, CancellationToken cancellationToken)
    {
        if (!_sourceAccessService.CanAccessSource(request.SourceId, requireExplicitSourceForRestrictedWrites: false))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        await foreach (var streamEvent in _chatService.StreamAsync(request, cancellationToken))
        {
            var payload = JsonSerializer.Serialize(streamEvent);
            await Response.WriteAsync($"event: {streamEvent.Type}\n", cancellationToken);
            await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
