using AI.SupportDesk.Application;
using AI.SupportDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.SupportDesk.Infrastructure;

public static class PersistenceRegistration
{
    public static IServiceCollection AddTicketPersistence(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<TicketDbContext>(options => options.UseSqlServer(connectionString));
        return services.AddScoped<ITicketStore, TicketStore>();
    }
}

internal sealed class TicketStore(TicketDbContext db) : ITicketStore
{
    public async Task<TicketPage> ListAsync(TicketQuery query, CancellationToken cancellationToken)
    {
        var tickets = db.Tickets.AsNoTracking();
        if (query.Status is { } status)
            tickets = tickets.Where(x => x.Status == status);
        if (query.Priority is { } priority)
            tickets = tickets.Where(x => x.Priority == priority);
        if (query.Category is { } category)
            tickets = tickets.Where(x => x.Category == category);
        if (query.Search is { } search)
            tickets = tickets.Where(x => x.Title.Contains(search) || x.Description.Contains(search));

        var total = await tickets.CountAsync(cancellationToken);
        var offset = (long)(query.Page - 1) * query.PageSize;
        if (offset >= total)
            return new TicketPage([], query.Page, query.PageSize, total);

        var ordered = query.OldestFirst
            ? tickets.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            : tickets.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);

        var items = await ordered.Skip((int)offset).Take(query.PageSize)
            .Select(x => new TicketDto(x.Id, x.Title, x.Description, x.Category, x.Status, x.Priority,
                x.CreatedAt, x.UpdatedAt, x.ResolvedAt))
            .ToListAsync(cancellationToken);
        return new TicketPage(items, query.Page, query.PageSize, total);
    }

    public async Task<TicketSummary> SummaryAsync(CancellationToken cancellationToken) =>
        await db.Tickets.AsNoTracking().GroupBy(_ => 1).Select(g => new TicketSummary(
            g.Count(),
            g.Count(x => x.Status == TicketStatus.Open),
            g.Count(x => x.Status == TicketStatus.InProgress),
            g.Count(x => x.Status == TicketStatus.Resolved),
            g.Count(x => x.Priority == TicketPriority.Critical)))
            .SingleOrDefaultAsync(cancellationToken) ?? new TicketSummary(0, 0, 0, 0, 0);

    public Task<SupportTicket?> FindAsync(Guid id, bool forUpdate, CancellationToken cancellationToken)
    {
        var tickets = forUpdate ? db.Tickets.AsTracking() : db.Tickets.AsNoTracking();
        return tickets.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public void Add(SupportTicket ticket) => db.Tickets.Add(ticket);
    public void Remove(SupportTicket ticket) => db.Tickets.Remove(ticket);

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // An update or delete lost a race with deletion.
            return false;
        }
    }
}
