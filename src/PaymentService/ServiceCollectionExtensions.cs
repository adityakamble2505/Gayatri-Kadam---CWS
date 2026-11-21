using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.PaymentService.Data;
using GreenCarWash.PaymentService.Repositories;
using GreenCarWash.PaymentService.Services;

namespace GreenCarWash.PaymentService;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.AddInternalHttpClient<IBookingDirectory, BookingDirectory>(Address(configuration, "Booking"));
        services.AddInternalHttpClient<ICustomerDirectory, CustomerDirectory>(Address(configuration, "Customer"));
        services.AddInternalHttpClient<IWasherDirectory, WasherDirectory>(Address(configuration, "Washer"));
        services.AddSingleton<IGatewayDelay, ConfiguredGatewayDelay>();
        services.AddSingleton<IPaymentGateway, SimulatedGatewayService>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentProcessor, PaymentProcessor>();
        services.AddHostedService<PendingReaper>();
        services.AddOutbox<PaymentDbContext, NotificationOutboxPublisher>();
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
