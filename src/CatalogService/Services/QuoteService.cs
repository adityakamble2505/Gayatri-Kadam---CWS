using FluentValidation;
using GreenCarWash.CatalogService.DTOs;
using GreenCarWash.CatalogService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;

namespace GreenCarWash.CatalogService.Services;

public interface IQuoteService
{
    Task<QuoteResponseDto> QuoteAsync(QuoteRequestDto dto, CancellationToken cancellationToken);
}

public sealed class QuoteService : IQuoteService
{
    private readonly IWashServiceRepository _services;
    private readonly IAddOnRepository _addOns;
    private readonly IPromoCatalog _promos;
    private readonly IValidator<QuoteRequestDto> _validator;
    private readonly ILogger<QuoteService> _logger;

    public QuoteService(
        IWashServiceRepository services,
        IAddOnRepository addOns,
        IPromoCatalog promos,
        IValidator<QuoteRequestDto> validator,
        ILogger<QuoteService> logger)
    {
        _services = services;
        _addOns = addOns;
        _promos = promos;
        _validator = validator;
        _logger = logger;
    }

    public async Task<QuoteResponseDto> QuoteAsync(QuoteRequestDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var service = await _services.FindAsync(dto.ServiceId, cancellationToken)
            ?? throw new NotFoundException("Service", dto.ServiceId);
        if (!service.IsActive)
            throw Input.Invalid("ServiceId", "Service is not active.");

        var ids = (dto.AddOnIds ?? []).Distinct().ToArray();
        var selected = await LoadAddOnsAsync(ids, cancellationToken);
        var addOnsTotal = selected.Sum(addOn => addOn.Price);
        var subtotal = service.Price * (1m - service.Discount / 100m) + addOnsTotal;
        var promoDiscount = await _promos.QuoteDiscountAsync(dto.PromoCode, subtotal, cancellationToken);
        var total = PriceCalculator.Total(service.Price, service.Discount, addOnsTotal, promoDiscount);

        _logger.LogInformation(
            "Quoted service {ServiceId}. Total: {TotalAmount}",
            service.ServiceId,
            total);

        return new QuoteResponseDto
        {
            ServiceNameSnapshot = service.ServiceName,
            ServicePriceSnapshot = service.Price,
            DiscountPct = service.Discount,
            AddOnsTotal = addOnsTotal,
            AddOns = selected,
            PromoDiscount = promoDiscount,
            TotalAmount = total,
            DurationMin = service.Duration,
            WaterSavedLiters = service.WaterSavedLiters
        };
    }

    private async Task<IReadOnlyList<AddOnSnapshotDto>> LoadAddOnsAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];

        var found = await _addOns.FindManyAsync(ids, cancellationToken);
        var byId = found.ToDictionary(addOn => addOn.AddOnId);
        var missing = ids.FirstOrDefault(id => !byId.ContainsKey(id));
        if (missing != 0)
            throw new NotFoundException("Add-on", missing);
        if (found.Any(addOn => !addOn.IsActive))
            throw Input.Invalid("AddOnIds", "Add-on is not active.");

        return ids.Select(id =>
        {
            var addOn = byId[id];
            return new AddOnSnapshotDto { Id = addOn.AddOnId, Name = addOn.AddOnName, Price = addOn.Price };
        }).ToArray();
    }
}
