using FluentValidation;
using GreenCarWash.WasherService.Data;
using GreenCarWash.WasherService.DTOs;
using GreenCarWash.WasherService.Entities;
using GreenCarWash.WasherService.Queue;
using GreenCarWash.WasherService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.WasherService.Services;

public interface IWasherProfileService
{
    Task<WasherResponseDto> CreateAsync(int userId, CreateWasherProfileDto dto, CancellationToken cancellationToken);

    Task<WasherResponseDto> GetAsync(int userId, CancellationToken cancellationToken);

    Task<WasherResponseDto> SetAvailabilityAsync(int userId, AvailabilityDto dto, CancellationToken cancellationToken);

    Task<WasherResponseDto> SetLocationAsync(int userId, LocationDto dto, CancellationToken cancellationToken);

    Task<WasherResponseDto> SavePhotoAsync(int userId, string? contentType, byte[] content, CancellationToken cancellationToken);
}

public sealed class WasherProfileService : IWasherProfileService
{
    private static readonly AssignmentStatus[] Busy =
    [
        AssignmentStatus.Assigned,
        AssignmentStatus.Accepted,
        AssignmentStatus.InProgress
    ];

    private readonly IWasherRepository _washers;
    private readonly IActiveCityCatalog _cities;
    private readonly IWasherQueue _queue;
    private readonly IWasherImageStore _images;
    private readonly IValidator<CreateWasherProfileDto> _createValidator;
    private readonly IValidator<AvailabilityDto> _availabilityValidator;
    private readonly IValidator<LocationDto> _locationValidator;
    private readonly IClock _clock;
    private readonly ILogger<WasherProfileService> _logger;

    public WasherProfileService(
        IWasherRepository washers,
        IActiveCityCatalog cities,
        IWasherQueue queue,
        IWasherImageStore images,
        IValidator<CreateWasherProfileDto> createValidator,
        IValidator<AvailabilityDto> availabilityValidator,
        IValidator<LocationDto> locationValidator,
        IClock clock,
        ILogger<WasherProfileService> logger)
    {
        _washers = washers;
        _cities = cities;
        _queue = queue;
        _images = images;
        _createValidator = createValidator;
        _availabilityValidator = availabilityValidator;
        _locationValidator = locationValidator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<WasherResponseDto> CreateAsync(int userId, CreateWasherProfileDto dto, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(dto, cancellationToken);
        if (await _washers.ExistsForUserAsync(userId, cancellationToken))
            throw new ConflictException("A profile already exists for this user.");
        if (!await _cities.IsActiveAsync(dto.CityId, cancellationToken))
            throw Input.Invalid("CityId", "City is not available.");

        var now = _clock.UtcNow;
        var washer = new WasherProfile
        {
            UserId = userId,
            CityId = dto.CityId,
            IsAvailable = false,
            IsActive = true,
            IsQualified = true,
            AssignmentStatus = AssignmentStatus.Available,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _washers.AddAsync(washer, cancellationToken);
        try
        {
            await _washers.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
        {
            throw new ConflictException("A profile already exists for this user.");
        }

        _logger.LogInformation("Created washer profile {WasherId} for user {UserId}", washer.WasherId, userId);
        return ToDto(washer);
    }

    public async Task<WasherResponseDto> GetAsync(int userId, CancellationToken cancellationToken)
    {
        var washer = await _washers.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Washer profile", userId);
        return ToDto(washer);
    }

    public async Task<WasherResponseDto> SetAvailabilityAsync(int userId, AvailabilityDto dto, CancellationToken cancellationToken)
    {
        await _availabilityValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var washer = await RequireAsync(userId, cancellationToken);
        if (dto.IsAvailable)
        {
            if (!washer.IsActive || !washer.IsQualified || Busy.Contains(washer.AssignmentStatus))
                throw new ConflictException("Washer cannot go available while inactive, unqualified, or on a job.");
            if (washer.AssignmentStatus == AssignmentStatus.Rejected)
                washer.AssignmentStatus = AssignmentStatus.Available;
        }

        if (dto.Latitude.HasValue)
        {
            washer.Latitude = dto.Latitude;
            washer.Longitude = dto.Longitude;
            washer.LastLocationAt = _clock.UtcNow;
        }

        washer.IsAvailable = dto.IsAvailable;
        if (!dto.IsAvailable && washer.AssignmentStatus == AssignmentStatus.Assigned && washer.CurrentBookingId is null)
        {
            washer.AssignmentStatus = AssignmentStatus.Available;
            washer.AssignedAt = null;
        }

        washer.UpdatedAt = _clock.UtcNow;
        await _washers.SaveAsync(cancellationToken);
        WasherQueueSync.Apply(_queue, washer);
        _logger.LogInformation("Washer {WasherId} availability is {IsAvailable}", washer.WasherId, washer.IsAvailable);
        return ToDto(washer);
    }

    public async Task<WasherResponseDto> SetLocationAsync(int userId, LocationDto dto, CancellationToken cancellationToken)
    {
        await _locationValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var washer = await RequireAsync(userId, cancellationToken);
        washer.Latitude = dto.Latitude;
        washer.Longitude = dto.Longitude;
        washer.LastLocationAt = _clock.UtcNow;
        washer.UpdatedAt = _clock.UtcNow;
        await _washers.SaveAsync(cancellationToken);
        _queue.UpdateDetails(washer.WasherId, washer.AvgRating, washer.Latitude, washer.Longitude);
        return ToDto(washer);
    }

    public async Task<WasherResponseDto> SavePhotoAsync(int userId, string? contentType, byte[] content, CancellationToken cancellationToken)
    {
        var washer = await RequireAsync(userId, cancellationToken);
        if (content.Length == 0 || content.Length > ImageSignatures.MaxBytes)
            throw Input.Invalid("Image", "Image must be a JPG or PNG up to 2 MB.");
        if (!ImageSignatures.TryGetExtension(contentType, content, out var extension))
            throw Input.Invalid("Image", "Image must be a JPG or PNG.");

        washer.ProfilePictureUrl = await _images.SaveAsync(washer.WasherId, extension, content, cancellationToken);
        washer.UpdatedAt = _clock.UtcNow;
        await _washers.SaveAsync(cancellationToken);
        return ToDto(washer);
    }

    private async Task<WasherProfile> RequireAsync(int userId, CancellationToken cancellationToken) =>
        await _washers.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Washer profile", userId);

    internal static WasherResponseDto ToDto(WasherProfile washer) => new()
    {
        WasherId = washer.WasherId,
        UserId = washer.UserId,
        CityId = washer.CityId,
        IsAvailable = washer.IsAvailable,
        IsActive = washer.IsActive,
        IsQualified = washer.IsQualified,
        CurrentBookingId = washer.CurrentBookingId,
        AssignedAt = washer.AssignedAt,
        AssignmentStatus = washer.AssignmentStatus.ToString(),
        Latitude = washer.Latitude,
        Longitude = washer.Longitude,
        LastLocationAt = washer.LastLocationAt,
        AvgRating = washer.AvgRating,
        TotalReviews = washer.TotalReviews,
        TotalCompletedWashes = washer.TotalCompletedWashes,
        ProfilePictureUrl = washer.ProfilePictureUrl
    };
}
