using Microsoft.EntityFrameworkCore;
using Ticketing.DataAccess.Entities;

namespace Ticketing.DataAccess;

/// <summary>
/// Entity Framework DbContext for the ticketing system.
/// </summary>
public class TicketingDbContext : DbContext
{
    public TicketingDbContext(DbContextOptions<TicketingDbContext> options)
        : base(options)
    {
    }

    public DbSet<TicketEntity> Tickets => Set<TicketEntity>();
    public DbSet<TicketHistoryEntity> TicketHistory => Set<TicketHistoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureTicket(modelBuilder);
        ConfigureTicketHistory(modelBuilder);
    }

    private static void ConfigureTicket(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TicketEntity>(entity =>
        {
            entity.ToTable("Tickets");
            
            entity.HasKey(e => e.TicketId);
            
            entity.Property(e => e.TicketId)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.Description)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.TicketType)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Category)
                .HasMaxLength(50);

            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .HasDefaultValue("medium");

            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(e => e.AssignedQueue)
                .HasMaxLength(50);

            entity.Property(e => e.AssignedTo)
                .HasMaxLength(100);

            entity.Property(e => e.CreatedBy)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.CreatedByName)
                .HasMaxLength(100);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.TriageDecision)
                .HasMaxLength(50);

            entity.Property(e => e.TriageNotes)
                .HasMaxLength(500);

            entity.Property(e => e.ResolutionNotes)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.ParentTicketId)
                .HasMaxLength(20);

            // Self-referential relationship for parent-child tickets
            entity.HasOne(e => e.ParentTicket)
                .WithMany(e => e.ChildTickets)
                .HasForeignKey(e => e.ParentTicketId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.AssignedQueue, e.Status });
            entity.HasIndex(e => e.ParentTicketId);
            entity.HasIndex(e => e.CreatedBy);
        });
    }

    private static void ConfigureTicketHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TicketHistoryEntity>(entity =>
        {
            entity.ToTable("TicketHistory");
            
            entity.HasKey(e => e.HistoryId);
            
            entity.Property(e => e.HistoryId)
                .UseIdentityColumn();

            entity.Property(e => e.TicketId)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.FieldName)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.OldValue)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.NewValue)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.ChangedBy)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.ChangedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.ChangeReason)
                .HasMaxLength(200);

            // Relationship
            entity.HasOne(e => e.Ticket)
                .WithMany(e => e.History)
                .HasForeignKey(e => e.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index
            entity.HasIndex(e => new { e.TicketId, e.ChangedAt });
        });
    }
}
