namespace GreenCarWash.CatalogService.Entities;

public sealed class PromoCode
{
    public int PromoId { get; set; }

    public string Code { get; set; } = string.Empty;

    public DiscountType DiscountType { get; set; }

    public decimal DiscountValue { get; set; }

    public decimal MinOrderAmount { get; set; }

    public decimal? MaxDiscount { get; set; }

    public DateTime ValidFrom { get; set; }

    public DateTime ValidTo { get; set; }

    public int? UsageLimit { get; set; }

    public int UsedCount { get; set; }

    public bool IsActive { get; set; } = true;
}
