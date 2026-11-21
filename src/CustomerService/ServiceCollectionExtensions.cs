using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.CustomerService.Repositories;
using GreenCarWash.CustomerService.Services;

namespace GreenCarWash.CustomerService;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomerApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        var auth = configuration["ServiceUrls:Auth"]
            ?? throw new InvalidOperationException("ServiceUrls:Auth is not configured.");
        if (!auth.EndsWith('/'))
            auth += "/";

        services.AddInternalHttpClient<IActiveCityCatalog, AuthActiveCityCatalog>(new Uri(auth));
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddSingleton<IVehicleImageStore, DiskVehicleImageStore>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IVehicleService, VehicleService>();
        return services;
    }
}
