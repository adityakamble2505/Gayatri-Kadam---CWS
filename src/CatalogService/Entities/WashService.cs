namespace GreenCarWash.CatalogService.Entities;

public sealed class WashService
{
    public int ServiceId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int Discount { get; set; }

    public int Duration { get; set; }

    public int WaterSavedLiters { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
