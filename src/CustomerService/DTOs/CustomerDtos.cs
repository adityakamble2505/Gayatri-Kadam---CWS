namespace GreenCarWash.CustomerService.DTOs;

public sealed class ProfileWriteDto
{
    public int CityId { get; set; }

    public string Address { get; set; } = string.Empty;

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }
}

public sealed class ProfileDto
{
    public int CustomerId { get; init; }

    public int UserId { get; init; }

    public int CityId { get; init; }

    public string Address { get; init; } = string.Empty;

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }
}

public sealed class VehicleWriteDto
{
    public string VehicleNumber { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string VehicleType { get; set; } = string.Empty;
}

public sealed class VehicleDto
{
    public int VehicleId { get; init; }

    public int CustomerId { get; init; }

    public string VehicleNumber { get; init; } = string.Empty;

    public string Brand { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string VehicleType { get; init; } = string.Empty;

    public string? ImageUrl { get; init; }

    public bool IsActive { get; init; }
}

public sealed class InternalVehicleDto
{
    public int VehicleId { get; init; }

    public int CustomerId { get; init; }

    public int UserId { get; init; }

    public string VehicleNumber { get; init; } = string.Empty;

    public string Brand { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string VehicleType { get; init; } = string.Empty;

    public string? ImageUrl { get; init; }

    public bool IsActive { get; init; }
}
