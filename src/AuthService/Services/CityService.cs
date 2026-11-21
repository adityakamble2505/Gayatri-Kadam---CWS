using FluentValidation;
using GreenCarWash.AuthService.DTOs;
using GreenCarWash.AuthService.Entities;
using GreenCarWash.AuthService.Repositories;
using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.AuthService.Services;

public interface ICityService
{
    Task<IReadOnlyList<CityResponseDto>> ListActiveAsync(CancellationToken cancellationToken);

    Task<CityResponseDto> CreateAsync(CityWriteDto dto, CancellationToken cancellationToken);

    Task<CityResponseDto> UpdateAsync(int cityId, CityWriteDto dto, CancellationToken cancellationToken);

    Task<CityResponseDto> SetActiveAsync(int cityId, bool isActive, CancellationToken cancellationToken);
}

public sealed class CityService : ICityService
{
    private readonly ICityRepository _cities;
    private readonly IValidator<CityWriteDto> _validator;
    private readonly ILogger<CityService> _logger;

    public CityService(ICityRepository cities, IValidator<CityWriteDto> validator, ILogger<CityService> logger)
    {
        _cities = cities;
        _validator = validator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CityResponseDto>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var cities = await _cities.ListActiveAsync(cancellationToken);
        return cities.Select(ToDto).ToArray();
    }

    public async Task<CityResponseDto> CreateAsync(CityWriteDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var name = dto.CityName.Trim();
        var state = dto.State.Trim();
        if (await _cities.NameExistsAsync(name, state, null, cancellationToken))
            throw new ConflictException("A city with this name and state already exists.");

        var city = new City
        {
            CityName = name,
            State = state,
            IsActive = true
        };
        await _cities.AddAsync(city, cancellationToken);
        try
        {
            await SqlServerErrors.ExecuteOnceAsync(() => _cities.SaveChangesAsync(cancellationToken));
        }
        catch (DbUpdateException exception) when (SqlServerErrors.IsDuplicateKey(exception))
        {
            throw new ConflictException("A city with this name and state already exists.");
        }

        _logger.LogInformation("City created. CityId: {CityId}", city.CityId);
        return ToDto(city);
    }

    public async Task<CityResponseDto> UpdateAsync(int cityId, CityWriteDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var city = await _cities.FindByIdAsync(cityId, cancellationToken)
            ?? throw new NotFoundException("City", cityId);

        var name = dto.CityName.Trim();
        var state = dto.State.Trim();
        if (await _cities.NameExistsAsync(name, state, cityId, cancellationToken))
            throw new ConflictException("A city with this name and state already exists.");

        city.CityName = name;
        city.State = state;
        try
        {
            await SqlServerErrors.ExecuteOnceAsync(() => _cities.SaveChangesAsync(cancellationToken));
        }
        catch (DbUpdateException exception) when (SqlServerErrors.IsDuplicateKey(exception))
        {
            throw new ConflictException("A city with this name and state already exists.");
        }

        _logger.LogInformation("City updated. CityId: {CityId}", city.CityId);
        return ToDto(city);
    }

    public async Task<CityResponseDto> SetActiveAsync(int cityId, bool isActive, CancellationToken cancellationToken)
    {
        var city = await _cities.FindByIdAsync(cityId, cancellationToken)
            ?? throw new NotFoundException("City", cityId);

        city.IsActive = isActive;
        await SqlServerErrors.ExecuteOnceAsync(() => _cities.SaveChangesAsync(cancellationToken));
        _logger.LogInformation("City {State}. CityId: {CityId}", isActive ? "activated" : "deactivated", city.CityId);
        return ToDto(city);
    }

    private static CityResponseDto ToDto(City city) => new()
    {
        CityId = city.CityId,
        CityName = city.CityName,
        State = city.State,
        IsActive = city.IsActive
    };

}
