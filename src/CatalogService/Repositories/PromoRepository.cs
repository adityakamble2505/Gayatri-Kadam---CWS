using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.CatalogService.Data;
using GreenCarWash.CatalogService.Entities;
using GreenCarWash.CatalogService.Services;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CatalogService.Repositories;

public enum PromoReleaseOutcome
{
    Missing,
    Unchanged,
    Decremented
}

public interface IPromoRepository
{
    Task<IReadOnlyList<PromoCode>> ListAsync(CancellationToken cancellationToken);

    Task<PromoCode?> FindAsync(int promoId, CancellationToken cancellationToken);

    Task<PromoCode?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, int? exceptId, CancellationToken cancellationToken);

    Task AddAsync(PromoCode promo, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);

    Task<int> RedeemAsync(string code, decimal subtotal, DateTime utcNow, CancellationToken cancellationToken);

    Task<PromoReleaseOutcome> ReleaseAsync(string code, CancellationToken cancellationToken);
}

public sealed class PromoRepository : IPromoRepository
{
    private readonly CatalogDbContext _db;

    public PromoRepository(CatalogDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PromoCode>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.PromoCodes
            .AsNoTracking()
            .OrderBy(promo => promo.PromoId)
            .ToListAsync(cancellationToken);
    }

    public Task<PromoCode?> FindAsync(int promoId, CancellationToken cancellationToken) =>
        _db.PromoCodes.FirstOrDefaultAsync(promo => promo.PromoId == promoId, cancellationToken);

    public Task<PromoCode?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        _db.PromoCodes.FirstOrDefaultAsync(promo => promo.Code == code, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, int? exceptId, CancellationToken cancellationToken)
    {
        if (exceptId is int id)
            return _db.PromoCodes.AnyAsync(promo => promo.PromoId != id && promo.Code == code, cancellationToken);

        return _db.PromoCodes.AnyAsync(promo => promo.Code == code, cancellationToken);
    }

    public async Task AddAsync(PromoCode promo, CancellationToken cancellationToken) =>
        await _db.PromoCodes.AddAsync(promo, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);

    public async Task<int> RedeemAsync(string code, decimal subtotal, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            DetachPromos();
            return await ExecuteHotAsync(
                $"""
                UPDATE PromoCodes
                SET UsedCount = UsedCount + 1
                WHERE Code = {code}
                  AND IsActive = 1
                  AND SYSUTCDATETIME() BETWEEN ValidFrom AND ValidTo
                  AND {subtotal} >= MinOrderAmount
                  AND (UsageLimit IS NULL OR UsedCount < UsageLimit)
                """,
                cancellationToken);
        }

        var promo = await _db.PromoCodes.FirstOrDefaultAsync(row => row.Code == code, cancellationToken);
        if (promo is null || !PromoRules.IsRedeemable(promo, subtotal, utcNow))
            return 0;

        promo.UsedCount += 1;
        await _db.SaveChangesAsync(cancellationToken);
        return 1;
    }

    public async Task<PromoReleaseOutcome> ReleaseAsync(string code, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            DetachPromos();
            var rows = await ExecuteHotAsync(
                $"""
                UPDATE PromoCodes
                SET UsedCount = UsedCount - 1
                WHERE Code = {code}
                  AND UsedCount > 0
                """,
                cancellationToken);
            if (rows > 0)
                return PromoReleaseOutcome.Decremented;

            var exists = await _db.PromoCodes.AsNoTracking().AnyAsync(promo => promo.Code == code, cancellationToken);
            return exists ? PromoReleaseOutcome.Unchanged : PromoReleaseOutcome.Missing;
        }

        var promo = await _db.PromoCodes.FirstOrDefaultAsync(row => row.Code == code, cancellationToken);
        if (promo is null)
            return PromoReleaseOutcome.Missing;
        if (promo.UsedCount <= 0)
            return PromoReleaseOutcome.Unchanged;

        promo.UsedCount -= 1;
        await _db.SaveChangesAsync(cancellationToken);
        return PromoReleaseOutcome.Decremented;
    }

    private async Task<int> ExecuteHotAsync(FormattableString sql, CancellationToken cancellationToken)
    {
        var rows = 0;
        await SqlServerErrors.ExecuteOnceAsync(async () =>
        {
            rows = await _db.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
        });
        return rows;
    }

    private void DetachPromos()
    {
        foreach (var entry in _db.ChangeTracker.Entries<PromoCode>().ToList())
            entry.State = EntityState.Detached;
    }
}
