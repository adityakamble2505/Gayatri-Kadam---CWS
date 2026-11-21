using GreenCarWash.CustomerService.Data;
using GreenCarWash.CustomerService.Entities;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CustomerService.Repositories;

public interface IProfileRepository
{
    Task<CustomerProfile?> FindByUserIdAsync(int userId, CancellationToken cancellationToken);

    Task<CustomerProfile?> FindAsync(int customerId, CancellationToken cancellationToken);

    Task<bool> ExistsForUserAsync(int userId, CancellationToken cancellationToken);

    Task AddAsync(CustomerProfile profile, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed class ProfileRepository : IProfileRepository
{
    private readonly CustomerDbContext _db;

    public ProfileRepository(CustomerDbContext db)
    {
        _db = db;
    }

    public Task<CustomerProfile?> FindByUserIdAsync(int userId, CancellationToken cancellationToken) =>
        _db.CustomerProfiles.FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public Task<CustomerProfile?> FindAsync(int customerId, CancellationToken cancellationToken) =>
        _db.CustomerProfiles.FirstOrDefaultAsync(profile => profile.CustomerId == customerId, cancellationToken);

    public Task<bool> ExistsForUserAsync(int userId, CancellationToken cancellationToken) =>
        _db.CustomerProfiles.AnyAsync(profile => profile.UserId == userId, cancellationToken);

    public async Task AddAsync(CustomerProfile profile, CancellationToken cancellationToken) =>
        await _db.CustomerProfiles.AddAsync(profile, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
