using FluentValidation;
using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.BuildingBlocks.Pagination;
using GreenCarWash.BuildingBlocks.Time;

namespace GreenCarWash.BookingService.Services;

public interface IReviewService
{
    Task<ReviewDto> CreateAsync(Caller caller, CreateReviewDto dto, CancellationToken cancellationToken);

    Task<PagedResult<ReviewDto>> ForWasherAsync(int washerId, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed class ReviewService : IReviewService
{
    private readonly IReviewRepository _reviews;
    private readonly ICustomerDirectory _customers;
    private readonly IWasherDirectory _washers;
    private readonly IBookingOutbox _outbox;
    private readonly IValidator<CreateReviewDto> _validator;
    private readonly IClock _clock;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        IReviewRepository reviews,
        ICustomerDirectory customers,
        IWasherDirectory washers,
        IBookingOutbox outbox,
        IValidator<CreateReviewDto> validator,
        IClock clock,
        ILogger<ReviewService> logger)
    {
        _reviews = reviews;
        _customers = customers;
        _washers = washers;
        _outbox = outbox;
        _validator = validator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ReviewDto> CreateAsync(Caller caller, CreateReviewDto dto, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(dto, cancellationToken);
        var booking = await _reviews.FindBookingAsync(dto.BookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", dto.BookingId);
        if (booking.Status != BookingStatus.Completed)
            throw new ConflictException("Reviews are allowed only after the booking is completed.");
        if (booking.WasherId is not int washerId)
            throw new ConflictException("This booking has no washer to review.");

        var role = await ParticipantAsync(caller, booking, washerId, cancellationToken);
        var review = new Review
        {
            BookingId = booking.BookingId,
            ReviewerRole = role,
            CustomerId = booking.CustomerId,
            WasherId = washerId,
            Rating = dto.Rating,
            Comments = string.IsNullOrWhiteSpace(dto.Comments) ? null : dto.Comments.Trim(),
            CreatedAt = _clock.UtcNow
        };
        await _reviews.AddAsync(review, cancellationToken);
        await _reviews.SaveAsync(cancellationToken);

        if (role == ReviewerRole.Customer)
            await SyncRatingAsync(booking, review, cancellationToken);

        return ToDto(review);
    }

    public async Task<PagedResult<ReviewDto>> ForWasherAsync(
        int washerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedSize, skip) = Pagination.Normalize(page, pageSize);
        var (items, total) = await _reviews.ListForWasherAsync(washerId, skip, normalizedSize, cancellationToken);
        return Pagination.Create(items.Select(ToDto).ToArray(), normalizedPage, normalizedSize, total);
    }

    private async Task<ReviewerRole> ParticipantAsync(
        Caller caller,
        Booking booking,
        int washerId,
        CancellationToken cancellationToken)
    {
        if (caller.Role == "Customer")
        {
            var customer = await _customers.GetByUserAsync(caller.UserId, cancellationToken);
            if (customer.CustomerId != booking.CustomerId)
                throw new ForbiddenException("You can only review a booking you took part in.");
            return ReviewerRole.Customer;
        }

        if (caller.Role == "Washer")
        {
            var washer = await _washers.GetByUserAsync(caller.UserId, cancellationToken);
            if (washer.WasherId != washerId)
                throw new ForbiddenException("You can only review a booking you took part in.");
            return ReviewerRole.Washer;
        }

        throw new ForbiddenException("You can only review a booking you took part in.");
    }

    private async Task SyncRatingAsync(Booking booking, Review review, CancellationToken cancellationToken)
    {
        try
        {
            await _washers.RateAsync(booking.WasherId!.Value, review.Rating, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Washer rating sync failed for booking {BookingId}", booking.BookingId);
            try
            {
                var pending = await _outbox.CreateAsync(
                    booking,
                    "RatingSyncPending",
                    booking.Status.ToString(),
                    _clock.UtcNow,
                    cancellationToken,
                    reviewId: review.ReviewId);
                await _reviews.AddOutboxAsync(pending, cancellationToken);
            }
            catch (Exception writeFailure) when (writeFailure is not OperationCanceledException)
            {
                _logger.LogWarning(writeFailure, "Could not store RatingSyncPending for booking {BookingId}", booking.BookingId);
            }
        }
    }

    private static ReviewDto ToDto(Review review) => new()
    {
        ReviewId = review.ReviewId,
        BookingId = review.BookingId,
        ReviewerRole = review.ReviewerRole.ToString(),
        CustomerId = review.CustomerId,
        WasherId = review.WasherId,
        Rating = review.Rating,
        Comments = review.Comments,
        CreatedAt = review.CreatedAt
    };
}
