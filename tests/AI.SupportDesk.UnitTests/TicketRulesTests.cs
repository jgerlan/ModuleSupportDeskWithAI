using AI.SupportDesk.Application;
using AI.SupportDesk.Domain;
using Xunit;

namespace AI.SupportDesk.UnitTests;

public sealed class TicketRulesTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Validation_reports_blank_oversized_and_invalid_fields()
    {
        var errors = new TicketInput("  ", new string('a', 5001), new string('b', 101),
            (TicketStatus)12, null).Validate();
        Assert.Equal(5, errors.Count);
        Assert.Contains("title", errors.Keys);
        Assert.Contains("description", errors.Keys);
        Assert.Contains("category", errors.Keys);
        Assert.Contains("status", errors.Keys);
        Assert.Contains("priority", errors.Keys);
        Assert.Empty(new TicketInput(" " + new string('a', 150), new string('b', 5000),
            new string('c', 100), TicketStatus.Open, TicketPriority.Low).Validate());
    }

    [Fact]
    public void Resolution_survives_closing_and_clears_on_reopening()
    {
        var ticket = SupportTicket.Create("Outage", "Details", "Network", TicketStatus.Open, TicketPriority.High, Started);
        Assert.Equal(TimeSpan.Zero, ticket.CreatedAt.Offset);
        Assert.Null(ticket.UpdatedAt);
        Assert.Null(ticket.ResolvedAt);

        Update(TicketStatus.Resolved, 1);
        var resolvedAt = Started.AddHours(1).ToUniversalTime();
        Assert.Equal(resolvedAt, ticket.ResolvedAt);
        Assert.Equal(resolvedAt, ticket.UpdatedAt);
        Update(TicketStatus.Resolved, 2);
        Assert.Equal(resolvedAt, ticket.UpdatedAt);
        Update(TicketStatus.Closed, 3);
        Assert.Equal(resolvedAt, ticket.ResolvedAt);
        Update(TicketStatus.InProgress, 4);
        Assert.Null(ticket.ResolvedAt);
        Assert.Equal(Started.AddHours(4).ToUniversalTime(), ticket.UpdatedAt);

        void Update(TicketStatus status, int hours) =>
            ticket.Update("Outage", "Details", "Network", status, TicketPriority.High, Started.AddHours(hours));
    }

    [Fact]
    public void Creating_as_resolved_sets_the_resolution_without_an_update()
    {
        var ticket = SupportTicket.Create("Outage", "Details", "Network", TicketStatus.Resolved, TicketPriority.High, Started);
        Assert.Equal(ticket.CreatedAt, ticket.ResolvedAt);
        Assert.Null(ticket.UpdatedAt);
    }

    [Theory]
    [InlineData("title", null)]
    [InlineData("title", " ")]
    [InlineData("title", "oversized")]
    [InlineData("description", null)]
    [InlineData("description", " ")]
    [InlineData("description", "oversized")]
    [InlineData("category", null)]
    [InlineData("category", " ")]
    [InlineData("category", "oversized")]
    public void Invalid_domain_update_preserves_the_existing_ticket(string field, string? value)
    {
        var ticket = SupportTicket.Create("Title", "Description", "Category",
            TicketStatus.Open, TicketPriority.Medium, Started);
        var original = TicketDto.From(ticket);
        if (value == "oversized")
        {
            var limit = field switch
            {
                "title" => SupportTicket.TitleMaxLength,
                "description" => SupportTicket.DescriptionMaxLength,
                _ => SupportTicket.CategoryMaxLength
            };
            value = new string('a', limit + 1);
        }

        Assert.ThrowsAny<ArgumentException>(() => ticket.Update(
            field == "title" ? value! : "Title",
            field == "description" ? value! : "Description",
            field == "category" ? value! : "Category",
            TicketStatus.Resolved, TicketPriority.High, Started.AddHours(1)));
        Assert.Equal(original, TicketDto.From(ticket));
    }

    [Theory]
    [InlineData((TicketStatus)99, TicketPriority.Low)]
    [InlineData(TicketStatus.Open, (TicketPriority)99)]
    public void Domain_rejects_invalid_enums(TicketStatus status, TicketPriority priority)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SupportTicket.Create("Title", "Description", "Category", status, priority, Started));
    }

    [Fact]
    public void Normalized_unchanged_values_do_not_set_updated_timestamp()
    {
        var ticket = SupportTicket.Create(" Title ", " Description ", " Category ",
            TicketStatus.Open, TicketPriority.Medium, Started);
        ticket.Update(" Title ", "Description", "Category",
            TicketStatus.Open, TicketPriority.Medium, Started.AddHours(1));

        Assert.Equal("Title", ticket.Title);
        Assert.Equal("Description", ticket.Description);
        Assert.Equal("Category", ticket.Category);
        Assert.Null(ticket.UpdatedAt);
    }
}
