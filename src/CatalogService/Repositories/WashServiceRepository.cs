using GreenCarWash.CatalogService.Data;
using GreenCarWash.CatalogService.Entities;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CatalogService.Repositories;

public interface IWashServiceRepository
{
    Task<IReadOnlyList<WashService>> ListActiveAsync(CancellationToken cancellationToken);

    Task<WashService?> FindAsync(int serviceId, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, int? exceptId, CancellationToken cancellationToken);

    Task AddAsync(WashService service, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed class WashServiceRepository : IWashServiceRepository
{
    private readonly CatalogDbContext _db;

    public WashServiceRepository(CatalogDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WashService>> ListActiveAsync(CancellationToken cancellationToken)
    {
        return await _db.Services
            .AsNoTracking()
            .Where(service => service.IsActive)
            .OrderBy(service => service.ServiceId)
            .ToListAsync(cancellationToken);
    }

    public Task<WashService?> FindAsync(int serviceId, CancellationToken cancellationToken) =>
        _db.Services.FirstOrDefaultAsync(service => service.ServiceId == serviceId, cancellationToken);

    public Task<bool> NameExistsAsync(string name, int? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();
        if (exceptId is int id)
        {
            return _db.Services.AnyAsync(
                service => service.ServiceId != id && service.ServiceName.ToLower() == lowered,
                cancellationToken);
        }

        return _db.Services.AnyAsync(
            service => service.ServiceName.ToLower() == lowered,
            cancellationToken);
    }

    public async Task AddAsync(WashService service, CancellationToken cancellationToken) =>
        await _db.Services.AddAsync(service, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
