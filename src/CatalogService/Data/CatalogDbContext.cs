using GreenCarWash.CatalogService.Entities;
using GreenCarWash.BuildingBlocks.Data;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CatalogService.Data;

public sealed class CatalogDbContext : ServiceDbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    public DbSet<WashService> Services => Set<WashService>();

    public DbSet<AddOn> AddOns => Set<AddOn>();

    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WashService>(entity =>
        {
            entity.ToTable("Services");
            entity.HasKey(service => service.ServiceId);
            entity.Property(service => service.ServiceId).ValueGeneratedOnAdd();
            entity.Property(service => service.ServiceName).HasMaxLength(100).IsRequired();
            entity.Property(service => service.Description).HasMaxLength(500);
            entity.Property(service => service.Price).HasPrecision(10, 2);
            entity.Property(service => service.Discount).IsRequired();
            entity.Property(service => service.Duration).IsRequired();
            entity.Property(service => service.WaterSavedLiters).IsRequired();
            entity.Property(service => service.IsActive).IsRequired();
            entity.Property(service => service.CreatedAt).HasPrecision(6).IsRequired();
            entity.Property(service => service.UpdatedAt).HasPrecision(6).IsRequired();
            entity.HasIndex(service => service.ServiceName).IsUnique().HasDatabaseName("UX_Service_Name");
        });

        modelBuilder.Entity<AddOn>(entity =>
        {
            entity.ToTable("AddOns");
            entity.HasKey(addOn => addOn.AddOnId);
            entity.Property(addOn => addOn.AddOnId).ValueGeneratedOnAdd();
            entity.Property(addOn => addOn.AddOnName).HasMaxLength(100).IsRequired();
            entity.Property(addOn => addOn.Price).HasPrecision(10, 2);
            entity.Property(addOn => addOn.IsActive).IsRequired();
            entity.HasIndex(addOn => addOn.AddOnName).IsUnique().HasDatabaseName("UX_AddOn_Name");
        });

        modelBuilder.Entity<PromoCode>(entity =>
        {
            entity.ToTable("PromoCodes");
            entity.HasKey(promo => promo.PromoId);
            entity.Property(promo => promo.PromoId).ValueGeneratedOnAdd();
            entity.Property(promo => promo.Code).HasMaxLength(30).IsRequired();
            entity.Property(promo => promo.DiscountType)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(promo => promo.DiscountValue).HasPrecision(10, 2);
            entity.Property(promo => promo.MinOrderAmount).HasPrecision(10, 2);
            entity.Property(promo => promo.MaxDiscount).HasPrecision(10, 2);
            entity.Property(promo => promo.ValidFrom).HasPrecision(6).IsRequired();
            entity.Property(promo => promo.ValidTo).HasPrecision(6).IsRequired();
            entity.Property(promo => promo.UsedCount).IsRequired();
            entity.Property(promo => promo.IsActive).IsRequired();
            entity.HasIndex(promo => promo.Code).IsUnique().HasDatabaseName("UX_Promo_Code");
        });
    }
}

public static class CatalogDataExtensions
{
    public static IServiceCollection AddCatalogData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = SqlConnectionStrings.ForDatabase(configuration, "CatalogDB", "CatalogDB");

        services.AddSingleton<UtcUpdatedAtInterceptor>();
        services.AddDbContext<CatalogDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<UtcUpdatedAtInterceptor>()));

        return services;
    }
}
