using FluentValidation;
using GreenCarWash.CustomerService.Data;
using GreenCarWash.CustomerService.DTOs;
using GreenCarWash.CustomerService.Entities;
using GreenCarWash.CustomerService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CustomerService.Services;

public interface IProfileService
{
    Task<ProfileDto> CreateAsync(int userId, ProfileWriteDto dto, CancellationToken cancellationToken);

    Task<ProfileDto> GetAsync(int userId, CancellationToken cancellationToken);

    Task<ProfileDto> UpdateAsync(int userId, ProfileWriteDto dto, CancellationToken cancellationToken);

    Task<ProfileDto> GetByUserIdAsync(int userId, CancellationToken cancellationToken);

    Task<ProfileDto> GetByIdAsync(int customerId, CancellationToken cancellationToken);
}

public sealed class ProfileService : IProfileService
{
    private readonly IProfileRepository _profiles;
    private readonly IActiveCityCatalog _cities;
    private readonly IValidator<ProfileWriteDto> _validator;
    private readonly IClock _clock;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(
        IProfileRepository profiles,
        IActiveCityCatalog cities,
        IValidator<ProfileWriteDto> validator,
        IClock clock,
        ILogger<ProfileService> logger)
    {
        _profiles = profiles;
        _cities = cities;
        _validator = validator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ProfileDto> CreateAsync(int userId, ProfileWriteDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        if (await _profiles.ExistsForUserAsync(userId, cancellationToken))
            throw new ConflictException("A profile already exists for this user.");
        await RequireActiveCityAsync(dto.CityId, cancellationToken);

        var now = _clock.UtcNow;
        var profile = new CustomerProfile
        {
            UserId = userId,
            CityId = dto.CityId,
            Address = dto.Address.Trim(),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _profiles.AddAsync(profile, cancellationToken);
        try
        {
            await _profiles.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("A profile already exists for this user.");
        }

        _logger.LogInformation("Created customer profile {CustomerId} for user {UserId}", profile.CustomerId, userId);
        return ToDto(profile);
    }

    public async Task<ProfileDto> GetAsync(int userId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Customer profile", userId);
        return ToDto(profile);
    }

    public async Task<ProfileDto> UpdateAsync(int userId, ProfileWriteDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var profile = await _profiles.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Customer profile", userId);
        await RequireActiveCityAsync(dto.CityId, cancellationToken);

        profile.CityId = dto.CityId;
        profile.Address = dto.Address.Trim();
        profile.Latitude = dto.Latitude;
        profile.Longitude = dto.Longitude;
        profile.UpdatedAt = _clock.UtcNow;
        await _profiles.SaveAsync(cancellationToken);
        return ToDto(profile);
    }

    public Task<ProfileDto> GetByUserIdAsync(int userId, CancellationToken cancellationToken) =>
        GetAsync(userId, cancellationToken);

    public async Task<ProfileDto> GetByIdAsync(int customerId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.FindAsync(customerId, cancellationToken)
            ?? throw new NotFoundException("Customer profile", customerId);
        return ToDto(profile);
    }

    private async Task RequireActiveCityAsync(int cityId, CancellationToken cancellationToken)
    {
        if (!await _cities.IsActiveAsync(cityId, cancellationToken))
            throw Input.Invalid("CityId", "City is not available.");
    }

    private static ProfileDto ToDto(CustomerProfile profile) => new()
    {
        CustomerId = profile.CustomerId,
        UserId = profile.UserId,
        CityId = profile.CityId,
        Address = profile.Address,
        Latitude = profile.Latitude,
        Longitude = profile.Longitude
    };
}
