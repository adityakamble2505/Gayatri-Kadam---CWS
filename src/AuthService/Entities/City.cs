namespace GreenCarWash.AuthService.Entities;

public class City
{
    public int CityId { get; set; }

    public string CityName { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
