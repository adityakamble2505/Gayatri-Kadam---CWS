using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.NotificationService.Entities;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.NotificationService.Data;

public sealed class NotificationDbContext : ServiceDbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(row => row.NotificationId);
            entity.Property(row => row.NotificationId).ValueGeneratedOnAdd();
            entity.Property(row => row.Type).HasMaxLength(40).IsRequired();
            entity.Property(row => row.Title).HasMaxLength(150).IsRequired();
            entity.Property(row => row.Message).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.ReferenceType).HasMaxLength(30);
            entity.Property(row => row.ReadAt).HasPrecision(6);
            entity.Property(row => row.CreatedAt).HasPrecision(6);
            entity.HasIndex(row => new { row.UserId, row.IsRead, row.CreatedAt }).HasDatabaseName("IX_Notif_User");
            entity.HasIndex(row => new { row.ReferenceType, row.ReferenceId }).HasDatabaseName("IX_Notif_Ref");
            entity.HasIndex(row => new { row.Type, row.ReferenceType, row.ReferenceId, row.UserId })
                .IsUnique()
                .HasFilter("[ReferenceType] IS NOT NULL AND [ReferenceId] IS NOT NULL")
                .HasDatabaseName("UX_Notif_Idempotency");
        });

        modelBuilder.Entity<EmailLog>(entity =>
        {
            entity.ToTable("EmailLogs");
            entity.HasKey(row => row.EmailLogId);
            entity.Property(row => row.EmailLogId).ValueGeneratedOnAdd();
            entity.Property(row => row.ToEmail).HasMaxLength(256).IsRequired();
            entity.Property(row => row.Subject).HasMaxLength(200).IsRequired();
            entity.Property(row => row.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(row => row.LastError).HasMaxLength(500);
            entity.Property(row => row.CreatedAt).HasPrecision(6);
            entity.Property(row => row.SentAt).HasPrecision(6);
            entity.HasIndex(row => new { row.Status, row.CreatedAt }).HasDatabaseName("IX_Email_Status");
        });
    }
}

public static class NotificationDataExtensions
{
    public static IServiceCollection AddNotificationData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = SqlConnectionStrings.ForDatabase(configuration, "NotificationDB", "NotificationDB");

        services.AddSingleton<UtcUpdatedAtInterceptor>();
        services.AddDbContext<NotificationDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<UtcUpdatedAtInterceptor>()));

        return services;
    }
}
