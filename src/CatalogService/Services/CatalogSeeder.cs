using GreenCarWash.CatalogService.Data;
using GreenCarWash.CatalogService.Entities;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CatalogService.Services;

public static class CatalogSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CatalogSeeder");
        await SeedAsync(db, clock, logger, cancellationToken);
    }

    public static async Task SeedAsync(
        CatalogDbContext db,
        IClock clock,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Services.AnyAsync(cancellationToken))
        {
            var now = clock.UtcNow;
            db.Services.AddRange(
                Package("Basic", "Exterior hand wash and dry", 19.99m, 30, 15, now),
                Package("Premium", "Wash, wax, and interior wipe-down", 39.99m, 45, 30, now),
                Package("Deluxe", "Full detail with a water-saving cycle", 69.99m, 75, 50, now));
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded catalog services");
        }

        if (!await db.AddOns.AnyAsync(cancellationToken))
        {
            db.AddOns.AddRange(
                new AddOn { AddOnName = "Interior Vacuum", Price = 14.99m, IsActive = true },
                new AddOn { AddOnName = "Ceramic Wax", Price = 24.99m, IsActive = true },
                new AddOn { AddOnName = "Tire Dressing", Price = 9.99m, IsActive = true },
                new AddOn { AddOnName = "Engine Wash", Price = 19.99m, IsActive = true });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded catalog add-ons");
        }

        if (!await db.PromoCodes.AnyAsync(cancellationToken))
        {
            var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            db.PromoCodes.AddRange(
                new PromoCode
                {
                    Code = "WELCOME10",
                    DiscountType = DiscountType.Percent,
                    DiscountValue = 10.00m,
                    MinOrderAmount = 0.00m,
                    MaxDiscount = 15.00m,
                    ValidFrom = from,
                    ValidTo = to,
                    UsageLimit = null,
                    UsedCount = 0,
                    IsActive = true
                },
                new PromoCode
                {
                    Code = "FLAT5",
                    DiscountType = DiscountType.Flat,
                    DiscountValue = 5.00m,
                    MinOrderAmount = 25.00m,
                    MaxDiscount = null,
                    ValidFrom = from,
                    ValidTo = to,
                    UsageLimit = 1000,
                    UsedCount = 0,
                    IsActive = true
                });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded catalog promo codes");
        }
    }

    private static WashService Package(string name, string description, decimal price, int duration, int water, DateTime now) =>
        new()
        {
            ServiceName = name,
            Description = description,
            Price = price,
            Discount = 0,
            Duration = duration,
            WaterSavedLiters = water,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
}
