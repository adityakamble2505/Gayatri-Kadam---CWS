using FluentValidation;
using GreenCarWash.WasherService.DTOs;
using GreenCarWash.WasherService.Entities;
using GreenCarWash.WasherService.Queue;
using GreenCarWash.WasherService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;

namespace GreenCarWash.WasherService.Services;

public interface IWasherDispatchService
{
    Task<NextWasherResultDto> NextAsync(NextWasherDto dto, CancellationToken cancellationToken);

    Task ReleaseAsync(ReleaseWasherDto dto, CancellationToken cancellationToken);

    Task ConfirmAsync(ConfirmAssignmentDto dto, CancellationToken cancellationToken);

    Task StartAsync(WasherIdDto dto, CancellationToken cancellationToken);

    Task CompleteAsync(WasherIdDto dto, CancellationToken cancellationToken);

    Task<WasherResponseDto> UpdateAssignmentAsync(AssignmentUpdateDto dto, CancellationToken cancellationToken);

    Task<WasherResponseDto> RateAsync(RatingUpdateDto dto, CancellationToken cancellationToken);

    Task<WasherResponseDto> GetAsync(int washerId, CancellationToken cancellationToken);

    Task<WasherResponseDto> GetByUserAsync(int userId, CancellationToken cancellationToken);

    Task SweepExpiredOffersAsync(CancellationToken cancellationToken);
}

public sealed class WasherDispatchService : IWasherDispatchService
{
    private readonly IWasherRepository _washers;
    private readonly IWasherQueue _queue;
    private readonly QueueLifetime _lifetime;
    private readonly IValidator<NextWasherDto> _nextValidator;
    private readonly IValidator<ReleaseWasherDto> _releaseValidator;
    private readonly IValidator<ConfirmAssignmentDto> _confirmValidator;
    private readonly IValidator<WasherIdDto> _idValidator;
    private readonly IValidator<AssignmentUpdateDto> _assignmentValidator;
    private readonly IValidator<RatingUpdateDto> _ratingValidator;
    private readonly IClock _clock;
    private readonly ILogger<WasherDispatchService> _logger;

