using FluentValidation;
using GreenCarWash.CatalogService.Data;
using GreenCarWash.CatalogService.DTOs;
using GreenCarWash.CatalogService.Entities;
using GreenCarWash.CatalogService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CatalogService.Services;

public interface IPromoCatalog
{
    Task<IReadOnlyList<PromoResponseDto>> ListAsync(CancellationToken cancellationToken);

    Task<PromoResponseDto> GetAsync(int promoId, CancellationToken cancellationToken);

    Task<PromoResponseDto> CreateAsync(PromoWriteDto dto, CancellationToken cancellationToken);

    Task<PromoResponseDto> UpdateAsync(int promoId, PromoWriteDto dto, CancellationToken cancellationToken);

    Task<PromoResponseDto> SetActiveAsync(int promoId, bool isActive, CancellationToken cancellationToken);

    Task<PromoDiscountDto> ValidateAsync(PromoCheckDto dto, CancellationToken cancellationToken);

    Task<decimal> QuoteDiscountAsync(string? code, decimal subtotal, CancellationToken cancellationToken);

    Task<PromoRedeemResultDto> RedeemAsync(PromoCheckDto dto, CancellationToken cancellationToken);

    Task<PromoReleaseResultDto> ReleaseAsync(PromoReleaseDto dto, CancellationToken cancellationToken);
}

public sealed class PromoCatalog : IPromoCatalog
{
    private readonly IPromoRepository _promos;
    private readonly IValidator<PromoWriteDto> _writeValidator;
    private readonly IValidator<PromoCheckDto> _checkValidator;
    private readonly IValidator<PromoReleaseDto> _releaseValidator;
    private readonly IClock _clock;
    private readonly ILogger<PromoCatalog> _logger;

