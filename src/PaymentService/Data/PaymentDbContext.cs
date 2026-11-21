using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.PaymentService.Entities;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.PaymentService.Data;

public sealed class PaymentDbContext : ServiceDbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentRefund> PaymentRefunds => Set<PaymentRefund>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(payment => payment.PaymentId);
            entity.Property(payment => payment.PaymentId).ValueGeneratedOnAdd();
            entity.Property(payment => payment.Amount).HasPrecision(10, 2);
            entity.Property(payment => payment.Currency).HasMaxLength(3).IsRequired();
            entity.Property(payment => payment.PaymentMethod)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(payment => payment.CardBrand).HasMaxLength(20);
            entity.Property(payment => payment.CardLast4).HasMaxLength(4).IsFixedLength();
            entity.Property(payment => payment.PaymentStatus)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(payment => payment.TransactionReference).HasMaxLength(100);
            entity.Property(payment => payment.IdempotencyKey).HasMaxLength(64).IsRequired();
            entity.Property(payment => payment.FailureReason).HasMaxLength(300);
            entity.Property(payment => payment.RefundedAmount).HasPrecision(10, 2);
            entity.Property(payment => payment.PaymentDate).HasPrecision(6);
            entity.Property(payment => payment.CreatedAt).HasPrecision(6);
            entity.Property(payment => payment.UpdatedAt).HasPrecision(6);
            entity.HasIndex(payment => payment.BookingId).IsUnique().HasDatabaseName("UX_Pay_Booking");
            entity.HasIndex(payment => payment.IdempotencyKey).IsUnique().HasDatabaseName("UX_Pay_Idem");
            entity.HasIndex(payment => payment.TransactionReference)
                .IsUnique()
                .HasFilter("[TransactionReference] IS NOT NULL")
                .HasDatabaseName("UX_Pay_Tx");
            entity.HasIndex(payment => new { payment.CustomerId, payment.CreatedAt }).HasDatabaseName("IX_Pay_Customer");
            entity.HasIndex(payment => new { payment.PaymentStatus, payment.PaymentDate }).HasDatabaseName("IX_Pay_Status");
        });

        modelBuilder.Entity<PaymentRefund>(entity =>
        {
            entity.ToTable("PaymentRefunds");
            entity.HasKey(refund => refund.RefundId);
            entity.Property(refund => refund.RefundId).ValueGeneratedOnAdd();
            entity.Property(refund => refund.Amount).HasPrecision(10, 2);
            entity.Property(refund => refund.Reason).HasMaxLength(300).IsRequired();
            entity.Property(refund => refund.CreatedAt).HasPrecision(6);
            entity.HasIndex(refund => refund.PaymentId).HasDatabaseName("IX_Refund_Payment");
            entity.HasOne<Payment>()
                .WithMany()
                .HasForeignKey(refund => refund.PaymentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Refund_Payment");
        });

        modelBuilder.ApplyOutbox();
    }
}

public static class PaymentDataExtensions
{
    public static IServiceCollection AddPaymentData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = SqlConnectionStrings.ForDatabase(configuration, "PaymentDB", "PaymentDB");

        services.AddSingleton<UtcUpdatedAtInterceptor>();
        services.AddDbContext<PaymentDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<UtcUpdatedAtInterceptor>()));

        return services;
    }
}
