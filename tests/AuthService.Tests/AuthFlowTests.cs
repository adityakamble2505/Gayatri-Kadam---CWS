using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using FluentValidation;
using GreenCarWash.AuthService.Data;
using GreenCarWash.AuthService.DTOs;
using GreenCarWash.AuthService.Entities;
using GreenCarWash.AuthService.Repositories;
using GreenCarWash.AuthService.Services;
using GreenCarWash.AuthService.Validators;
using AuthApp = GreenCarWash.AuthService.Services.AuthenticationService;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace GreenCarWash.AuthService.Tests;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
}

public sealed class AuthFlowTests
{
    private const string Password = "Customer@123";
    private const string WrongPassword = "WrongPass@1";

    private FakeClock _clock = null!;
    private AuthDbContext _db = null!;
    private AuthApp _auth = null!;
    private CityService _cities = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock();
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AuthDbContext(options);
        var users = new UserRepository(_db);
        var tokens = new RefreshTokenRepository(_db);
        var passwords = new BcryptPasswordHasher();
        var tokenService = new TokenService(TestConfiguration(), _clock);
        _auth = new AuthApp(
            users,
            tokens,
            passwords,
            tokenService,
            new RegisterDtoValidator(),
            new LoginDtoValidator(),
            _clock,
            NullLogger<AuthenticationService>.Instance);
        _cities = new CityService(new CityRepository(_db), new CityWriteDtoValidator(), NullLogger<CityService>.Instance);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task TC_01_Register_Customer_Succeeds()
    {
        var user = await _auth.RegisterAsync(Register("ada@example.com", "Customer"), CancellationToken.None);

        user.Role.Should().Be("Customer");
        user.Email.Should().Be("ada@example.com");
        var stored = await _db.Users.SingleAsync();
        stored.PasswordHash.Should().NotBe(Password);
        stored.PasswordHash.Should().StartWith("$2");
    }

    [Test]
    public async Task TC_02_Register_Washer_Succeeds()
    {
        var user = await _auth.RegisterAsync(Register("wash@example.com", "Washer"), CancellationToken.None);

        user.Role.Should().Be("Washer");
        (await _db.Users.SingleAsync()).Role.Should().Be(UserRole.Washer);
    }

