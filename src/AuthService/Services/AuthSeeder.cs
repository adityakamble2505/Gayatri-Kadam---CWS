using GreenCarWash.AuthService.Data;
using GreenCarWash.AuthService.Entities;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.AuthService.Services;

public static class AuthSeeder
{
    public static readonly (string CityName, string State)[] UsCities =
    [
        ("New York", "NY"),
        ("Los Angeles", "CA"),
        ("Chicago", "IL"),
        ("Houston", "TX"),
        ("Phoenix", "AZ")
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AuthSeeder");
        await SeedAsync(db, clock, passwords, configuration, logger, cancellationToken);
    }

    public static async Task SeedAsync(
        AuthDbContext db,
        IClock clock,
        IPasswordHasher passwords,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await SeedAdminAsync(db, clock, passwords, configuration, logger, cancellationToken);
        await SeedCitiesAsync(db, logger, cancellationToken);
    }

    private static async Task SeedAdminAsync(
        AuthDbContext db,
        IClock clock,
        IPasswordHasher passwords,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var email = Required(configuration, "Admin:Email").Trim().ToLowerInvariant();
        var password = Required(configuration, "Admin:Password");
        var fullName = configuration["Admin:FullName"] ?? "GreenCarWash Admin";
        var phone = configuration["Admin:PhoneNumber"] ?? "+12125550100";
        var now = clock.UtcNow;

        var admins = await db.Users.Where(user => user.Role == UserRole.Admin).ToListAsync(cancellationToken);
        if (admins.Count == 0)
        {
            db.Users.Add(new User
            {
                FullName = fullName,
                Email = email,
                PasswordHash = passwords.Hash(password),
                PhoneNumber = phone,
                Role = UserRole.Admin,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded admin user {Email}", email);
            return;
        }

        var replaced = false;
        foreach (var admin in admins.Where(admin => IsPlaceholderHash(admin.PasswordHash)))
        {
            admin.PasswordHash = passwords.Hash(password);
            admin.UpdatedAt = now;
            replaced = true;
            logger.LogInformation("Replaced placeholder admin password hash. UserId: {UserId}", admin.UserId);
        }

        if (replaced)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCitiesAsync(AuthDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        if (await db.Cities.AnyAsync(cancellationToken))
            return;

        foreach (var (cityName, state) in UsCities)
        {
            db.Cities.Add(new City
            {
                CityName = cityName,
                State = state,
                IsActive = true
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} cities", UsCities.Length);
    }

    public static bool IsPlaceholderHash(string passwordHash) =>
        passwordHash.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
        || !passwordHash.StartsWith("$2", StringComparison.Ordinal);

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{key} is not configured.");

        return value;
    }
}
