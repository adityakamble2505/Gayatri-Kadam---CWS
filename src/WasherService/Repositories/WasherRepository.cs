using GreenCarWash.WasherService.Data;
using GreenCarWash.WasherService.Entities;
using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.WasherService.Repositories;

public sealed class NearbyWasher
{
    public int WasherId { get; set; }

    public int UserId { get; set; }

    public int CityId { get; set; }

    public decimal AvgRating { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public double Miles { get; set; }
}

public interface IWasherRepository
{
    Task<WasherProfile?> FindByIdAsync(int washerId, CancellationToken cancellationToken);

    Task<WasherProfile?> FindByUserIdAsync(int userId, CancellationToken cancellationToken);

    Task<bool> ExistsForUserAsync(int userId, CancellationToken cancellationToken);

    Task AddAsync(WasherProfile washer, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);

    Task<(IReadOnlyList<WasherProfile> Items, int Total)> ListAsync(
        int? cityId,
        bool? isActive,
        bool? isAvailable,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WasherProfile>> ListAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<NearbyWasher>> NearbyAsync(int cityId, decimal latitude, decimal longitude, CancellationToken cancellationToken);

    Task<int?> MarkAssignedAsync(int washerId, DateTime utcNow, CancellationToken cancellationToken);

    Task<bool> ReleaseOfferAsync(int washerId, DateTime utcNow, CancellationToken cancellationToken);

    Task<bool> ConfirmAsync(int washerId, int bookingId, DateTime utcNow, CancellationToken cancellationToken);

    Task<bool> StartAsync(int washerId, DateTime utcNow, CancellationToken cancellationToken);

    Task<WasherProfile?> CompleteAsync(int washerId, DateTime utcNow, CancellationToken cancellationToken);

    Task ApplyRatingAsync(int washerId, int rating, DateTime utcNow, CancellationToken cancellationToken);

    Task<int> ResetStaleAssignmentsAsync(DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<WasherProfile>> ListQueueCandidatesAsync(CancellationToken cancellationToken);
}

public sealed class WasherRepository : IWasherRepository
{
    private readonly WasherDbContext _db;

    public WasherRepository(WasherDbContext db)
    {
        _db = db;
    }

    public Task<WasherProfile?> FindByIdAsync(int washerId, CancellationToken cancellationToken) =>
        _db.WasherProfiles.FirstOrDefaultAsync(washer => washer.WasherId == washerId, cancellationToken);

    public Task<WasherProfile?> FindByUserIdAsync(int userId, CancellationToken cancellationToken) =>
        _db.WasherProfiles.FirstOrDefaultAsync(washer => washer.UserId == userId, cancellationToken);

    public Task<bool> ExistsForUserAsync(int userId, CancellationToken cancellationToken) =>
        _db.WasherProfiles.AnyAsync(washer => washer.UserId == userId, cancellationToken);

    public async Task AddAsync(WasherProfile washer, CancellationToken cancellationToken) =>
        await _db.WasherProfiles.AddAsync(washer, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);

    public async Task<(IReadOnlyList<WasherProfile> Items, int Total)> ListAsync(
        int? cityId,
        bool? isActive,
        bool? isAvailable,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = Filtered(cityId, isActive, isAvailable);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(washer => washer.WasherId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<WasherProfile>> ListAllAsync(CancellationToken cancellationToken) =>
        await _db.WasherProfiles.AsNoTracking().OrderBy(washer => washer.WasherId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NearbyWasher>> NearbyAsync(
        int cityId,
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            return await _db.Database.SqlQuery<NearbyWasher>(
                $"""
                SELECT WasherId, UserId, CityId, AvgRating, Latitude, Longitude,
                       CAST(geography::Point(Latitude, Longitude, 4326).STDistance(geography::Point({latitude}, {longitude}, 4326)) / 1609.344 AS float) AS Miles
                FROM WasherProfiles
                WHERE CityId = {cityId}
                  AND IsActive = 1
                  AND IsAvailable = 1
                  AND IsQualified = 1
                  AND AssignmentStatus = 'Available'
                  AND Latitude IS NOT NULL
                  AND Longitude IS NOT NULL
                ORDER BY Miles, AvgRating DESC
                OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY
                """).ToListAsync(cancellationToken);
        }

        var rows = await _db.WasherProfiles.AsNoTracking()
            .Where(washer => washer.CityId == cityId
                && washer.IsActive
                && washer.IsAvailable
                && washer.IsQualified
                && washer.AssignmentStatus == AssignmentStatus.Available
                && washer.Latitude != null
                && washer.Longitude != null)
            .ToListAsync(cancellationToken);

        return rows
            .Select(washer => new NearbyWasher
            {
                WasherId = washer.WasherId,
                UserId = washer.UserId,
                CityId = washer.CityId,
                AvgRating = washer.AvgRating,
                Latitude = washer.Latitude!.Value,
                Longitude = washer.Longitude!.Value,
                Miles = Queue.Geo.Miles((double)latitude, (double)longitude, (double)washer.Latitude.Value, (double)washer.Longitude.Value)
            })
            .OrderBy(washer => washer.Miles)
            .ThenByDescending(washer => washer.AvgRating)
            .Take(10)
            .ToArray();
    }

    public async Task<int?> MarkAssignedAsync(int washerId, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            Detach(washerId);
            var rows = await ExecuteHotAsync(
                $"""
                UPDATE WasherProfiles
                SET AssignmentStatus = 'Assigned', AssignedAt = {utcNow}, UpdatedAt = {utcNow}
                WHERE WasherId = {washerId}
                  AND IsActive = 1
                  AND IsAvailable = 1
                  AND IsQualified = 1
                  AND AssignmentStatus = 'Available'
                """,
                cancellationToken);
            if (rows == 0)
                return null;
            return await _db.WasherProfiles.AsNoTracking()
                .Where(washer => washer.WasherId == washerId)
                .Select(washer => (int?)washer.UserId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var washer = await FindByIdAsync(washerId, cancellationToken);
        if (washer is null || !washer.IsActive || !washer.IsAvailable || !washer.IsQualified
            || washer.AssignmentStatus != AssignmentStatus.Available)
            return null;
        washer.AssignmentStatus = AssignmentStatus.Assigned;
        washer.AssignedAt = utcNow;
        washer.UpdatedAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return washer.UserId;
    }

    public async Task<bool> ReleaseOfferAsync(int washerId, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            Detach(washerId);
            var rows = await ExecuteHotAsync(
                $"""
                UPDATE WasherProfiles
                SET AssignmentStatus = 'Available', AssignedAt = NULL, UpdatedAt = {utcNow}
                WHERE WasherId = {washerId}
                  AND IsActive = 1
                  AND IsAvailable = 1
                  AND AssignmentStatus = 'Assigned'
                  AND CurrentBookingId IS NULL
                """,
                cancellationToken);
            return rows > 0;
        }

        var washer = await FindByIdAsync(washerId, cancellationToken);
        if (washer is null || !washer.IsActive || !washer.IsAvailable
            || washer.AssignmentStatus != AssignmentStatus.Assigned || washer.CurrentBookingId is not null)
            return false;
        washer.AssignmentStatus = AssignmentStatus.Available;
        washer.AssignedAt = null;
        washer.UpdatedAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ConfirmAsync(int washerId, int bookingId, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            Detach(washerId);
            var rows = await ExecuteHotAsync(
                $"""
                UPDATE WasherProfiles
                SET AssignmentStatus = 'Accepted', CurrentBookingId = {bookingId}, AssignedAt = {utcNow}, UpdatedAt = {utcNow}
                WHERE WasherId = {washerId}
                  AND IsActive = 1
                  AND AssignmentStatus = 'Assigned'
                """,
                cancellationToken);
            return rows > 0;
        }

        var washer = await FindByIdAsync(washerId, cancellationToken);
        if (washer is null || !washer.IsActive || washer.AssignmentStatus != AssignmentStatus.Assigned)
            return false;
        washer.AssignmentStatus = AssignmentStatus.Accepted;
        washer.CurrentBookingId = bookingId;
        washer.AssignedAt = utcNow;
        washer.UpdatedAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> StartAsync(int washerId, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            Detach(washerId);
            var rows = await ExecuteHotAsync(
                $"""
                UPDATE WasherProfiles
                SET AssignmentStatus = 'InProgress', UpdatedAt = {utcNow}
                WHERE WasherId = {washerId}
                  AND AssignmentStatus = 'Accepted'
                """,
                cancellationToken);
            return rows > 0;
        }

        var washer = await FindByIdAsync(washerId, cancellationToken);
        if (washer is null || washer.AssignmentStatus != AssignmentStatus.Accepted)
            return false;
        washer.AssignmentStatus = AssignmentStatus.InProgress;
        washer.UpdatedAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<WasherProfile?> CompleteAsync(int washerId, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            Detach(washerId);
            var rows = await ExecuteHotAsync(
                $"""
                UPDATE WasherProfiles
                SET AssignmentStatus = 'Available',
                    CurrentBookingId = NULL,
                    AssignedAt = NULL,
                    TotalCompletedWashes = TotalCompletedWashes + 1,
                    UpdatedAt = {utcNow}
                WHERE WasherId = {washerId}
                  AND AssignmentStatus = 'InProgress'
                """,
                cancellationToken);
            if (rows == 0)
                return null;
            return await _db.WasherProfiles.AsNoTracking()
                .FirstOrDefaultAsync(washer => washer.WasherId == washerId, cancellationToken);
        }

        var washer = await FindByIdAsync(washerId, cancellationToken);
        if (washer is null || washer.AssignmentStatus != AssignmentStatus.InProgress)
            return null;
        washer.AssignmentStatus = AssignmentStatus.Available;
        washer.CurrentBookingId = null;
        washer.AssignedAt = null;
        washer.TotalCompletedWashes += 1;
        washer.UpdatedAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return washer;
    }

    public async Task ApplyRatingAsync(int washerId, int rating, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            Detach(washerId);
            var rows = await ExecuteHotAsync(
                $"""
                UPDATE WasherProfiles
                SET AvgRating = ROUND((AvgRating * TotalReviews + {rating}) / (TotalReviews + 1), 2),
                    TotalReviews = TotalReviews + 1,
                    UpdatedAt = {utcNow}
                WHERE WasherId = {washerId}
                """,
                cancellationToken);
            if (rows == 0)
                throw new NotFoundException("Washer", washerId);
            return;
        }

        var washer = await FindByIdAsync(washerId, cancellationToken)
            ?? throw new NotFoundException("Washer", washerId);
        var reviews = washer.TotalReviews;
        washer.AvgRating = decimal.Round((washer.AvgRating * reviews + rating) / (reviews + 1), 2, MidpointRounding.AwayFromZero);
        washer.TotalReviews = reviews + 1;
        washer.UpdatedAt = utcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ResetStaleAssignmentsAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var stale = await _db.WasherProfiles
            .Where(washer => washer.AssignmentStatus == AssignmentStatus.Assigned && washer.CurrentBookingId == null)
            .ToListAsync(cancellationToken);
        foreach (var washer in stale)
        {
            washer.AssignmentStatus = AssignmentStatus.Available;
            washer.AssignedAt = null;
            washer.UpdatedAt = utcNow;
        }

        if (stale.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    public async Task<IReadOnlyList<WasherProfile>> ListQueueCandidatesAsync(CancellationToken cancellationToken) =>
        await _db.WasherProfiles.AsNoTracking()
            .Where(washer => washer.IsActive
                && washer.IsAvailable
                && washer.IsQualified
                && washer.AssignmentStatus == AssignmentStatus.Available)
            .ToListAsync(cancellationToken);

    private IQueryable<WasherProfile> Filtered(int? cityId, bool? isActive, bool? isAvailable)
    {
        var query = _db.WasherProfiles.AsNoTracking();
        if (cityId is int city)
            query = query.Where(washer => washer.CityId == city);
        if (isActive is bool active)
            query = query.Where(washer => washer.IsActive == active);
        if (isAvailable is bool available)
            query = query.Where(washer => washer.IsAvailable == available);
        return query;
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

    private void Detach(int washerId)
    {
        var local = _db.WasherProfiles.Local.FirstOrDefault(washer => washer.WasherId == washerId);
        if (local is not null)
            _db.Entry(local).State = EntityState.Detached;
    }
}
