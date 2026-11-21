using GreenCarWash.CatalogService.Entities;

namespace GreenCarWash.CatalogService.Services;

public static class PriceCalculator
{
    public static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal PromoDiscount(
        DiscountType discountType,
        decimal discountValue,
        decimal? maxDiscount,
        decimal subtotal)
    {
        if (subtotal <= 0 || discountValue <= 0)
            return 0m;

        var raw = discountType == DiscountType.Percent
            ? subtotal * discountValue / 100m
            : discountValue;

        if (discountType == DiscountType.Percent && maxDiscount is decimal cap)
            raw = Math.Min(raw, cap);

        raw = Math.Min(raw, subtotal);
        var rounded = RoundMoney(raw);
        var payable = RoundMoney(subtotal);
        return rounded > payable ? payable : rounded;
    }

    public static decimal Total(decimal price, int discountPct, decimal addOnsTotal, decimal promoDiscount)
    {
        var amount = price * (1m - discountPct / 100m) + addOnsTotal - promoDiscount;
        if (amount < 0)
            amount = 0;
        return RoundMoney(amount);
    }
}

public static class PromoRules
{
    public static bool IsRedeemable(PromoCode promo, decimal subtotal, DateTime utcNow)
    {
        if (!promo.IsActive)
            return false;
        if (utcNow < promo.ValidFrom || utcNow > promo.ValidTo)
            return false;
        if (subtotal < promo.MinOrderAmount)
            return false;
        if (promo.UsageLimit is int limit && promo.UsedCount >= limit)
            return false;
        return true;
    }

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}
