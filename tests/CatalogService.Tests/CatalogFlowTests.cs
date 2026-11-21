using FluentAssertions;
using FluentValidation;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.CatalogService.Data;
using GreenCarWash.CatalogService.DTOs;
using GreenCarWash.CatalogService.Entities;
using GreenCarWash.CatalogService.Repositories;
using GreenCarWash.CatalogService.Services;
using GreenCarWash.CatalogService.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace GreenCarWash.CatalogService.Tests;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
}

public sealed class CatalogFlowTests
{
    private FakeClock _clock = null!;
    private CatalogDbContext _db = null!;
    private WashServiceCatalog _services = null!;
    private PromoCatalog _promos = null!;
    private QuoteService _quotes = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock();
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CatalogDbContext(options);
        var services = new WashServiceRepository(_db);
        var addOns = new AddOnRepository(_db);
        var promos = new PromoRepository(_db);
        _services = new WashServiceCatalog(services, new ServiceWriteDtoValidator(), _clock, NullLogger<WashServiceCatalog>.Instance);
        _promos = new PromoCatalog(
            promos,
            new PromoWriteDtoValidator(),
            new PromoCheckDtoValidator(),
            new PromoReleaseDtoValidator(),
            _clock,
            NullLogger<PromoCatalog>.Instance);
        _quotes = new QuoteService(services, addOns, _promos, new QuoteRequestDtoValidator(), NullLogger<QuoteService>.Instance);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task TC_08_ListServices_ReturnsActiveOnly()
    {
        var active = await ServiceAsync("Basic", 19.99m, 0);
        var inactive = await ServiceAsync("Deluxe", 69.99m, 0);
        inactive.IsActive = false;
        await _db.SaveChangesAsync();

        var listed = await _services.ListActiveAsync(CancellationToken.None);

        listed.Should().ContainSingle();
        listed[0].ServiceId.Should().Be(active.ServiceId);
        listed[0].ServiceName.Should().Be("Basic");
    }

