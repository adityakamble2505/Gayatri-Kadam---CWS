using GreenCarWash.AuthService.Entities;
using GreenCarWash.BuildingBlocks.Data;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.AuthService.Data;

public sealed class AuthDbContext : ServiceDbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<City> Cities => Set<City>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.UserId);
            entity.Property(user => user.UserId).ValueGeneratedOnAdd();
            entity.Property(user => user.FullName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(256).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(user => user.PhoneNumber).HasMaxLength(20).IsRequired();
            entity.Property(user => user.Role)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(user => user.IsActive).IsRequired();
            entity.Property(user => user.FailedLoginCount).IsRequired();
            entity.Property(user => user.LockoutEndUtc).HasPrecision(6);
            entity.Property(user => user.LastLoginUtc).HasPrecision(6);
            entity.Property(user => user.CreatedAt).HasPrecision(6).IsRequired();
            entity.Property(user => user.UpdatedAt).HasPrecision(6).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique().HasDatabaseName("UX_Users_Email");
            entity.HasIndex(user => new { user.Role, user.IsActive }).HasDatabaseName("IX_Users_Role_Active");
        });

        modelBuilder.Entity<City>(entity =>
        {
            entity.ToTable("Cities");
            entity.HasKey(city => city.CityId);
            entity.Property(city => city.CityId).ValueGeneratedOnAdd();
            entity.Property(city => city.CityName).HasMaxLength(100).IsRequired();
            entity.Property(city => city.State).HasMaxLength(100).IsRequired();
            entity.Property(city => city.IsActive).IsRequired();
            entity.HasIndex(city => new { city.CityName, city.State }).IsUnique().HasDatabaseName("UX_Cities_Name_State");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(token => token.TokenId);
            entity.Property(token => token.TokenId).ValueGeneratedOnAdd();
            entity.Property(token => token.TokenHash).HasMaxLength(64).HasColumnType("char(64)").IsRequired();
            entity.Property(token => token.ExpiresAt).HasPrecision(6).IsRequired();
            entity.Property(token => token.RevokedAt).HasPrecision(6);
            entity.Property(token => token.CreatedAt).HasPrecision(6).IsRequired();
            entity.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("UX_RT_Hash");
            entity.HasIndex(token => new { token.UserId, token.RevokedAt }).HasDatabaseName("IX_RT_User");
            entity.HasOne(token => token.User)
                .WithMany(user => user.RefreshTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_RT_User");
        });
    }
}

public static class AuthDataExtensions
{
    public static IServiceCollection AddAuthData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = SqlConnectionStrings.ForDatabase(configuration, "AuthDB", "AuthDB");

        services.AddSingleton<UtcUpdatedAtInterceptor>();
        services.AddDbContext<AuthDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<UtcUpdatedAtInterceptor>()));

        return services;
    }
}
