using FluentValidation;
using GreenCarWash.CatalogService.Data;
using GreenCarWash.CatalogService.DTOs;
using GreenCarWash.CatalogService.Entities;
using GreenCarWash.CatalogService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CatalogService.Services;

public interface IWashServiceCatalog
{
    Task<IReadOnlyList<ServiceResponseDto>> ListActiveAsync(CancellationToken cancellationToken);

    Task<ServiceResponseDto> GetAsync(int serviceId, CancellationToken cancellationToken);

    Task<ServiceResponseDto> CreateAsync(ServiceWriteDto dto, CancellationToken cancellationToken);

    Task<ServiceResponseDto> UpdateAsync(int serviceId, ServiceWriteDto dto, CancellationToken cancellationToken);

    Task<ServiceResponseDto> SetActiveAsync(int serviceId, bool isActive, CancellationToken cancellationToken);
}

public sealed class WashServiceCatalog : IWashServiceCatalog
{
    private readonly IWashServiceRepository _services;
    private readonly IValidator<ServiceWriteDto> _validator;
    private readonly IClock _clock;
    private readonly ILogger<WashServiceCatalog> _logger;

    public WashServiceCatalog(
        IWashServiceRepository services,
        IValidator<ServiceWriteDto> validator,
        IClock clock,
        ILogger<WashServiceCatalog> logger)
    {
        _services = services;
        _validator = validator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServiceResponseDto>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var services = await _services.ListActiveAsync(cancellationToken);
        return services.Select(ToDto).ToArray();
    }

    public async Task<ServiceResponseDto> GetAsync(int serviceId, CancellationToken cancellationToken)
    {
        var service = await _services.FindAsync(serviceId, cancellationToken)
            ?? throw new NotFoundException("Service", serviceId);
        return ToDto(service);
    }

    public async Task<ServiceResponseDto> CreateAsync(ServiceWriteDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var name = dto.ServiceName.Trim();
        if (await _services.NameExistsAsync(name, null, cancellationToken))
            throw new ConflictException("A service with this name already exists.");

        var now = _clock.UtcNow;
        var service = new WashService
        {
            ServiceName = name,
            Description = Clean(dto.Description),
            Price = dto.Price,
            Discount = dto.Discount,
            Duration = dto.Duration,
            WaterSavedLiters = dto.WaterSavedLiters,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _services.AddAsync(service, cancellationToken);
        try
        {
            await _services.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("A service with this name already exists.");
        }

        _logger.LogInformation("Created service {ServiceId}", service.ServiceId);
        return ToDto(service);
    }

    public async Task<ServiceResponseDto> UpdateAsync(int serviceId, ServiceWriteDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var service = await _services.FindAsync(serviceId, cancellationToken)
            ?? throw new NotFoundException("Service", serviceId);
        var name = dto.ServiceName.Trim();
        if (await _services.NameExistsAsync(name, serviceId, cancellationToken))
            throw new ConflictException("A service with this name already exists.");

        service.ServiceName = name;
        service.Description = Clean(dto.Description);
        service.Price = dto.Price;
        service.Discount = dto.Discount;
        service.Duration = dto.Duration;
        service.WaterSavedLiters = dto.WaterSavedLiters;
        service.UpdatedAt = _clock.UtcNow;

        try
        {
            await _services.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("A service with this name already exists.");
        }

        return ToDto(service);
    }

    public async Task<ServiceResponseDto> SetActiveAsync(int serviceId, bool isActive, CancellationToken cancellationToken)
    {
        var service = await _services.FindAsync(serviceId, cancellationToken)
            ?? throw new NotFoundException("Service", serviceId);
        service.IsActive = isActive;
        service.UpdatedAt = _clock.UtcNow;
        await _services.SaveAsync(cancellationToken);
        return ToDto(service);
    }

    private static string? Clean(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        return description.Trim();
    }

    private static ServiceResponseDto ToDto(WashService service) => new()
    {
        ServiceId = service.ServiceId,
        ServiceName = service.ServiceName,
        Description = service.Description,
        Price = service.Price,
        Discount = service.Discount,
        Duration = service.Duration,
        WaterSavedLiters = service.WaterSavedLiters,
        IsActive = service.IsActive
    };
}
