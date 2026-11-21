namespace GreenCarWash.CustomerService.Entities;

public sealed class CustomerProfile
{
    public int CustomerId { get; set; }

    public int UserId { get; set; }

    public int CityId { get; set; }

    public string Address { get; set; } = string.Empty;

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}
