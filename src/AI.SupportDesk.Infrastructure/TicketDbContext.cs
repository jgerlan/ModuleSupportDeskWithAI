using AI.SupportDesk.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.SupportDesk.Infrastructure;

public sealed class TicketDbContext(DbContextOptions<TicketDbContext> options) : DbContext(options)
{
    public DbSet<SupportTicket> Tickets => Set<SupportTicket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new TicketConfiguration());
}

internal sealed class TicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> ticket)
    {
        ticket.ToTable("Tickets", table =>
        {
            table.HasCheckConstraint("CK_Tickets_DescriptionLength", "DATALENGTH([Description]) BETWEEN 2 AND 10000");
            table.HasCheckConstraint("CK_Tickets_Status", "[Status] BETWEEN 0 AND 3");
            table.HasCheckConstraint("CK_Tickets_Priority", "[Priority] BETWEEN 0 AND 3");
        });
        ticket.HasKey(x => x.Id);
        ticket.Property(x => x.Id).ValueGeneratedNever();
        ticket.Property(x => x.Title).HasMaxLength(SupportTicket.TitleMaxLength).IsRequired().UseCollation("Latin1_General_100_CI_AS");
        ticket.Property(x => x.Category).HasMaxLength(SupportTicket.CategoryMaxLength).IsRequired().UseCollation("Latin1_General_100_CI_AS");
        ticket.Property(x => x.Description).HasColumnType("nvarchar(max)").IsRequired().UseCollation("Latin1_General_100_CI_AS");
        ticket.Property(x => x.CreatedAt).HasColumnType("datetimeoffset");
        ticket.Property(x => x.UpdatedAt).HasColumnType("datetimeoffset");
        ticket.Property(x => x.ResolvedAt).HasColumnType("datetimeoffset");
        ticket.HasIndex(x => new { x.CreatedAt, x.Id });
    }
}
