using GreenCarWash.BookingService.Data;
using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BookingService.Services;
using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.BuildingBlocks.Outbox;

namespace GreenCarWash.BookingService;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBookingApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<DispatchOptions>(configuration.GetSection(DispatchOptions.SectionName));
        services.AddInternalHttpClient<ICustomerDirectory, CustomerDirectory>(Address(configuration, "Customer"));
        services.AddInternalHttpClient<IWasherDirectory, WasherDirectory>(Address(configuration, "Washer"));
        services.AddInternalHttpClient<ICatalogPricing, CatalogPricing>(Address(configuration, "Catalog"));
        services.AddInternalHttpClient<IActiveCityCatalog, AuthActiveCityCatalog>(Address(configuration, "Auth"));
        services.AddInternalHttpClient<IPaymentCompensation, PaymentCompensation>(Address(configuration, "Payment"));
        services.AddInternalHttpClient<IUserDirectory, AuthUserDirectory>(Address(configuration, "Auth"));
        services.AddScoped<IBookingOutbox, BookingOutbox>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IBookingInsightsRepository, BookingInsightsRepository>();
        services.AddScoped<IAdminReportRepository, AdminReportRepository>();
        services.AddScoped<DisplayNameLookup>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<ILeaderboardService, LeaderboardService>();
        services.AddScoped<IAdminOrderService, AdminOrderService>();
        services.AddScoped<IAdminReportService, AdminReportService>();
        services.AddOutbox<BookingDbContext, NotificationOutboxPublisher>("RatingSyncPending", "RefundPending");
        services.AddScoped<RatingSyncProcessor>();
        services.AddScoped<RefundRetryProcessor>();
        services.AddHostedService<RatingSyncRetryWorker>();
        services.AddHostedService<RefundRetryWorker>();
        services.AddSingleton<IBookingImageStore, DiskBookingImageStore>();
        services.AddSingleton<DispatchCoordinator>();
        services.AddSingleton<IDispatchQueue>(provider => provider.GetRequiredService<DispatchCoordinator>());
        services.AddSingleton<DispatchMaintenance>();
        services.AddHostedService(provider => provider.GetRequiredService<DispatchCoordinator>());
        services.AddHostedService<ScheduledBookingWorker>();
        services.AddHostedService<DispatchRecoveryService>();
        services.AddScoped<IBookingWorkflow, BookingWorkflow>();
        return services;
    }

    private static Uri Address(IConfiguration configuration, string key)
    {
        var address = configuration[$"ServiceUrls:{key}"]
            ?? throw new InvalidOperationException($"ServiceUrls:{key} is not configured.");
        if (!address.EndsWith('/'))
            address += "/";
        return new Uri(address);
    }
}
