using GreenCarWash.BookingService.Entities;
using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.BookingService.Data;

public sealed class BookingDbContext : ServiceDbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options)
    {
    }

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<BookingAddOn> BookingAddOns => Set<BookingAddOn>();

    public DbSet<BookingStatusHistory> BookingStatusHistory => Set<BookingStatusHistory>();

    public DbSet<BookingOffer> BookingOffers => Set<BookingOffer>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("Bookings");
            entity.HasKey(booking => booking.BookingId);
            entity.Property(booking => booking.BookingId).ValueGeneratedOnAdd();
            entity.Property(booking => booking.BookingNumber).HasMaxLength(20).IsRequired();
            entity.Property(booking => booking.BookingType)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(booking => booking.BookingDate).HasPrecision(6);
            entity.Property(booking => booking.Address).HasMaxLength(500).IsRequired();
            entity.Property(booking => booking.Latitude).HasPrecision(9, 6);
            entity.Property(booking => booking.Longitude).HasPrecision(9, 6);
            entity.Property(booking => booking.Instructions).HasMaxLength(1000);
            entity.Property(booking => booking.ServiceNameSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(booking => booking.ServicePriceSnapshot).HasPrecision(10, 2);
            entity.Property(booking => booking.AddOnsTotal).HasPrecision(10, 2);
            entity.Property(booking => booking.PromoCode).HasMaxLength(30);
            entity.Property(booking => booking.PromoDiscount).HasPrecision(10, 2);
            entity.Property(booking => booking.TotalAmount).HasPrecision(10, 2);
            entity.Property(booking => booking.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(booking => booking.DispatchStartedAt).HasPrecision(6);
            entity.Property(booking => booking.AfterWashPhotoUrl).HasMaxLength(500);
            entity.Property(booking => booking.AcceptedAt).HasPrecision(6);
            entity.Property(booking => booking.StartedAt).HasPrecision(6);
            entity.Property(booking => booking.CompletedAt).HasPrecision(6);
            entity.Property(booking => booking.CancelledAt).HasPrecision(6);
            entity.Property(booking => booking.CancelledBy)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(booking => booking.CancelReason).HasMaxLength(500);
            entity.Property(booking => booking.CreatedAt).HasPrecision(6);
            entity.Property(booking => booking.UpdatedAt).HasPrecision(6);
            entity.HasIndex(booking => booking.BookingNumber).IsUnique().HasDatabaseName("UX_Booking_Number");
            entity.HasIndex(booking => new { booking.CustomerId, booking.CreatedAt }).HasDatabaseName("IX_Booking_Customer");
            entity.HasIndex(booking => new { booking.WasherId, booking.Status, booking.BookingDate }).HasDatabaseName("IX_Booking_Washer");
            entity.HasIndex(booking => new { booking.Status, booking.WasherId, booking.DispatchStartedAt, booking.BookingDate }).HasDatabaseName("IX_Booking_Dispatch");
            entity.HasIndex(booking => new { booking.CityId, booking.Status, booking.BookingDate }).HasDatabaseName("IX_Booking_City_Status");
            entity.HasIndex(booking => new { booking.Status, booking.CompletedAt }).HasDatabaseName("IX_Booking_Completed");
            entity.HasIndex(booking => new { booking.ServiceId, booking.Status }).HasDatabaseName("IX_Booking_Service");
            entity.HasIndex(booking => new { booking.CustomerId, booking.Status, booking.BookingDate }).HasDatabaseName("IX_Booking_Customer_Status_Date");
            entity.HasIndex(booking => new { booking.Status, booking.BookingType, booking.BookingDate }).HasDatabaseName("IX_Booking_Reminder");
            entity.HasIndex(booking => new { booking.Status, booking.CustomerId, booking.WaterSavedLiters }).HasDatabaseName("IX_Booking_Leaderboard");
            entity.HasIndex(booking => new { booking.Status, booking.BookingDate }).HasDatabaseName("IX_Booking_Status_Date");
            entity.HasIndex(booking => new { booking.Status, booking.CityId, booking.CompletedAt }).HasDatabaseName("IX_Booking_Completed_City");
            entity.HasIndex(booking => new { booking.Status, booking.WasherId, booking.StartedAt, booking.CompletedAt }).HasDatabaseName("IX_Booking_Washer_Performance");
        });

        modelBuilder.Entity<BookingAddOn>(entity =>
        {
            entity.ToTable("BookingAddOns");
            entity.HasKey(addOn => addOn.BookingAddOnId);
            entity.Property(addOn => addOn.BookingAddOnId).ValueGeneratedOnAdd();
            entity.Property(addOn => addOn.NameSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(addOn => addOn.PriceSnapshot).HasPrecision(10, 2);
            entity.HasIndex(addOn => new { addOn.BookingId, addOn.AddOnId }).IsUnique().HasDatabaseName("UX_BA");
            entity.HasIndex(addOn => addOn.AddOnId).HasDatabaseName("IX_BA_AddOn");
            entity.HasOne(addOn => addOn.Booking)
                .WithMany(booking => booking.AddOns)
                .HasForeignKey(addOn => addOn.BookingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_BA_Booking");
        });

        modelBuilder.Entity<BookingStatusHistory>(entity =>
        {
            entity.ToTable("BookingStatusHistory");
            entity.HasKey(history => history.HistoryId);
            entity.Property(history => history.HistoryId).ValueGeneratedOnAdd();
            entity.Property(history => history.FromStatus).HasMaxLength(20);
            entity.Property(history => history.ToStatus).HasMaxLength(20).IsRequired();
            entity.Property(history => history.ChangedByRole).HasMaxLength(20).IsRequired();
            entity.Property(history => history.Note).HasMaxLength(500);
            entity.Property(history => history.ChangedAt).HasPrecision(6);
            entity.HasIndex(history => new { history.BookingId, history.ChangedAt }).HasDatabaseName("IX_BSH_Booking");
            entity.HasOne(history => history.Booking)
                .WithMany()
                .HasForeignKey(history => history.BookingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_BSH_Booking");
        });

        modelBuilder.Entity<BookingOffer>(entity =>
        {
            entity.ToTable("BookingOffers");
            entity.HasKey(offer => offer.OfferId);
            entity.Property(offer => offer.OfferId).ValueGeneratedOnAdd();
            entity.Property(offer => offer.OfferedAt).HasPrecision(6);
            entity.Property(offer => offer.ExpiresAt).HasPrecision(6);
            entity.Property(offer => offer.Response)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(offer => offer.RespondedAt).HasPrecision(6);
            entity.HasIndex(offer => new { offer.BookingId, offer.Response }).HasDatabaseName("IX_Offer_Booking");
            entity.HasIndex(offer => new { offer.Response, offer.ExpiresAt }).HasDatabaseName("IX_Offer_Pending");
            entity.HasIndex(offer => new { offer.WasherId, offer.OfferedAt }).HasDatabaseName("IX_Offer_Washer");
            entity.HasOne(offer => offer.Booking)
                .WithMany()
                .HasForeignKey(offer => offer.BookingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Offer_Booking");
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("Reviews");
            entity.HasKey(review => review.ReviewId);
            entity.Property(review => review.ReviewId).ValueGeneratedOnAdd();
            entity.Property(review => review.ReviewerRole)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(review => review.Rating).HasColumnType("tinyint");
            entity.Property(review => review.Comments).HasMaxLength(1000);
            entity.Property(review => review.CreatedAt).HasPrecision(6);
            entity.HasIndex(review => new { review.BookingId, review.ReviewerRole })
                .IsUnique()
                .HasDatabaseName("UX_Review_Booking_Role");
            entity.HasIndex(review => new { review.WasherId, review.ReviewerRole, review.CreatedAt })
                .HasDatabaseName("IX_Review_Washer");
            entity.HasOne<Booking>()
                .WithMany()
                .HasForeignKey(review => review.BookingId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Review_Booking");
        });

        modelBuilder.ApplyOutbox();
    }
}

public static class BookingDataExtensions
{
    public static IServiceCollection AddBookingData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = SqlConnectionStrings.ForDatabase(configuration, "BookingDB", "BookingDB");

        services.AddSingleton<UtcUpdatedAtInterceptor>();
        services.AddDbContext<BookingDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<UtcUpdatedAtInterceptor>()));

        return services;
    }
}