    public WasherDispatchService(
        IWasherRepository washers,
        IWasherQueue queue,
        QueueLifetime lifetime,
        IValidator<NextWasherDto> nextValidator,
        IValidator<ReleaseWasherDto> releaseValidator,
        IValidator<ConfirmAssignmentDto> confirmValidator,
        IValidator<WasherIdDto> idValidator,
        IValidator<AssignmentUpdateDto> assignmentValidator,
        IValidator<RatingUpdateDto> ratingValidator,
        IClock clock,
        ILogger<WasherDispatchService> logger)
    {
        _washers = washers;
        _queue = queue;
        _lifetime = lifetime;
        _nextValidator = nextValidator;
        _releaseValidator = releaseValidator;
        _confirmValidator = confirmValidator;
        _idValidator = idValidator;
        _assignmentValidator = assignmentValidator;
        _ratingValidator = ratingValidator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<NextWasherResultDto> NextAsync(NextWasherDto dto, CancellationToken cancellationToken)
    {
        await _lifetime.Ready.WaitAsync(cancellationToken);
        await _nextValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var criteria = new WasherSelectionCriteria
        {
            Strategy = dto.Strategy,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude
        };
        var exclude = dto.ExcludeIds ?? [];

        while (true)
        {
            if (!_queue.TryDequeueNext(dto.CityId, exclude, criteria, out var entry))
                throw new NotFoundException("No washer is available.");

            var userId = await _washers.MarkAssignedAsync(entry.WasherId, _clock.UtcNow, cancellationToken);
            if (userId is null)
            {
                _queue.ClearOffer(entry.WasherId);
                continue;
            }

            _logger.LogInformation("Offered washer {WasherId} in city {CityId}", entry.WasherId, dto.CityId);
            return new NextWasherResultDto { WasherId = entry.WasherId, UserId = userId.Value };
        }
    }

    public async Task ReleaseAsync(ReleaseWasherDto dto, CancellationToken cancellationToken)
    {
        await _releaseValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var released = await _washers.ReleaseOfferAsync(dto.WasherId, _clock.UtcNow, cancellationToken);
        if (!released)
            throw new ConflictException("Washer cannot be released.");

        if (dto.ToTail)
        {
            _queue.ReturnToTail(dto.WasherId);
            if (!_queue.Contains(dto.WasherId))
            {
                var washer = await _washers.FindByIdAsync(dto.WasherId, cancellationToken);
                if (washer is not null && WasherQueueSync.IsEligible(washer))
                    _queue.Enqueue(WasherQueueSync.ToEntry(washer));
            }
        }
        else
        {
            _queue.ClearOffer(dto.WasherId);
        }

        _logger.LogInformation("Released washer {WasherId}. ToTail: {ToTail}", dto.WasherId, dto.ToTail);
    }

    public async Task ConfirmAsync(ConfirmAssignmentDto dto, CancellationToken cancellationToken)
    {
        await _confirmValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var confirmed = await _washers.ConfirmAsync(dto.WasherId, dto.BookingId, _clock.UtcNow, cancellationToken);
        if (!confirmed)
            throw new ConflictException("Washer assignment cannot be confirmed.");
        _queue.ClearOffer(dto.WasherId);
        _queue.Remove(dto.WasherId);
    }

    public async Task StartAsync(WasherIdDto dto, CancellationToken cancellationToken)
    {
        await _idValidator.ValidateAndThrowAsync(dto, cancellationToken);
        if (!await _washers.StartAsync(dto.WasherId, _clock.UtcNow, cancellationToken))
            throw new ConflictException("Washer assignment cannot be started.");
        _queue.Remove(dto.WasherId);
    }

    public async Task CompleteAsync(WasherIdDto dto, CancellationToken cancellationToken)
    {
        await _idValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var washer = await _washers.CompleteAsync(dto.WasherId, _clock.UtcNow, cancellationToken)
            ?? throw new ConflictException("Washer assignment cannot be completed.");
        _queue.ClearOffer(washer.WasherId);
        if (WasherQueueSync.IsEligible(washer))
            _queue.Enqueue(WasherQueueSync.ToEntry(washer));
        else
            _queue.Remove(washer.WasherId);
        _logger.LogInformation("Completed wash for washer {WasherId}. Total: {Total}", washer.WasherId, washer.TotalCompletedWashes);
    }

    public async Task<WasherResponseDto> UpdateAssignmentAsync(AssignmentUpdateDto dto, CancellationToken cancellationToken)
    {
        await _assignmentValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var washer = await _washers.FindByIdAsync(dto.WasherId, cancellationToken)
            ?? throw new NotFoundException("Washer", dto.WasherId);
        washer.AssignmentStatus = Enum.Parse<AssignmentStatus>(dto.AssignmentStatus, ignoreCase: true);
        if (dto.ClearBooking)
            washer.CurrentBookingId = null;
        else if (dto.CurrentBookingId.HasValue)
            washer.CurrentBookingId = dto.CurrentBookingId;
        washer.AssignedAt = washer.AssignmentStatus == AssignmentStatus.Available ? null : _clock.UtcNow;
        washer.UpdatedAt = _clock.UtcNow;
        await _washers.SaveAsync(cancellationToken);
        WasherQueueSync.Apply(_queue, washer);
        return WasherProfileService.ToDto(washer);
    }

    public async Task<WasherResponseDto> RateAsync(RatingUpdateDto dto, CancellationToken cancellationToken)
    {
        await _ratingValidator.ValidateAndThrowAsync(dto, cancellationToken);
        await _washers.ApplyRatingAsync(dto.WasherId, dto.Rating, _clock.UtcNow, cancellationToken);
        var washer = await _washers.FindByIdAsync(dto.WasherId, cancellationToken)
            ?? throw new NotFoundException("Washer", dto.WasherId);
        _queue.UpdateDetails(washer.WasherId, washer.AvgRating, washer.Latitude, washer.Longitude);
        return WasherProfileService.ToDto(washer);
    }

    public async Task<WasherResponseDto> GetAsync(int washerId, CancellationToken cancellationToken)
    {
        var washer = await _washers.FindByIdAsync(washerId, cancellationToken)
            ?? throw new NotFoundException("Washer", washerId);
        return WasherProfileService.ToDto(washer);
    }

    public async Task<WasherResponseDto> GetByUserAsync(int userId, CancellationToken cancellationToken)
    {
        var washer = await _washers.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Washer profile", userId);
        return WasherProfileService.ToDto(washer);
    }

    public async Task SweepExpiredOffersAsync(CancellationToken cancellationToken)
    {
        var expired = _queue.ExpiredOffers(_clock.UtcNow);
        foreach (var entry in expired)
        {
            var released = await _washers.ReleaseOfferAsync(entry.WasherId, _clock.UtcNow, cancellationToken);
            if (released)
            {
                _queue.ReturnToTail(entry.WasherId);
                _logger.LogInformation("Offer lease expired. Washer {WasherId} returned to the queue", entry.WasherId);
            }
            else
            {
                _queue.ClearOffer(entry.WasherId);
            }
        }
    }
}

public sealed class QueueLifetime
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => _ready.Task;

    public void MarkReady() => _ready.TrySetResult();
}
