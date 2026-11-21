namespace GreenCarWash.CatalogService.DTOs;

public sealed class ServiceResponseDto
{
    public int ServiceId { get; init; }

    public string ServiceName { get; init; } = string.Empty;

    public string? Description { get; init; }

    public decimal Price { get; init; }

    public int Discount { get; init; }

    public int Duration { get; init; }

    public int WaterSavedLiters { get; init; }

    public bool IsActive { get; init; }
}

public sealed class ServiceWriteDto
{
    public string ServiceName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int Discount { get; set; }

    public int Duration { get; set; }

    public int WaterSavedLiters { get; set; }
}

public sealed class AddOnResponseDto
{
    public int AddOnId { get; init; }

    public string AddOnName { get; init; } = string.Empty;

    public decimal Price { get; init; }

    public bool IsActive { get; init; }
}

public sealed class AddOnWriteDto
{
    public string AddOnName { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

public sealed class PromoResponseDto
{
    public int PromoId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string DiscountType { get; init; } = string.Empty;

    public decimal DiscountValue { get; init; }

    public decimal MinOrderAmount { get; init; }

    public decimal? MaxDiscount { get; init; }

    public DateTime ValidFrom { get; init; }

    public DateTime ValidTo { get; init; }

    public int? UsageLimit { get; init; }

    public int UsedCount { get; init; }

    public bool IsActive { get; init; }
}

public sealed class PromoWriteDto
{
    public string Code { get; set; } = string.Empty;

    public string DiscountType { get; set; } = string.Empty;

    public decimal DiscountValue { get; set; }

    public decimal MinOrderAmount { get; set; }

    public decimal? MaxDiscount { get; set; }

    public DateTime ValidFrom { get; set; }

    public DateTime ValidTo { get; set; }

    public int? UsageLimit { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class PromoCheckDto
{
    public string Code { get; set; } = string.Empty;

    public decimal Subtotal { get; set; }
}

public sealed class PromoDiscountDto
{
    public string Code { get; init; } = string.Empty;

    public decimal DiscountAmount { get; init; }
}

public sealed class PromoRedeemResultDto
{
    public string Code { get; init; } = string.Empty;

    public decimal DiscountAmount { get; init; }

    public int UsedCount { get; init; }
}

public sealed class PromoReleaseDto
{
    public string Code { get; set; } = string.Empty;
}

public sealed class PromoReleaseResultDto
{
    public string Code { get; init; } = string.Empty;

    public int UsedCount { get; init; }
}

public sealed class QuoteRequestDto
{
    public int ServiceId { get; set; }

    public List<int> AddOnIds { get; set; } = [];

    public string? PromoCode { get; set; }
}

public sealed class AddOnSnapshotDto
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal Price { get; init; }
}

public sealed class QuoteResponseDto
{
    public string ServiceNameSnapshot { get; init; } = string.Empty;

    public decimal ServicePriceSnapshot { get; init; }

    public int DiscountPct { get; init; }

    public decimal AddOnsTotal { get; init; }

    public IReadOnlyList<AddOnSnapshotDto> AddOns { get; init; } = [];

    public decimal PromoDiscount { get; init; }

    public decimal TotalAmount { get; init; }

    public int DurationMin { get; init; }

    public int WaterSavedLiters { get; init; }
}