    [Test]
    public async Task TC_09_CreateService_DuplicateName_IsConflict()
    {
        await _services.CreateAsync(Write("Basic", 19.99m), CancellationToken.None);

        var act = () => _services.CreateAsync(Write("basic", 10m), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await _db.Services.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task TC_10_ValidatePromo_DoesNotChangeUsedCount()
    {
        await PromoAsync("WELCOME10", DiscountType.Percent, 10m, maxDiscount: 15m);

        var result = await _promos.ValidateAsync(new PromoCheckDto { Code = "welcome10", Subtotal = 50m }, CancellationToken.None);

        result.DiscountAmount.Should().Be(5.00m);
        (await _db.PromoCodes.SingleAsync()).UsedCount.Should().Be(0);
    }

    [Test]
    public async Task Pricing_OneHundredPercentDiscount_TotalIsZero()
    {
        var service = await ServiceAsync("Free", 80m, 100);

        var quote = await _quotes.QuoteAsync(new QuoteRequestDto { ServiceId = service.ServiceId }, CancellationToken.None);

        quote.DiscountPct.Should().Be(100);
        quote.ServicePriceSnapshot.Should().Be(80m);
        quote.TotalAmount.Should().Be(0m);
    }

    [Test]
    public async Task Pricing_FlatPromoLargerThanSubtotal_TotalIsZero()
    {
        var service = await ServiceAsync("Basic", 20m, 0);
        await PromoAsync("BIG", DiscountType.Flat, 50m, minOrder: 0m);

        var quote = await _quotes.QuoteAsync(new QuoteRequestDto
        {
            ServiceId = service.ServiceId,
            PromoCode = "BIG"
        }, CancellationToken.None);

        quote.PromoDiscount.Should().Be(20m);
        quote.TotalAmount.Should().Be(0m);
        (await _db.PromoCodes.SingleAsync()).UsedCount.Should().Be(0);
    }

    [Test]
    public async Task Pricing_InactiveAddOn_IsRejected()
    {
        var service = await ServiceAsync("Basic", 19.99m, 0);
        var addOn = new AddOn { AddOnName = "Old Wax", Price = 5m, IsActive = false };
        _db.AddOns.Add(addOn);
        await _db.SaveChangesAsync();

        var act = () => _quotes.QuoteAsync(new QuoteRequestDto
        {
            ServiceId = service.ServiceId,
            AddOnIds = [addOn.AddOnId]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not active*");
    }

    [Test]
    public async Task Pricing_InactiveService_IsRejected()
    {
        var service = await ServiceAsync("Basic", 19.99m, 0, active: false);

        var act = () => _quotes.QuoteAsync(new QuoteRequestDto { ServiceId = service.ServiceId }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not active*");
    }

    [Test]
    public async Task Pricing_PercentPromo_UsesMaxDiscountCap()
    {
        var service = await ServiceAsync("Deluxe", 200m, 0);
        await PromoAsync("TWENTY", DiscountType.Percent, 20m, maxDiscount: 15m);

        var quote = await _quotes.QuoteAsync(new QuoteRequestDto
        {
            ServiceId = service.ServiceId,
            PromoCode = "TWENTY"
        }, CancellationToken.None);

        quote.PromoDiscount.Should().Be(15m);
        quote.TotalAmount.Should().Be(185m);
        (await _db.PromoCodes.SingleAsync()).UsedCount.Should().Be(0);
    }

    [Test]
    public async Task Pricing_FlatPromo_SubtractsFlatAmount()
    {
        var service = await ServiceAsync("Premium", 40m, 0);
        await PromoAsync("FLAT5", DiscountType.Flat, 5m, minOrder: 0m);

        var quote = await _quotes.QuoteAsync(new QuoteRequestDto
        {
            ServiceId = service.ServiceId,
            PromoCode = "FLAT5"
        }, CancellationToken.None);

        quote.PromoDiscount.Should().Be(5m);
        quote.TotalAmount.Should().Be(35m);
    }

    [Test]
    public async Task Pricing_RoundsHalfAwayFromZero()
    {
        var service = await ServiceAsync("Odd", 10.05m, 10);

        var quote = await _quotes.QuoteAsync(new QuoteRequestDto { ServiceId = service.ServiceId }, CancellationToken.None);

        quote.TotalAmount.Should().Be(9.05m);
    }

    [Test]
    public async Task Pricing_Welcome10OnBasicPlusVacuum_RoundsAndDoesNotRedeem()
    {
        var service = await ServiceAsync("Basic", 19.99m, 0, duration: 30, water: 15);
        var vacuum = new AddOn { AddOnName = "Interior Vacuum", Price = 14.99m, IsActive = true };
        _db.AddOns.Add(vacuum);
        await PromoAsync("WELCOME10", DiscountType.Percent, 10m, maxDiscount: 15m);
        await _db.SaveChangesAsync();

        var quote = await _quotes.QuoteAsync(new QuoteRequestDto
        {
            ServiceId = service.ServiceId,
            AddOnIds = [vacuum.AddOnId, vacuum.AddOnId],
            PromoCode = "WELCOME10"
        }, CancellationToken.None);

        quote.ServiceNameSnapshot.Should().Be("Basic");
        quote.AddOnsTotal.Should().Be(14.99m);
        quote.AddOns.Should().ContainSingle();
        quote.AddOns[0].Id.Should().Be(vacuum.AddOnId);
        quote.AddOns[0].Name.Should().Be("Interior Vacuum");
        quote.AddOns[0].Price.Should().Be(14.99m);
        quote.PromoDiscount.Should().Be(3.50m);
        quote.TotalAmount.Should().Be(31.48m);
        quote.DurationMin.Should().Be(30);
        quote.WaterSavedLiters.Should().Be(15);
        (await _db.PromoCodes.SingleAsync()).UsedCount.Should().Be(0);
    }

    [Test]
    public async Task Redeem_SecondTimeAtLimit_IsConflict()
    {
        await PromoAsync("ONCE", DiscountType.Flat, 5m, usageLimit: 1);
        var request = new PromoCheckDto { Code = "ONCE", Subtotal = 30m };

        var first = await _promos.RedeemAsync(request, CancellationToken.None);
        var act = () => _promos.RedeemAsync(request, CancellationToken.None);

        first.UsedCount.Should().Be(1);
        first.DiscountAmount.Should().Be(5m);
        await act.Should().ThrowAsync<ConflictException>();
        (await _db.PromoCodes.SingleAsync()).UsedCount.Should().Be(1);
    }

    [Test]
    public async Task Release_DoesNotGoBelowZero()
    {
        var promo = await PromoAsync("ONCE", DiscountType.Flat, 5m, usageLimit: 1);
        promo.UsedCount = 1;
        await _db.SaveChangesAsync();

        var first = await _promos.ReleaseAsync(new PromoReleaseDto { Code = "once" }, CancellationToken.None);
        var second = await _promos.ReleaseAsync(new PromoReleaseDto { Code = "ONCE" }, CancellationToken.None);

        first.UsedCount.Should().Be(0);
        second.UsedCount.Should().Be(0);
        (await _db.PromoCodes.SingleAsync()).UsedCount.Should().Be(0);
    }

    private async Task<WashService> ServiceAsync(
        string name,
        decimal price,
        int discount,
        bool active = true,
        int duration = 30,
        int water = 10)
    {
        var service = new WashService
        {
            ServiceName = name,
            Price = price,
            Discount = discount,
            Duration = duration,
            WaterSavedLiters = water,
            IsActive = active,
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow
        };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();
        return service;
    }

    private async Task<PromoCode> PromoAsync(
        string code,
        DiscountType type,
        decimal value,
        decimal? maxDiscount = null,
        decimal minOrder = 0m,
        int? usageLimit = null)
    {
        var promo = new PromoCode
        {
            Code = code,
            DiscountType = type,
            DiscountValue = value,
            MinOrderAmount = minOrder,
            MaxDiscount = maxDiscount,
            ValidFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidTo = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UsageLimit = usageLimit,
            UsedCount = 0,
            IsActive = true
        };
        _db.PromoCodes.Add(promo);
        await _db.SaveChangesAsync();
        return promo;
    }

    private static ServiceWriteDto Write(string name, decimal price) => new()
    {
        ServiceName = name,
        Price = price,
        Discount = 0,
        Duration = 30,
        WaterSavedLiters = 15
    };
}
