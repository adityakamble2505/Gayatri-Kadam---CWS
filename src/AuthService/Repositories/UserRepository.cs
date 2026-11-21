using GreenCarWash.AuthService.Data;
using GreenCarWash.AuthService.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GreenCarWash.AuthService.Repositories;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(int userId, CancellationToken cancellationToken);

    Task<bool> AnyAdminAsync(CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);

    Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class UserRepository : IUserRepository
{
    private readonly AuthDbContext _db;

    public UserRepository(AuthDbContext db)
    {
        _db = db;
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return _db.Users.FirstOrDefaultAsync(user => user.Email.ToLower() == normalized, cancellationToken);
    }

    public Task<User?> FindByIdAsync(int userId, CancellationToken cancellationToken) =>
        _db.Users.FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    public Task<bool> AnyAdminAsync(CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(user => user.Role == UserRole.Admin, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken) =>
        await _db.Users.AddAsync(user, cancellationToken);

    public async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return null;

        return await _db.Database.BeginTransactionAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