    public PromoCatalog(
        IPromoRepository promos,
        IValidator<PromoWriteDto> writeValidator,
        IValidator<PromoCheckDto> checkValidator,
        IValidator<PromoReleaseDto> releaseValidator,
        IClock clock,
        ILogger<PromoCatalog> logger)
    {
        _promos = promos;
        _writeValidator = writeValidator;
        _checkValidator = checkValidator;
        _releaseValidator = releaseValidator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PromoResponseDto>> ListAsync(CancellationToken cancellationToken)
    {
        var promos = await _promos.ListAsync(cancellationToken);
        return promos.Select(ToDto).ToArray();
    }

    public async Task<PromoResponseDto> GetAsync(int promoId, CancellationToken cancellationToken)
    {
        var promo = await _promos.FindAsync(promoId, cancellationToken)
            ?? throw new NotFoundException("Promo code", promoId);
        return ToDto(promo);
    }

    public async Task<PromoResponseDto> CreateAsync(PromoWriteDto dto, CancellationToken cancellationToken)
    {
        await _writeValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var code = PromoRules.Normalize(dto.Code);
        if (await _promos.CodeExistsAsync(code, null, cancellationToken))
            throw new ConflictException("A promo code with this code already exists.");

        var promo = new PromoCode
        {
            Code = code,
            DiscountType = ParseType(dto.DiscountType),
            DiscountValue = dto.DiscountValue,
            MinOrderAmount = dto.MinOrderAmount,
            MaxDiscount = dto.MaxDiscount,
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo,
            UsageLimit = dto.UsageLimit,
            UsedCount = 0,
            IsActive = dto.IsActive
        };
        await _promos.AddAsync(promo, cancellationToken);
        try
        {
            await _promos.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("A promo code with this code already exists.");
        }

        _logger.LogInformation("Created promo {PromoId}", promo.PromoId);
        return ToDto(promo);
    }

    public async Task<PromoResponseDto> UpdateAsync(int promoId, PromoWriteDto dto, CancellationToken cancellationToken)
    {
        await _writeValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var promo = await _promos.FindAsync(promoId, cancellationToken)
            ?? throw new NotFoundException("Promo code", promoId);
        var code = PromoRules.Normalize(dto.Code);
        if (await _promos.CodeExistsAsync(code, promoId, cancellationToken))
            throw new ConflictException("A promo code with this code already exists.");

        promo.Code = code;
        promo.DiscountType = ParseType(dto.DiscountType);
        promo.DiscountValue = dto.DiscountValue;
        promo.MinOrderAmount = dto.MinOrderAmount;
        promo.MaxDiscount = dto.MaxDiscount;
        promo.ValidFrom = dto.ValidFrom;
        promo.ValidTo = dto.ValidTo;
        promo.UsageLimit = dto.UsageLimit;
        promo.IsActive = dto.IsActive;

        try
        {
            await _promos.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("A promo code with this code already exists.");
        }

        return ToDto(promo);
    }

    public async Task<PromoResponseDto> SetActiveAsync(int promoId, bool isActive, CancellationToken cancellationToken)
    {
        var promo = await _promos.FindAsync(promoId, cancellationToken)
            ?? throw new NotFoundException("Promo code", promoId);
        promo.IsActive = isActive;
        await _promos.SaveAsync(cancellationToken);
        return ToDto(promo);
    }

    public async Task<PromoDiscountDto> ValidateAsync(PromoCheckDto dto, CancellationToken cancellationToken)
    {
        await _checkValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var code = PromoRules.Normalize(dto.Code);
        var discount = await DiscountOfAsync(code, dto.Subtotal, cancellationToken);
        return new PromoDiscountDto { Code = code, DiscountAmount = discount };
    }

    public async Task<decimal> QuoteDiscountAsync(string? code, decimal subtotal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            return 0m;

        return await DiscountOfAsync(PromoRules.Normalize(code), subtotal, cancellationToken);
    }

    public async Task<PromoRedeemResultDto> RedeemAsync(PromoCheckDto dto, CancellationToken cancellationToken)
    {
        await _checkValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var code = PromoRules.Normalize(dto.Code);
        var rows = await _promos.RedeemAsync(code, dto.Subtotal, _clock.UtcNow, cancellationToken);
        var promo = await _promos.FindByCodeAsync(code, cancellationToken);
        if (rows == 0 || promo is null)
            throw new ConflictException("Promo code cannot be redeemed.");

        _logger.LogInformation("Redeemed promo {PromoId}. UsedCount: {UsedCount}", promo.PromoId, promo.UsedCount);
        return new PromoRedeemResultDto
        {
            Code = promo.Code,
            DiscountAmount = PriceCalculator.PromoDiscount(
                promo.DiscountType,
                promo.DiscountValue,
                promo.MaxDiscount,
                dto.Subtotal),
            UsedCount = promo.UsedCount
        };
    }

    public async Task<PromoReleaseResultDto> ReleaseAsync(PromoReleaseDto dto, CancellationToken cancellationToken)
    {
        await _releaseValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var code = PromoRules.Normalize(dto.Code);
        var outcome = await _promos.ReleaseAsync(code, cancellationToken);
        if (outcome == PromoReleaseOutcome.Missing)
            throw new NotFoundException($"Promo code '{code}' was not found.");

        var promo = await _promos.FindByCodeAsync(code, cancellationToken)
            ?? throw new NotFoundException($"Promo code '{code}' was not found.");
        _logger.LogInformation("Released promo {PromoId}. UsedCount: {UsedCount}", promo.PromoId, promo.UsedCount);
        return new PromoReleaseResultDto { Code = promo.Code, UsedCount = promo.UsedCount };
    }

    private async Task<decimal> DiscountOfAsync(string code, decimal subtotal, CancellationToken cancellationToken)
    {
        var promo = await _promos.FindByCodeAsync(code, cancellationToken);
        if (promo is null || !PromoRules.IsRedeemable(promo, subtotal, _clock.UtcNow))
            throw Input.Invalid("PromoCode", "Promo code is not valid.");

        return PriceCalculator.PromoDiscount(promo.DiscountType, promo.DiscountValue, promo.MaxDiscount, subtotal);
    }

    private static DiscountType ParseType(string value) =>
        value.Equals("Flat", StringComparison.OrdinalIgnoreCase) ? DiscountType.Flat : DiscountType.Percent;

    private static PromoResponseDto ToDto(PromoCode promo) => new()
    {
        PromoId = promo.PromoId,
        Code = promo.Code,
        DiscountType = promo.DiscountType.ToString(),
        DiscountValue = promo.DiscountValue,
        MinOrderAmount = promo.MinOrderAmount,
        MaxDiscount = promo.MaxDiscount,
        ValidFrom = promo.ValidFrom,
        ValidTo = promo.ValidTo,
        UsageLimit = promo.UsageLimit,
        UsedCount = promo.UsedCount,
        IsActive = promo.IsActive
    };
}
