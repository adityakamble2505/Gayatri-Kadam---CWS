using FluentValidation;
using GreenCarWash.CustomerService.Data;
using GreenCarWash.CustomerService.DTOs;
using GreenCarWash.CustomerService.Entities;
using GreenCarWash.CustomerService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.CustomerService.Services;

public interface IVehicleImageStore
{
    Task<string> SaveAsync(int vehicleId, string extension, byte[] content, CancellationToken cancellationToken);
}

public sealed class DiskVehicleImageStore : IVehicleImageStore
{
    private readonly string _directory;

    public DiskVehicleImageStore(IWebHostEnvironment environment)
    {
        _directory = Path.Combine(environment.ContentRootPath, "uploads", "vehicles");
    }

    public async Task<string> SaveAsync(int vehicleId, string extension, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var name = $"{vehicleId}-{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(_directory, name), content, cancellationToken);
        return $"/uploads/vehicles/{name}";
    }
}

public interface IVehicleService
{
    Task<VehicleDto> AddAsync(int userId, VehicleWriteDto dto, CancellationToken cancellationToken);

    Task<IReadOnlyList<VehicleDto>> ListActiveAsync(int userId, CancellationToken cancellationToken);

    Task DeleteAsync(int userId, int vehicleId, CancellationToken cancellationToken);

    Task<VehicleDto> SaveImageAsync(int userId, int vehicleId, string? contentType, byte[] content, CancellationToken cancellationToken);

    Task<InternalVehicleDto> GetForBookingAsync(int customerId, int vehicleId, CancellationToken cancellationToken);
}

public sealed class VehicleService : IVehicleService
{
    private readonly IProfileRepository _profiles;
    private readonly IVehicleRepository _vehicles;
    private readonly IVehicleImageStore _images;
    private readonly IValidator<VehicleWriteDto> _validator;
    private readonly IClock _clock;
    private readonly ILogger<VehicleService> _logger;

    public VehicleService(
        IProfileRepository profiles,
        IVehicleRepository vehicles,
        IVehicleImageStore images,
        IValidator<VehicleWriteDto> validator,
        IClock clock,
        ILogger<VehicleService> logger)
    {
        _profiles = profiles;
        _vehicles = vehicles;
        _images = images;
        _validator = validator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<VehicleDto> AddAsync(int userId, VehicleWriteDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var profile = await RequireProfileAsync(userId, cancellationToken);
        var number = dto.VehicleNumber.Trim().ToUpperInvariant();
        if (await _vehicles.NumberExistsAsync(profile.CustomerId, number, cancellationToken))
            throw new ConflictException("A vehicle with this number already exists.");

        var vehicle = new Vehicle
        {
            CustomerId = profile.CustomerId,
            VehicleNumber = number,
            Brand = dto.Brand.Trim(),
            Model = dto.Model.Trim(),
            VehicleType = Enum.Parse<VehicleType>(dto.VehicleType, ignoreCase: true),
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };
        await _vehicles.AddAsync(vehicle, cancellationToken);
        try
        {
            await _vehicles.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("A vehicle with this number already exists.");
        }

        _logger.LogInformation("Added vehicle {VehicleId} for customer {CustomerId}", vehicle.VehicleId, profile.CustomerId);
        return ToDto(vehicle);
    }

    public async Task<IReadOnlyList<VehicleDto>> ListActiveAsync(int userId, CancellationToken cancellationToken)
    {
        var profile = await RequireProfileAsync(userId, cancellationToken);
        var vehicles = await _vehicles.ListActiveAsync(profile.CustomerId, cancellationToken);
        return vehicles.Select(ToDto).ToArray();
    }

    public async Task DeleteAsync(int userId, int vehicleId, CancellationToken cancellationToken)
    {
        var profile = await RequireProfileAsync(userId, cancellationToken);
        var vehicle = await _vehicles.FindActiveAsync(profile.CustomerId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Vehicle", vehicleId);
        vehicle.IsActive = false;
        await _vehicles.SaveAsync(cancellationToken);
        _logger.LogInformation("Soft-deleted vehicle {VehicleId}", vehicleId);
    }

    public async Task<VehicleDto> SaveImageAsync(
        int userId,
        int vehicleId,
        string? contentType,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var profile = await RequireProfileAsync(userId, cancellationToken);
        var vehicle = await _vehicles.FindActiveAsync(profile.CustomerId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Vehicle", vehicleId);
        if (content.Length == 0 || content.Length > ImageSignatures.MaxBytes)
            throw Input.Invalid("Image", "Image must be a JPG or PNG up to 2 MB.");
        if (!ImageSignatures.TryGetExtension(contentType, content, out var extension))
            throw Input.Invalid("Image", "Image must be a JPG or PNG.");

        vehicle.ImageUrl = await _images.SaveAsync(vehicle.VehicleId, extension, content, cancellationToken);
        await _vehicles.SaveAsync(cancellationToken);
        return ToDto(vehicle);
    }

    public async Task<InternalVehicleDto> GetForBookingAsync(int customerId, int vehicleId, CancellationToken cancellationToken)
    {
        var vehicle = await _vehicles.FindActiveWithOwnerAsync(customerId, vehicleId, cancellationToken)
            ?? throw new NotFoundException("Vehicle", vehicleId);
        var owner = vehicle.Customer
            ?? throw new NotFoundException("Vehicle", vehicleId);

        return new InternalVehicleDto
        {
            VehicleId = vehicle.VehicleId,
            CustomerId = vehicle.CustomerId,
            UserId = owner.UserId,
            VehicleNumber = vehicle.VehicleNumber,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            VehicleType = vehicle.VehicleType.ToString(),
            ImageUrl = vehicle.ImageUrl,
            IsActive = vehicle.IsActive
        };
    }

    private async Task<CustomerProfile> RequireProfileAsync(int userId, CancellationToken cancellationToken) =>
        await _profiles.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Customer profile", userId);

    private static VehicleDto ToDto(Vehicle vehicle) => new()
    {
        VehicleId = vehicle.VehicleId,
        CustomerId = vehicle.CustomerId,
        VehicleNumber = vehicle.VehicleNumber,
        Brand = vehicle.Brand,
        Model = vehicle.Model,
        VehicleType = vehicle.VehicleType.ToString(),
        ImageUrl = vehicle.ImageUrl,
        IsActive = vehicle.IsActive
    };
}
