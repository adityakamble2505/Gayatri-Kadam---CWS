using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.BuildingBlocks.Data;

public abstract class ServiceDbContext : DbContext
{
    protected ServiceDbContext(DbContextOptions options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }
}

public static class OutboxModelBuilderExtensions
{
    public static void ApplyOutbox(this ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Outbox.OutboxEvent>();
        entity.ToTable("OutboxEvents");
        entity.HasKey(outboxEvent => outboxEvent.EventId);
        entity.Property(outboxEvent => outboxEvent.EventId).ValueGeneratedOnAdd();
        entity.Property(outboxEvent => outboxEvent.EventType).HasMaxLength(80).IsRequired();
        entity.Property(outboxEvent => outboxEvent.Payload).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(outboxEvent => outboxEvent.CreatedAt).HasPrecision(6).IsRequired();
        entity.Property(outboxEvent => outboxEvent.ProcessedAt).HasPrecision(6);
        entity.Property(outboxEvent => outboxEvent.RetryCount).IsRequired();
        entity.Property(outboxEvent => outboxEvent.NextAttemptAt).HasPrecision(6).IsRequired();
        entity.Property(outboxEvent => outboxEvent.LastError).HasMaxLength(500);
        entity.HasIndex(outboxEvent => new { outboxEvent.ProcessedAt, outboxEvent.NextAttemptAt, outboxEvent.EventId })
            .HasDatabaseName("IX_Outbox_Pending");
    }
}
