using AI.SupportDesk.Domain;

namespace AI.SupportDesk.Application;

public sealed record TicketInput(string? Title, string? Description, string? Category,
    TicketStatus? Status, TicketPriority? Priority)
{
    public const int TitleLimit = SupportTicket.TitleMaxLength;
    public const int DescriptionLimit = SupportTicket.DescriptionMaxLength;
    public const int CategoryLimit = SupportTicket.CategoryMaxLength;

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        CheckText(errors, "title", Title, TitleLimit);
        CheckText(errors, "description", Description, DescriptionLimit);
        CheckText(errors, "category", Category, CategoryLimit);
        if (Status is null || !Enum.IsDefined(Status.Value))
            errors["status"] = ["Select a valid status."];
        if (Priority is null || !Enum.IsDefined(Priority.Value))
            errors["priority"] = ["Select a valid priority."];
        return errors;
    }

    private static void CheckText(Dictionary<string, string[]> errors, string name, string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors[name] = ["This field is required."];
        else if (value.Trim().Length > limit)
            errors[name] = [$"Use {limit} characters or fewer."];
    }
}

public sealed record TicketDto(Guid Id, string Title, string Description, string Category,
    TicketStatus Status, TicketPriority Priority, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    DateTimeOffset? ResolvedAt)
{
    public static TicketDto From(SupportTicket ticket) => new(ticket.Id, ticket.Title, ticket.Description,
        ticket.Category, ticket.Status, ticket.Priority, ticket.CreatedAt, ticket.UpdatedAt, ticket.ResolvedAt);
}

public sealed record TicketPage(IReadOnlyList<TicketDto> Items, int Page, int PageSize, int TotalCount);
public sealed record TicketSummary(int Total, int Open, int InProgress, int Resolved, int Critical);
public sealed record TicketQuery(int Page, int PageSize, TicketStatus? Status, TicketPriority? Priority,
    string? Category, string? Search, bool OldestFirst);

public interface ITicketStore
{
    Task<TicketPage> ListAsync(TicketQuery query, CancellationToken cancellationToken);
    Task<TicketSummary> SummaryAsync(CancellationToken cancellationToken);
    Task<SupportTicket?> FindAsync(Guid id, bool forUpdate, CancellationToken cancellationToken);
    void Add(SupportTicket ticket);
    void Remove(SupportTicket ticket);
    Task<bool> SaveAsync(CancellationToken cancellationToken);
}

public sealed class TicketValidationException(Dictionary<string, string[]> errors) : Exception("Invalid ticket")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

public sealed class TicketService(ITicketStore store, TimeProvider clock)
{
    public Task<TicketPage> ListAsync(TicketQuery query, CancellationToken ct) => store.ListAsync(query, ct);
    public Task<TicketSummary> SummaryAsync(CancellationToken ct) => store.SummaryAsync(ct);

    public async Task<TicketDto?> GetAsync(Guid id, CancellationToken ct) =>
        await store.FindAsync(id, false, ct) is { } ticket ? TicketDto.From(ticket) : null;

    public async Task<TicketDto> CreateAsync(TicketInput input, CancellationToken ct)
    {
        Validate(input);
        var ticket = SupportTicket.Create(input.Title!.Trim(), input.Description!.Trim(),
            input.Category!.Trim(), input.Status!.Value, input.Priority!.Value, clock.GetUtcNow());
        store.Add(ticket);
        await store.SaveAsync(ct);
        return TicketDto.From(ticket);
    }

    public async Task<TicketDto?> UpdateAsync(Guid id, TicketInput input, CancellationToken ct)
    {
        Validate(input);
        if (await store.FindAsync(id, true, ct) is not { } ticket)
            return null;

        ticket.Update(input.Title!.Trim(), input.Description!.Trim(), input.Category!.Trim(),
            input.Status!.Value, input.Priority!.Value, clock.GetUtcNow());
        return await store.SaveAsync(ct) ? TicketDto.From(ticket) : null;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (await store.FindAsync(id, true, ct) is not { } ticket)
            return false;

        store.Remove(ticket);
        return await store.SaveAsync(ct);
    }

    private static void Validate(TicketInput input)
    {
        var errors = input.Validate();
        if (errors.Count > 0)
            throw new TicketValidationException(errors);
    }
}
