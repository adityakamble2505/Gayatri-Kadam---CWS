using GreenCarWash.WasherService.Entities;
using GreenCarWash.BuildingBlocks.Data;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.WasherService.Data;

public sealed class WasherDbContext : ServiceDbContext
{
    public WasherDbContext(DbContextOptions<WasherDbContext> options) : base(options)
    {
    }

    public DbSet<WasherProfile> WasherProfiles => Set<WasherProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WasherProfile>(entity =>
        {
            entity.ToTable("WasherProfiles");
            entity.HasKey(washer => washer.WasherId);
            entity.Property(washer => washer.WasherId).ValueGeneratedOnAdd();
            entity.Property(washer => washer.UserId).IsRequired();
            entity.Property(washer => washer.CityId).IsRequired();
            entity.Property(washer => washer.IsAvailable).IsRequired();
            entity.Property(washer => washer.IsActive).IsRequired();
            entity.Property(washer => washer.IsQualified).IsRequired();
            entity.Property(washer => washer.AssignedAt).HasPrecision(6);
            entity.Property(washer => washer.AssignmentStatus)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(washer => washer.Latitude).HasPrecision(9, 6);
            entity.Property(washer => washer.Longitude).HasPrecision(9, 6);
            entity.Property(washer => washer.LastLocationAt).HasPrecision(6);
            entity.Property(washer => washer.AvgRating).HasPrecision(3, 2);
            entity.Property(washer => washer.TotalReviews).IsRequired();
            entity.Property(washer => washer.TotalCompletedWashes).IsRequired();
            entity.Property(washer => washer.ProfilePictureUrl).HasMaxLength(500);
            entity.Property(washer => washer.CreatedAt).HasPrecision(6).IsRequired();
            entity.Property(washer => washer.UpdatedAt).HasPrecision(6).IsRequired();
            entity.HasIndex(washer => washer.UserId).IsUnique().HasDatabaseName("UX_Washer_User");
            entity.HasIndex(washer => new { washer.CityId, washer.IsActive, washer.IsAvailable, washer.AssignmentStatus, washer.AvgRating })
                .HasDatabaseName("IX_Washer_Dispatch");
        });
    }
}

public static class WasherDataExtensions
{
    public static IServiceCollection AddWasherData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = SqlConnectionStrings.ForDatabase(configuration, "WasherDB", "WasherDB");

        services.AddSingleton<UtcUpdatedAtInterceptor>();
        services.AddDbContext<WasherDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<UtcUpdatedAtInterceptor>()));

        return services;
    }
}
