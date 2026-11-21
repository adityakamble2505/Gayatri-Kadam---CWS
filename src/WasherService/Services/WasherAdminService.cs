using ClosedXML.Excel;
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

public interface IWasherAdminService
{
    Task<PagedWashersDto> ListAsync(int? cityId, bool? isActive, bool? isAvailable, int page, int pageSize, CancellationToken cancellationToken);

    Task<WasherResponseDto> CreateAsync(AdminWasherWriteDto dto, CancellationToken cancellationToken);

    Task<WasherResponseDto> UpdateAsync(int washerId, AdminWasherUpdateDto dto, CancellationToken cancellationToken);

    Task<WasherResponseDto> SetActiveAsync(int washerId, bool isActive, CancellationToken cancellationToken);

    Task<WasherResponseDto> SetQualifiedAsync(int washerId, bool isQualified, CancellationToken cancellationToken);

    Task<byte[]> ExportAsync(CancellationToken cancellationToken);

    IReadOnlyList<QueueEntryDto> Snapshot(int cityId);

    Task<IReadOnlyList<NearbyWasherDto>> NearbyAsync(int cityId, decimal latitude, decimal longitude, CancellationToken cancellationToken);
}

public sealed class WasherAdminService : IWasherAdminService
{
    private readonly IWasherRepository _washers;
    private readonly IActiveCityCatalog _cities;
    private readonly IWasherQueue _queue;
    private readonly IValidator<AdminWasherWriteDto> _writeValidator;
    private readonly IValidator<AdminWasherUpdateDto> _updateValidator;
    private readonly IClock _clock;
    private readonly ILogger<WasherAdminService> _logger;

