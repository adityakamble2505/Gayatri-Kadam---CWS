using GreenCarWash.WasherService.Auth;
using GreenCarWash.WasherService.Queue;
using GreenCarWash.WasherService.Repositories;
using GreenCarWash.WasherService.Services;
using GreenCarWash.BuildingBlocks.Http;
using Microsoft.AspNetCore.Authorization;

namespace GreenCarWash.WasherService;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWasherApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<QueueOptions>(configuration.GetSection("Queue"));
        var auth = configuration["ServiceUrls:Auth"]
            ?? throw new InvalidOperationException("ServiceUrls:Auth is not configured.");
        if (!auth.EndsWith('/'))
            auth += "/";

        services.AddInternalHttpClient<IActiveCityCatalog, AuthActiveCityCatalog>(new Uri(auth));
        services.AddSingleton<FifoStrategy>();
        services.AddSingleton<TopRatedStrategy>();
        services.AddSingleton<NearestStrategy>();
        services.AddSingleton<IWasherSelectionStrategy>(provider => provider.GetRequiredService<FifoStrategy>());
        services.AddSingleton<IWasherSelectionStrategy>(provider => provider.GetRequiredService<TopRatedStrategy>());
        services.AddSingleton<IWasherSelectionStrategy>(provider => provider.GetRequiredService<NearestStrategy>());
        services.AddSingleton<IWasherQueue, InMemoryWasherQueue>();
        services.AddSingleton<QueueLifetime>();
        services.AddScoped<IWasherRepository, WasherRepository>();
        services.AddSingleton<IWasherImageStore, DiskWasherImageStore>();
        services.AddScoped<IWasherProfileService, WasherProfileService>();
        services.AddScoped<IWasherAdminService, WasherAdminService>();
        services.AddScoped<IWasherDispatchService, WasherDispatchService>();
        services.AddScoped<WasherQueueRebuild>();
        services.AddHostedService<QueueRebuildHostedService>();
        services.AddHostedService<LeaseReaper>();
        services.AddSingleton<IAuthorizationHandler, AdminOrServiceHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminOrServiceHandler.PolicyName, policy =>
                policy.Requirements.Add(new AdminOrServiceRequirement()));
        });
        return services;
    }
}
