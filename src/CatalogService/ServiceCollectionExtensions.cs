using GreenCarWash.CatalogService.Repositories;
using GreenCarWash.CatalogService.Services;

namespace GreenCarWash.CatalogService;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogApplication(this IServiceCollection services)
    {
        services.AddScoped<IWashServiceRepository, WashServiceRepository>();
        services.AddScoped<IAddOnRepository, AddOnRepository>();
        services.AddScoped<IPromoRepository, PromoRepository>();
        services.AddScoped<IWashServiceCatalog, WashServiceCatalog>();
        services.AddScoped<IAddOnCatalog, AddOnCatalog>();
        services.AddScoped<IPromoCatalog, PromoCatalog>();
        services.AddScoped<IQuoteService, QuoteService>();
        return services;
    }
}
