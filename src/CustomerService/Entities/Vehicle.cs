namespace GreenCarWash.CustomerService.Entities;

public sealed class Vehicle
{
    public int VehicleId { get; set; }

    public int CustomerId { get; set; }

    public string VehicleNumber { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public VehicleType VehicleType { get; set; }

    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public CustomerProfile? Customer { get; set; }
}
