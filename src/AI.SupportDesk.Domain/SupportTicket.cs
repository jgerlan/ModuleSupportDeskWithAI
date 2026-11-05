namespace AI.SupportDesk.Domain;

public enum TicketStatus { Open, InProgress, Resolved, Closed }
public enum TicketPriority { Low, Medium, High, Critical }

public sealed class SupportTicket
{
    public const int TitleMaxLength = 150;
    public const int DescriptionMaxLength = 5000;
    public const int CategoryMaxLength = 100;

    private SupportTicket() { }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public TicketStatus Status { get; private set; }
    public TicketPriority Priority { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public static SupportTicket Create(string title, string description, string category,
        TicketStatus status, TicketPriority priority, DateTimeOffset now)
    {
        var ticket = new SupportTicket { Id = Guid.NewGuid(), CreatedAt = now.ToUniversalTime() };
        ticket.Update(title, description, category, status, priority, now);
        ticket.UpdatedAt = null;
        return ticket;
    }

    public void Update(string title, string description, string category,
        TicketStatus status, TicketPriority priority, DateTimeOffset now)
    {
        title = NormalizeText(title, nameof(title), TitleMaxLength);
        description = NormalizeText(description, nameof(description), DescriptionMaxLength);
        category = NormalizeText(category, nameof(category), CategoryMaxLength);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (!Enum.IsDefined(priority))
            throw new ArgumentOutOfRangeException(nameof(priority));

        var unchanged = Title == title && Description == description && Category == category
            && Status == status && Priority == priority;
        if (unchanged)
            return;

        var utcNow = now.ToUniversalTime();
        if (status == TicketStatus.Resolved && Status != TicketStatus.Resolved)
            ResolvedAt = utcNow;
        if (status is TicketStatus.Open or TicketStatus.InProgress)
            ResolvedAt = null;

        Title = title;
        Description = description;
        Category = category;
        Status = status;
        Priority = priority;
        UpdatedAt = utcNow;
    }

    private static string NormalizeText(string value, string parameterName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException($"Use {maxLength} characters or fewer.", parameterName);
        return normalized;
    }
}
