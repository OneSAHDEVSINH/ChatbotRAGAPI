using ChatbotRAGAPI.Contracts;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotRAGAPI.Controllers;

[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IAppTelemetry _appTelemetry;

    public DiagnosticsController(IAppTelemetry appTelemetry)
    {
        _appTelemetry = appTelemetry;
    }

    [HttpGet("summary")]
    [ProducesResponseType<DiagnosticsSummaryResponse>(StatusCodes.Status200OK)]
    public ActionResult<DiagnosticsSummaryResponse> GetSummary()
    {
        return Ok(_appTelemetry.GetSummary());
    }
}
