using System.ComponentModel.DataAnnotations;
using AI.SupportDesk.Application;
using AI.SupportDesk.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AI.SupportDesk.Api;

public sealed class TicketListRequest
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public string? Status { get; init; }
    public string? Priority { get; init; }
    [StringLength(100)] public string? Category { get; init; }
    [StringLength(5000)] public string? Search { get; init; }
    [RegularExpression("^(asc|desc)$")] public string SortDirection { get; init; } = "desc";
}

[ApiController]
[Route("api/tickets")]
[ProducesResponseType(typeof(ProblemDetails), 500)]
public sealed class TicketsController(TicketService tickets) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<ActionResult<TicketPage>> List([FromQuery] TicketListRequest request, CancellationToken ct)
    {
        var status = ParseFilter<TicketStatus>(request.Status, "status");
        var priority = ParseFilter<TicketPriority>(request.Priority, "priority");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        return await tickets.ListAsync(new TicketQuery(request.Page, request.PageSize, status, priority,
            request.Category?.Trim(), request.Search?.Trim(), request.SortDirection == "asc"), ct);
    }

    [HttpGet("summary")]
    public Task<TicketSummary> Summary(CancellationToken ct) => tickets.SummaryAsync(ct);

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<TicketDto>> Get(Guid id, CancellationToken ct) =>
        await tickets.GetAsync(id, ct) is { } ticket ? Ok(ticket) : Missing();

    [HttpPost]
    [ProducesResponseType(typeof(TicketDto), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<ActionResult<TicketDto>> Create(TicketInput input, CancellationToken ct)
    {
        var ticket = await tickets.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = ticket.Id }, ticket);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<TicketDto>> Update(Guid id, TicketInput input, CancellationToken ct) =>
        await tickets.UpdateAsync(id, input, ct) is { } ticket ? Ok(ticket) : Missing();

    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await tickets.DeleteAsync(id, ct) ? NoContent() : Missing();

    private ObjectResult Missing() => Problem(statusCode: 404, title: "Ticket not found.");

    private T? ParseFilter<T>(string? value, string field) where T : struct, Enum
    {
        if (value is null)
            return null;

        if (Enum.GetNames<T>().Any(name => name.Equals(value, StringComparison.OrdinalIgnoreCase)))
            return Enum.Parse<T>(value, ignoreCase: true);

        ModelState.AddModelError(field, $"Use one of: {string.Join(", ", Enum.GetNames<T>())}.");
        return null;
    }
}
