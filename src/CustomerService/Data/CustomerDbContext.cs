using GreenCarWash.CustomerService.Entities;
using GreenCarWash.BuildingBlocks.Data;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CustomerService.Data;

public sealed class CustomerDbContext : ServiceDbContext
{
    public CustomerDbContext(DbContextOptions<CustomerDbContext> options) : base(options)
    {
    }

    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            entity.ToTable("CustomerProfiles");
            entity.HasKey(profile => profile.CustomerId);
            entity.Property(profile => profile.CustomerId).ValueGeneratedOnAdd();
            entity.Property(profile => profile.UserId).IsRequired();
            entity.Property(profile => profile.CityId).IsRequired();
            entity.Property(profile => profile.Address).HasMaxLength(500).IsRequired();
            entity.Property(profile => profile.Latitude).HasPrecision(9, 6);
            entity.Property(profile => profile.Longitude).HasPrecision(9, 6);
            entity.Property(profile => profile.CreatedAt).HasPrecision(6).IsRequired();
            entity.Property(profile => profile.UpdatedAt).HasPrecision(6).IsRequired();
            entity.HasIndex(profile => profile.UserId).IsUnique().HasDatabaseName("UX_Cust_User");
            entity.HasIndex(profile => profile.CityId).HasDatabaseName("IX_Cust_City");
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("Vehicles");
            entity.HasKey(vehicle => vehicle.VehicleId);
            entity.Property(vehicle => vehicle.VehicleId).ValueGeneratedOnAdd();
            entity.Property(vehicle => vehicle.VehicleNumber).HasMaxLength(20).IsRequired();
            entity.Property(vehicle => vehicle.Brand).HasMaxLength(60).IsRequired();
            entity.Property(vehicle => vehicle.Model).HasMaxLength(60).IsRequired();
            entity.Property(vehicle => vehicle.VehicleType)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(vehicle => vehicle.ImageUrl).HasMaxLength(500);
            entity.Property(vehicle => vehicle.IsActive).IsRequired();
            entity.Property(vehicle => vehicle.CreatedAt).HasPrecision(6).IsRequired();
            entity.HasIndex(vehicle => new { vehicle.CustomerId, vehicle.VehicleNumber })
                .IsUnique()
                .HasDatabaseName("UX_Veh_Cust_Number");
            entity.HasIndex(vehicle => new { vehicle.CustomerId, vehicle.IsActive })
                .HasDatabaseName("IX_Veh_Cust_Active");
            entity.HasOne(vehicle => vehicle.Customer)
                .WithMany(profile => profile.Vehicles)
                .HasForeignKey(vehicle => vehicle.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Veh_Cust");
        });
    }
}

public static class CustomerDataExtensions
{
    public static IServiceCollection AddCustomerData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = SqlConnectionStrings.ForDatabase(configuration, "CustomerDB", "CustomerDB");

        services.AddSingleton<UtcUpdatedAtInterceptor>();
        services.AddDbContext<CustomerDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(sp.GetRequiredService<UtcUpdatedAtInterceptor>()));

        return services;
    }
}
