using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GreenCarWash.BuildingBlocks.Outbox;

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox<TDbContext, TPublisher>(
        this IServiceCollection services,
        params string[] excludedEventTypes)
        where TDbContext : DbContext
        where TPublisher : class, IOutboxEventPublisher
    {
        services.AddSingleton(new OutboxPublisherOptions<TDbContext>
        {
            ExcludedEventTypes = new HashSet<string>(excludedEventTypes, StringComparer.Ordinal)
        });
        services.AddScoped<IOutboxStore, EfOutboxStore<TDbContext>>();
        services.AddScoped<IOutboxEventPublisher, TPublisher>();
        services.AddHostedService<OutboxPublisherBackgroundService<TDbContext>>();
        return services;
    }
}

public sealed class OutboxPublisherOptions<TDbContext>
{
    public HashSet<string> ExcludedEventTypes { get; init; } = new(StringComparer.Ordinal);
}
