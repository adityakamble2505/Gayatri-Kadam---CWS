using GreenCarWash.BuildingBlocks.Http;
using GreenCarWash.NotificationService.Repositories;
using GreenCarWash.NotificationService.Services;

namespace GreenCarWash.NotificationService;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddMemoryCache();
        services.AddSingleton<IEmailContentStore, EmailContentStore>();
        services.AddInternalHttpClient<IUserDirectory, UserDirectory>(Address(configuration, "Auth"));
        services.AddInternalHttpClient<IBookingNoticeDirectory, BookingNoticeDirectory>(Address(configuration, "Booking"));
        services.AddSingleton<ISmtpEmailService, MailKitEmailService>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationCenter, NotificationCenter>();
        services.AddHostedService<EmailSenderBackgroundService>();
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
