using GreenCarWash.CatalogService.Data;
using GreenCarWash.CatalogService.Entities;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CatalogService.Repositories;

public interface IAddOnRepository
{
    Task<IReadOnlyList<AddOn>> ListActiveAsync(CancellationToken cancellationToken);

    Task<AddOn?> FindAsync(int addOnId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AddOn>> FindManyAsync(IReadOnlyCollection<int> addOnIds, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, int? exceptId, CancellationToken cancellationToken);

    Task AddAsync(AddOn addOn, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed class AddOnRepository : IAddOnRepository
{
    private readonly CatalogDbContext _db;

    public AddOnRepository(CatalogDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AddOn>> ListActiveAsync(CancellationToken cancellationToken)
    {
        return await _db.AddOns
            .AsNoTracking()
            .Where(addOn => addOn.IsActive)
            .OrderBy(addOn => addOn.AddOnId)
            .ToListAsync(cancellationToken);
    }

    public Task<AddOn?> FindAsync(int addOnId, CancellationToken cancellationToken) =>
        _db.AddOns.FirstOrDefaultAsync(addOn => addOn.AddOnId == addOnId, cancellationToken);

    public async Task<IReadOnlyList<AddOn>> FindManyAsync(IReadOnlyCollection<int> addOnIds, CancellationToken cancellationToken)
    {
        return await _db.AddOns
            .Where(addOn => addOnIds.Contains(addOn.AddOnId))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, int? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();
        if (exceptId is int id)
        {
            return _db.AddOns.AnyAsync(
                addOn => addOn.AddOnId != id && addOn.AddOnName.ToLower() == lowered,
                cancellationToken);
        }

        return _db.AddOns.AnyAsync(
            addOn => addOn.AddOnName.ToLower() == lowered,
            cancellationToken);
    }

    public async Task AddAsync(AddOn addOn, CancellationToken cancellationToken) =>
        await _db.AddOns.AddAsync(addOn, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