    public WasherAdminService(
        IWasherRepository washers,
        IActiveCityCatalog cities,
        IWasherQueue queue,
        IValidator<AdminWasherWriteDto> writeValidator,
        IValidator<AdminWasherUpdateDto> updateValidator,
        IClock clock,
        ILogger<WasherAdminService> logger)
    {
        _washers = washers;
        _cities = cities;
        _queue = queue;
        _writeValidator = writeValidator;
        _updateValidator = updateValidator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PagedWashersDto> ListAsync(
        int? cityId,
        bool? isActive,
        bool? isAvailable,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);
        var (items, total) = await _washers.ListAsync(cityId, isActive, isAvailable, page, pageSize, cancellationToken);
        return new PagedWashersDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items.Select(WasherProfileService.ToDto).ToArray()
        };
    }

    public async Task<WasherResponseDto> CreateAsync(AdminWasherWriteDto dto, CancellationToken cancellationToken)
    {
        await _writeValidator.ValidateAndThrowAsync(dto, cancellationToken);
        if (await _washers.ExistsForUserAsync(dto.UserId, cancellationToken))
            throw new ConflictException("A profile already exists for this user.");
        if (!await _cities.IsActiveAsync(dto.CityId, cancellationToken))
            throw Input.Invalid("CityId", "City is not available.");

        var now = _clock.UtcNow;
        var washer = new WasherProfile
        {
            UserId = dto.UserId,
            CityId = dto.CityId,
            IsActive = dto.IsActive,
            IsQualified = dto.IsQualified,
            IsAvailable = false,
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

        _logger.LogInformation("Admin created washer {WasherId}", washer.WasherId);
        return WasherProfileService.ToDto(washer);
    }

    public async Task<WasherResponseDto> UpdateAsync(int washerId, AdminWasherUpdateDto dto, CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var washer = await RequireAsync(washerId, cancellationToken);
        if (!await _cities.IsActiveAsync(dto.CityId, cancellationToken))
            throw Input.Invalid("CityId", "City is not available.");

        washer.CityId = dto.CityId;
        washer.IsAvailable = dto.IsAvailable;
        washer.IsActive = dto.IsActive;
        washer.IsQualified = dto.IsQualified;
        if (dto.Latitude.HasValue)
        {
            washer.Latitude = dto.Latitude;
            washer.Longitude = dto.Longitude;
            washer.LastLocationAt = _clock.UtcNow;
        }

        washer.UpdatedAt = _clock.UtcNow;
        await _washers.SaveAsync(cancellationToken);
        WasherQueueSync.Apply(_queue, washer);
        return WasherProfileService.ToDto(washer);
    }

    public async Task<WasherResponseDto> SetActiveAsync(int washerId, bool isActive, CancellationToken cancellationToken)
    {
        var washer = await RequireAsync(washerId, cancellationToken);
        washer.IsActive = isActive;
        washer.UpdatedAt = _clock.UtcNow;
        await _washers.SaveAsync(cancellationToken);
        WasherQueueSync.Apply(_queue, washer);
        _logger.LogInformation("Washer {WasherId} active is {IsActive}", washerId, isActive);
        return WasherProfileService.ToDto(washer);
    }

    public async Task<WasherResponseDto> SetQualifiedAsync(int washerId, bool isQualified, CancellationToken cancellationToken)
    {
        var washer = await RequireAsync(washerId, cancellationToken);
        washer.IsQualified = isQualified;
        washer.UpdatedAt = _clock.UtcNow;
        await _washers.SaveAsync(cancellationToken);
        WasherQueueSync.Apply(_queue, washer);
        return WasherProfileService.ToDto(washer);
    }

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken)
    {
        var washers = await _washers.ListAllAsync(cancellationToken);
        var names = await _cities.NamesAsync(cancellationToken);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Washers");
        var headers = new[]
        {
            "WasherId", "UserId", "City", "Active", "Available", "AvgRating", "TotalReviews", "TotalCompletedWashes"
        };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];

        for (var index = 0; index < washers.Count; index++)
        {
            var washer = washers[index];
            var row = index + 2;
            sheet.Cell(row, 1).Value = washer.WasherId;
            sheet.Cell(row, 2).Value = washer.UserId;
            sheet.Cell(row, 3).Value = names.TryGetValue(washer.CityId, out var name) ? name : washer.CityId.ToString();
            sheet.Cell(row, 4).Value = washer.IsActive ? "Yes" : "No";
            sheet.Cell(row, 5).Value = washer.IsAvailable ? "Yes" : "No";
            sheet.Cell(row, 6).Value = washer.AvgRating;
            sheet.Cell(row, 7).Value = washer.TotalReviews;
            sheet.Cell(row, 8).Value = washer.TotalCompletedWashes;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyList<QueueEntryDto> Snapshot(int cityId)
    {
        if (cityId <= 0)
            throw Input.Invalid("CityId", "City id is required.");
        return _queue.Snapshot(cityId).Select(entry => new QueueEntryDto
        {
            WasherId = entry.WasherId,
            CityId = entry.CityId,
            AvgRating = entry.AvgRating,
            Latitude = entry.Latitude,
            Longitude = entry.Longitude,
            EnqueuedAtUtc = entry.EnqueuedAtUtc
        }).ToArray();
    }

    public async Task<IReadOnlyList<NearbyWasherDto>> NearbyAsync(
        int cityId,
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken)
    {
        if (cityId <= 0)
            throw Input.Invalid("CityId", "City id is required.");
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw Input.Invalid("Location", "Latitude and longitude are out of range.");

        var rows = await _washers.NearbyAsync(cityId, latitude, longitude, cancellationToken);
        return rows.Select(row => new NearbyWasherDto
        {
            WasherId = row.WasherId,
            UserId = row.UserId,
            CityId = row.CityId,
            AvgRating = row.AvgRating,
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            Miles = row.Miles
        }).ToArray();
    }

    private async Task<WasherProfile> RequireAsync(int washerId, CancellationToken cancellationToken) =>
        await _washers.FindByIdAsync(washerId, cancellationToken)
            ?? throw new NotFoundException("Washer", washerId);
}
