using GreenCarWash.AuthService.Data;
using GreenCarWash.AuthService.Entities;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.AuthService.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);
}

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AuthDbContext _db;

    public RefreshTokenRepository(AuthDbContext db)
    {
        _db = db;
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        _db.RefreshTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken) =>
        await _db.RefreshTokens.AddAsync(token, cancellationToken);
}
