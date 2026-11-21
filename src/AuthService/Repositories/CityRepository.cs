using GreenCarWash.AuthService.Data;
using GreenCarWash.AuthService.Entities;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.AuthService.Repositories;

public interface ICityRepository
{
    Task<IReadOnlyList<City>> ListActiveAsync(CancellationToken cancellationToken);

    Task<City?> FindByIdAsync(int cityId, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string cityName, string state, int? excludingCityId, CancellationToken cancellationToken);

    Task<bool> AnyAsync(CancellationToken cancellationToken);

    Task AddAsync(City city, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class CityRepository : ICityRepository
{
    private readonly AuthDbContext _db;

    public CityRepository(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<City>> ListActiveAsync(CancellationToken cancellationToken) =>
        await _db.Cities
            .AsNoTracking()
            .Where(city => city.IsActive)
            .OrderBy(city => city.CityName)
            .ThenBy(city => city.State)
            .ToListAsync(cancellationToken);

    public Task<City?> FindByIdAsync(int cityId, CancellationToken cancellationToken) =>
        _db.Cities.FirstOrDefaultAsync(city => city.CityId == cityId, cancellationToken);

    public Task<bool> NameExistsAsync(string cityName, string state, int? excludingCityId, CancellationToken cancellationToken)
    {
        var name = cityName.Trim().ToLowerInvariant();
        var region = state.Trim().ToLowerInvariant();
        return _db.Cities.AnyAsync(city =>
            city.CityName.ToLower() == name
            && city.State.ToLower() == region
            && (excludingCityId == null || city.CityId != excludingCityId), cancellationToken);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken) =>
        _db.Cities.AnyAsync(cancellationToken);

    public async Task AddAsync(City city, CancellationToken cancellationToken) =>
        await _db.Cities.AddAsync(city, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
