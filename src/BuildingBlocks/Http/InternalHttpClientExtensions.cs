using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace GreenCarWash.BuildingBlocks.Http;

public static class InternalHttpClientExtensions
{
    public static IHttpClientBuilder AddInternalHttpClient<TClient>(this IServiceCollection services, Uri baseAddress)
        where TClient : class
    {
        services.TryAddTransient<InternalServiceHeadersHandler>();
        return services.AddHttpClient<TClient>(client => client.BaseAddress = baseAddress)
            .ApplyInternalResilience();
    }

    public static IHttpClientBuilder AddInternalHttpClient<TClient, TImplementation>(
        this IServiceCollection services,
        Uri baseAddress)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.TryAddTransient<InternalServiceHeadersHandler>();
        return services.AddHttpClient<TClient, TImplementation>(client => client.BaseAddress = baseAddress)
            .ApplyInternalResilience();
    }

    public static IServiceCollection AddInternalServiceClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddTransient<InternalServiceHeadersHandler>();

        var section = configuration.GetSection("ServiceUrls");
        if (!section.Exists())
            throw new InvalidOperationException("ServiceUrls configuration section is missing.");

        foreach (var child in section.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Value))
                throw new InvalidOperationException($"ServiceUrls:{child.Key} is not configured.");

            var address = child.Value.EndsWith('/') ? child.Value : child.Value + "/";
            services.AddHttpClient(child.Key, client => client.BaseAddress = new Uri(address))
                .ApplyInternalResilience();
        }

        return services;
    }

    private static IHttpClientBuilder ApplyInternalResilience(this IHttpClientBuilder builder)
    {
        return builder
            .AddHttpMessageHandler<InternalServiceHeadersHandler>()
            .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10)))
            .AddTransientHttpErrorPolicy(policy => policy
                .Or<TimeoutRejectedException>()
                .CircuitBreakerAsync(handledEventsAllowedBeforeBreaking: 5, durationOfBreak: TimeSpan.FromSeconds(30)))
            .AddTransientHttpErrorPolicy(policy => policy
                .Or<TimeoutRejectedException>()
                .WaitAndRetryAsync(retryCount: 3, sleepDurationProvider: RetryDelay));
    }

    private static TimeSpan RetryDelay(int attempt)
    {
        var exponential = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 200));
        return exponential + jitter;
    }
}
