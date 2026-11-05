using AI.SupportDesk.Application;
using Microsoft.AspNetCore.Mvc;

namespace AI.SupportDesk.Api;

[ApiController]
[Route("api/tickets/{id}/analysis")]
public sealed class TicketAnalysisController(TicketAnalysisService analysis) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(TicketAnalysisDto), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    [ProducesResponseType(typeof(ProblemDetails), 504)]
    public async Task<ActionResult<TicketAnalysisDto>> Analyze(Guid id, CancellationToken cancellationToken)
    {
        var result = await analysis.AnalyzeAsync(id, cancellationToken);
        return result is null ? Problem(statusCode: 404, title: "Ticket not found.") : Ok(result);
    }
}