    [Test]
    public async Task TC_03_Register_Admin_IsRejected()
    {
        var act = () => _auth.RegisterAsync(Register("admin2@example.com", "Admin"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Admin cannot be self-registered*");
        (await _db.Users.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task TC_04_Login_ValidCredentials_ReturnsTokensWithClaims()
    {
        await _auth.RegisterAsync(Register("ada@example.com", "Customer"), CancellationToken.None);

        var response = await _auth.LoginAsync(new LoginDto
        {
            Email = "Ada@Example.com",
            Password = Password
        }, CancellationToken.None);

        response.RefreshToken.Should().NotBeNullOrWhiteSpace();
        response.AccessTokenExpiresAt.Should().Be(_clock.UtcNow.AddMinutes(15));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        jwt.Claims.Should().Contain(claim => claim.Type == TokenService.UserIdClaim && claim.Value == response.User.UserId.ToString());
        jwt.Claims.Should().Contain(claim => claim.Type == TokenService.RoleClaim && claim.Value == "Customer");
        jwt.Claims.Should().Contain(claim => claim.Type == TokenService.EmailClaim && claim.Value == "ada@example.com");

        var stored = await _db.RefreshTokens.SingleAsync();
        stored.TokenHash.Should().Be(TokenHasher.Sha256(response.RefreshToken));
        stored.TokenHash.Should().NotBe(response.RefreshToken);
        stored.RevokedAt.Should().BeNull();
    }

    [Test]
    public async Task TC_25_GetCities_ReturnsActiveOnly()
    {
        _db.Cities.AddRange(
            new City { CityName = "New York", State = "NY", IsActive = true },
            new City { CityName = "Phoenix", State = "AZ", IsActive = false });
        await _db.SaveChangesAsync();

        var cities = await _cities.ListActiveAsync(CancellationToken.None);

        cities.Should().ContainSingle();
        cities[0].CityName.Should().Be("New York");
        cities[0].IsActive.Should().BeTrue();
    }

    [Test]
    public async Task Lockout_AfterFiveFailures_SetsLockoutEndUtc()
    {
        await _auth.RegisterAsync(Register("ada@example.com", "Customer"), CancellationToken.None);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var act = () => _auth.LoginAsync(new LoginDto { Email = "ada@example.com", Password = WrongPassword }, CancellationToken.None);
            await act.Should().ThrowAsync<UnauthorizedException>().WithMessage(AuthApp.InvalidCredentialsMessage);
        }

        var user = await _db.Users.SingleAsync();
        user.FailedLoginCount.Should().Be(5);
        user.LockoutEndUtc.Should().Be(_clock.UtcNow.AddMinutes(15));
    }

    [Test]
    public async Task Lockout_RejectsCorrectPassword_AndSuccessResetsCountAfterWindow()
    {
        await _auth.RegisterAsync(Register("ada@example.com", "Customer"), CancellationToken.None);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = () => _auth.LoginAsync(new LoginDto { Email = "ada@example.com", Password = WrongPassword }, CancellationToken.None);
            await failed.Should().ThrowAsync<UnauthorizedException>();
        }

        var duringLockout = () => _auth.LoginAsync(new LoginDto { Email = "ada@example.com", Password = Password }, CancellationToken.None);
        await duringLockout.Should().ThrowAsync<UnauthorizedException>().WithMessage(AuthApp.InvalidCredentialsMessage);
        (await _db.Users.SingleAsync()).FailedLoginCount.Should().Be(5);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(15);
        var response = await _auth.LoginAsync(new LoginDto { Email = "ada@example.com", Password = Password }, CancellationToken.None);

        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        var user = await _db.Users.SingleAsync();
        user.FailedLoginCount.Should().Be(0);
        user.LockoutEndUtc.Should().BeNull();
        user.LastLoginUtc.Should().Be(_clock.UtcNow);
    }

    [Test]
    public async Task Refresh_RotatesToken_AndRevokesThePreviousOne()
    {
        await _auth.RegisterAsync(Register("ada@example.com", "Customer"), CancellationToken.None);
        var login = await _auth.LoginAsync(new LoginDto { Email = "ada@example.com", Password = Password }, CancellationToken.None);

        var refreshed = await _auth.RefreshAsync(new RefreshRequestDto { RefreshToken = login.RefreshToken }, CancellationToken.None);

        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);
        var tokens = await _db.RefreshTokens.OrderBy(token => token.TokenId).ToListAsync();
        tokens.Should().HaveCount(2);
        tokens[0].TokenHash.Should().Be(TokenHasher.Sha256(login.RefreshToken));
        tokens[0].RevokedAt.Should().Be(_clock.UtcNow);
        tokens[1].TokenHash.Should().Be(TokenHasher.Sha256(refreshed.RefreshToken));
        tokens[1].RevokedAt.Should().BeNull();

        var replay = () => _auth.RefreshAsync(new RefreshRequestDto { RefreshToken = login.RefreshToken }, CancellationToken.None);
        await replay.Should().ThrowAsync<UnauthorizedException>().WithMessage(AuthApp.InvalidRefreshTokenMessage);

        var next = await _auth.RefreshAsync(new RefreshRequestDto { RefreshToken = refreshed.RefreshToken }, CancellationToken.None);
        next.RefreshToken.Should().NotBe(refreshed.RefreshToken);
    }

    [Test]
    public async Task Register_DuplicateEmail_IsConflict()
    {
        await _auth.RegisterAsync(Register("ada@example.com", "Customer"), CancellationToken.None);

        var act = () => _auth.RegisterAsync(Register("Ada@Example.com", "Washer"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("An account with this email already exists.");
        (await _db.Users.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task Seeder_ReplacesPlaceholderHash_AndSeedsCitiesWhenEmpty()
    {
        _db.Users.Add(new User
        {
            FullName = "GreenCarWash Admin",
            Email = "admin@greencarwash.example",
            PasswordHash = "$2a$11$PLACEHOLDER_BCRYPT_HASH_REPLACE_BEFORE_ANY_LOGIN_000000",
            PhoneNumber = "+12125550100",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow
        });
        await _db.SaveChangesAsync();

        var passwords = new BcryptPasswordHasher();
        await AuthSeeder.SeedAsync(_db, _clock, passwords, TestConfiguration(), NullLogger.Instance, CancellationToken.None);

        var admin = await _db.Users.SingleAsync();
        admin.PasswordHash.Should().NotContain("PLACEHOLDER");
        passwords.Verify("Admin@1234", admin.PasswordHash).Should().BeTrue();
        (await _db.Cities.CountAsync()).Should().Be(5);

        await AuthSeeder.SeedAsync(_db, _clock, passwords, TestConfiguration(), NullLogger.Instance, CancellationToken.None);
        (await _db.Cities.CountAsync()).Should().Be(5);
        (await _db.Users.CountAsync(user => user.Role == UserRole.Admin)).Should().Be(1);
    }

    private static RegisterDto Register(string email, string role) => new()
    {
        FullName = "Ada Lovelace",
        Email = email,
        Password = Password,
        PhoneNumber = "+12125550199",
        Role = role
    };

    private static IConfiguration TestConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "GreenCarWash",
                ["Jwt:Audience"] = "GreenCarWash",
                ["Jwt:Key"] = "dev-only-greencarwash-jwt-signing-key",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "7",
                ["Admin:Email"] = "admin@greencarwash.example",
                ["Admin:Password"] = "Admin@1234",
                ["Admin:FullName"] = "GreenCarWash Admin",
                ["Admin:PhoneNumber"] = "+12125550100"
            })
            .Build();
}
