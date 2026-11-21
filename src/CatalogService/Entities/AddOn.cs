namespace GreenCarWash.CatalogService.Entities;

public sealed class AddOn
{
    public int AddOnId { get; set; }

    public string AddOnName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;
}
