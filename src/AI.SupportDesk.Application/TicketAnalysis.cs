using AI.SupportDesk.Domain;

namespace AI.SupportDesk.Application;

public sealed record TicketAnalysisDto(string SuggestedCategory, TicketPriority SuggestedPriority,
    string Summary, IReadOnlyList<string> PossibleCauses, IReadOnlyList<string> RecommendedActions)
{
    public static TicketAnalysisDto Create(string? category, string? priority, string? summary,
        string[]? causes, string[]? actions)
    {
        if (!ValidText(category, 100) || !ValidText(summary, 2000)
            || priority is null || !Enum.GetNames<TicketPriority>().Contains(priority)
            || !ValidList(causes) || !ValidList(actions))
            throw new TicketAnalysisException(AnalysisFailure.InvalidResponse);

        return new(category!.Trim(), Enum.Parse<TicketPriority>(priority!), summary!.Trim(),
            causes!.Select(x => x.Trim()).ToArray(), actions!.Select(x => x.Trim()).ToArray());
    }

    private static bool ValidText(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= limit;

    private static bool ValidList(string[]? values) =>
        values is { Length: <= 8 } && values.All(x => ValidText(x, 1000));
}

public enum AnalysisFailure { InvalidResponse, Unavailable, Timeout, Authentication }

public sealed class TicketAnalysisException(AnalysisFailure failure) : Exception("Ticket analysis failed.")
{
    public AnalysisFailure Failure { get; } = failure;
}

public interface ITicketAnalysisProvider
{
    Task<TicketAnalysisDto> AnalyzeAsync(string title, string description, CancellationToken cancellationToken);
}

public sealed class TicketAnalysisService(TicketService tickets, ITicketAnalysisProvider provider)
{
    public async Task<TicketAnalysisDto?> AnalyzeAsync(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await tickets.GetAsync(id, cancellationToken);
        return ticket is null ? null :
            await provider.AnalyzeAsync(ticket.Title, ticket.Description, cancellationToken);
    }
}
