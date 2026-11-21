using FluentValidation;
using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Validators;
using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BuildingBlocks;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Pagination;
using GreenCarWash.BuildingBlocks.Time;

namespace GreenCarWash.BookingService.Services;

public interface IBookingWorkflow
{
    Task<BookingDetailDto> WashNowAsync(Caller caller, CreateBookingDto dto, CancellationToken cancellationToken);

    Task<BookingDetailDto> ScheduleAsync(Caller caller, ScheduleBookingDto dto, CancellationToken cancellationToken);

    Task<BookingDetailDto> AcceptAsync(Caller caller, int bookingId, CancellationToken cancellationToken);

    Task DeclineAsync(Caller caller, int bookingId, CancellationToken cancellationToken);

    Task<BookingDetailDto> StartAsync(Caller caller, int bookingId, CancellationToken cancellationToken);

    Task<BookingDetailDto> CompleteAsync(Caller caller, int bookingId, string? contentType, byte[] content, CancellationToken cancellationToken);

    Task<BookingDetailDto> CancelAsync(Caller caller, int bookingId, CancelBookingDto dto, CancellationToken cancellationToken);

    Task<BookingDetailDto> GetAsync(Caller caller, int bookingId, CancellationToken cancellationToken);

    Task<PagedResult<BookingSummaryDto>> MyOrdersAsync(Caller caller, OrderListQuery query, CancellationToken cancellationToken);

    Task<PagedResult<BookingSummaryDto>> WasherOrdersAsync(Caller caller, OrderListQuery query, CancellationToken cancellationToken);

    Task<InternalBookingDto> GetInternalAsync(int bookingId, CancellationToken cancellationToken);

    Task<BookingDetailDto> AssignAsync(int bookingId, int washerId, int adminUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BookingSummaryDto>> AttentionAsync(CancellationToken cancellationToken);
}

public sealed class BookingWorkflow : IBookingWorkflow
{
    private static readonly BookingStatus[] CurrentStatuses =
    [
        BookingStatus.Pending,
        BookingStatus.Scheduled,
        BookingStatus.Accepted,
        BookingStatus.InProgress
    ];

    private static readonly BookingStatus[] PastStatuses =
    [
        BookingStatus.Completed,
        BookingStatus.Cancelled
    ];

    private readonly IBookingRepository _bookings;
    private readonly ICustomerDirectory _customers;
    private readonly IWasherDirectory _washers;
    private readonly ICatalogPricing _catalog;
    private readonly IActiveCityCatalog _cities;
    private readonly IBookingImageStore _images;
    private readonly IValidator<CreateBookingDto> _createValidator;
    private readonly IValidator<ScheduleBookingDto> _scheduleValidator;
    private readonly IValidator<CancelBookingDto> _cancelValidator;
    private readonly IValidator<OrderListQuery> _listValidator;
    private readonly IDispatchQueue _dispatch;
    private readonly IPaymentCompensation _payments;
    private readonly IClock _clock;
    private readonly ILogger<BookingWorkflow> _logger;

    public BookingWorkflow(
        IBookingRepository bookings,
        ICustomerDirectory customers,
        IWasherDirectory washers,
        ICatalogPricing catalog,
        IActiveCityCatalog cities,
        IBookingImageStore images,
        IValidator<CreateBookingDto> createValidator,
        IValidator<ScheduleBookingDto> scheduleValidator,
        IValidator<CancelBookingDto> cancelValidator,
        IValidator<OrderListQuery> listValidator,
        IDispatchQueue dispatch,
        IPaymentCompensation payments,
        IClock clock,
        ILogger<BookingWorkflow> logger)
    {
        _bookings = bookings;
        _customers = customers;
        _washers = washers;
        _catalog = catalog;
        _cities = cities;
        _images = images;
        _createValidator = createValidator;
        _scheduleValidator = scheduleValidator;
        _cancelValidator = cancelValidator;
        _listValidator = listValidator;
        _dispatch = dispatch;
        _payments = payments;
        _clock = clock;
        _logger = logger;
    }

    public async Task<BookingDetailDto> WashNowAsync(Caller caller, CreateBookingDto dto, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(dto, cancellationToken);
        return await CreateAsync(caller, dto, BookingType.Immediate, _clock.UtcNow, cancellationToken);
    }

    public async Task<BookingDetailDto> ScheduleAsync(Caller caller, ScheduleBookingDto dto, CancellationToken cancellationToken)
    {
        await _scheduleValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var when = AsUtc(dto.BookingDate);
        if (when < _clock.UtcNow.AddHours(2))
            throw Input.Invalid("BookingDate", "Booking must be at least 2 hours from now.");
        return await CreateAsync(caller, dto, BookingType.Scheduled, when, cancellationToken);
    }

    public async Task<BookingDetailDto> AcceptAsync(Caller caller, int bookingId, CancellationToken cancellationToken)
    {
        var washer = await _washers.GetByUserAsync(caller.UserId, cancellationToken);
        var attempt = await _bookings.TryAcceptAsync(
            bookingId,
            washer.WasherId,
            washer.CityId,
            caller.UserId,
            _clock.UtcNow,
            cancellationToken);
        if (attempt.Outcome == AcceptOutcome.Overlap)
            throw new ConflictException("Washer has an overlapping booking.");
        if (attempt.Outcome == AcceptOutcome.AlreadyAssigned)
            throw new ConflictException("already assigned");
        if (attempt.Outcome == AcceptOutcome.OfferedToOther)
            throw new ForbiddenException("Washer does not hold the current offer.");
        if (attempt.Outcome == AcceptOutcome.NotOffered)
            throw new ConflictException("Washer does not hold the current offer.");

        try
        {
            await _washers.ConfirmAsync(washer.WasherId, bookingId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Washer confirm failed for booking {BookingId}", bookingId);
            await _bookings.RevertAcceptAsync(bookingId, washer.WasherId, attempt.PreviousStatus, _clock.UtcNow, cancellationToken);
            throw new BadGatewayException("Washer assignment could not be confirmed.");
        }

        await ReleaseOtherOffersAsync(bookingId, washer.WasherId, cancellationToken);
        _dispatch.TryComplete(bookingId, washer.WasherId, OfferDecision.Accepted);
        _logger.LogInformation("Washer {WasherId} accepted booking {BookingId}", washer.WasherId, bookingId);
        return ToDetail(await RequireAsync(bookingId, cancellationToken));
    }

    public async Task DeclineAsync(Caller caller, int bookingId, CancellationToken cancellationToken)
    {
        var washer = await _washers.GetByUserAsync(caller.UserId, cancellationToken);
        await _bookings.DeclineOfferAsync(bookingId, washer.WasherId, _clock.UtcNow, cancellationToken);
        await _bookings.AddNoteAsync(bookingId, caller.UserId, "Washer", "Washer declined", _clock.UtcNow, cancellationToken);
        _dispatch.TryComplete(bookingId, washer.WasherId, OfferDecision.Declined);
        _logger.LogInformation("Washer {WasherId} declined booking {BookingId}", washer.WasherId, bookingId);
        try
        {
            await _washers.ReleaseOfferAsync(washer.WasherId, cancellationToken);
        }
        catch (Exception exception) when (exception is ConflictException or NotFoundException)
        {
            _logger.LogInformation("Washer {WasherId} had no offer lease to release", washer.WasherId);
        }
    }

    public async Task<BookingDetailDto> StartAsync(Caller caller, int bookingId, CancellationToken cancellationToken)
    {
        var washer = await _washers.GetByUserAsync(caller.UserId, cancellationToken);
        var booking = await RequireTrackedAsync(bookingId, cancellationToken);
        if (booking.WasherId != washer.WasherId)
            throw new ForbiddenException("Only the assigned washer can start this booking.");
        BookingStateMachine.Ensure(booking.Status, BookingStatus.InProgress);

        var now = _clock.UtcNow;
        booking.StartedAt = now;
        await _bookings.ChangeStatusAsync(
            booking,
            BookingStatus.InProgress,
            caller.UserId,
            "Washer",
            "Service started",
            "ServiceStarted",
            now,
            cancellationToken);

        try
        {
            await _washers.StartAsync(washer.WasherId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Washer start failed for booking {BookingId}", bookingId);
            var current = await RequireTrackedAsync(bookingId, cancellationToken);
            current.StartedAt = null;
            await _bookings.ChangeStatusAsync(
                current,
                BookingStatus.Accepted,
                SystemActor.SystemUserId,
                "System",
                "Washer start failed",
                null,
                _clock.UtcNow,
                cancellationToken);
            throw new BadGatewayException("Washer assignment could not be started.");
        }

        return ToDetail(await RequireAsync(bookingId, cancellationToken));
    }

    public async Task<BookingDetailDto> CompleteAsync(
        Caller caller,
        int bookingId,
        string? contentType,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var washer = await _washers.GetByUserAsync(caller.UserId, cancellationToken);
        var booking = await RequireTrackedAsync(bookingId, cancellationToken);
        if (booking.WasherId != washer.WasherId)
            throw new ForbiddenException("Only the assigned washer can complete this booking.");
        BookingStateMachine.Ensure(booking.Status, BookingStatus.Completed);
        if (content.Length == 0 || content.Length > ImageSignatures.MaxBytes)
            throw Input.Invalid("Image", "After-wash photo must be a JPG or PNG up to 5 MB.");
        if (!ImageSignatures.TryGetExtension(contentType, content, out var extension))
            throw Input.Invalid("Image", "After-wash photo must be a JPG or PNG.");

        var url = await _images.SaveAsync(bookingId, extension, content, cancellationToken);
        var now = _clock.UtcNow;
        booking.AfterWashPhotoUrl = url;
        booking.CompletedAt = now;
        await _bookings.ChangeStatusAsync(
            booking,
            BookingStatus.Completed,
            caller.UserId,
            "Washer",
            "Service completed",
            "ServiceCompleted",
            now,
            cancellationToken);

        try
        {
            await _washers.CompleteAsync(washer.WasherId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Washer complete failed for booking {BookingId}", bookingId);
            var current = await RequireTrackedAsync(bookingId, cancellationToken);
            current.AfterWashPhotoUrl = null;
            current.CompletedAt = null;
            await _bookings.ChangeStatusAsync(
                current,
                BookingStatus.InProgress,
                SystemActor.SystemUserId,
                "System",
                "Washer complete failed",
                null,
                _clock.UtcNow,
                cancellationToken);
            throw new BadGatewayException("Washer assignment could not be completed.");
        }

        return ToDetail(await RequireAsync(bookingId, cancellationToken));
    }

    public async Task<BookingDetailDto> CancelAsync(Caller caller, int bookingId, CancelBookingDto dto, CancellationToken cancellationToken)
    {
        await _cancelValidator.ValidateAndThrowAsync(dto, cancellationToken);
        var booking = await RequireTrackedAsync(bookingId, cancellationToken);
        var actor = await AuthorizeCancelAsync(caller, booking, cancellationToken);
        BookingStateMachine.Ensure(booking.Status, BookingStatus.Cancelled);

        var washerId = booking.WasherId;
        var promo = booking.PromoCode;
        var now = _clock.UtcNow;
        booking.CancelledAt = now;
        booking.CancelledBy = actor;
        booking.CancelReason = dto.Reason.Trim();
        await _bookings.ChangeStatusAsync(
            booking,
            BookingStatus.Cancelled,
            caller.UserId,
            actor.ToString(),
            dto.Reason.Trim(),
            "BookingCancelled",
            now,
            cancellationToken);

        await StopDispatchAsync(bookingId, cancellationToken);
        await FreeWasherAndPromoAsync(washerId, promo, releasePromo: true, cancellationToken);
        await _payments.RefundOnCancelAsync(booking, cancellationToken);
        _logger.LogInformation("Booking {BookingId} cancelled by {Actor}", bookingId, actor);
        return ToDetail(await RequireAsync(bookingId, cancellationToken));
    }

    public async Task<BookingDetailDto> GetAsync(Caller caller, int bookingId, CancellationToken cancellationToken)
    {
        var booking = await _bookings.FindReadAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", bookingId);
        if (!await CanReadAsync(caller, booking, cancellationToken))
            throw new ForbiddenException("You cannot view this booking.");
        return ToDetail(booking);
    }

    public async Task<PagedResult<BookingSummaryDto>> MyOrdersAsync(Caller caller, OrderListQuery query, CancellationToken cancellationToken)
    {
        await _listValidator.ValidateAndThrowAsync(query, cancellationToken);
        var customer = await _customers.GetByUserAsync(caller.UserId, cancellationToken);
        return await ListAsync(customer.CustomerId, null, query, cancellationToken);
    }

    public async Task<PagedResult<BookingSummaryDto>> WasherOrdersAsync(Caller caller, OrderListQuery query, CancellationToken cancellationToken)
    {
        await _listValidator.ValidateAndThrowAsync(query, cancellationToken);
        var washer = await _washers.GetByUserAsync(caller.UserId, cancellationToken);
        return await ListAsync(null, washer.WasherId, query, cancellationToken);
    }

    public async Task<InternalBookingDto> GetInternalAsync(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await _bookings.FindReadAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", bookingId);
        var customer = await _customers.GetByIdAsync(booking.CustomerId, cancellationToken);
        int? washerUserId = null;
        if (booking.WasherId is int washerId)
            washerUserId = (await _washers.GetByIdAsync(washerId, cancellationToken)).UserId;
        return new InternalBookingDto
        {
            BookingId = booking.BookingId,
            BookingNumber = booking.BookingNumber,
            Status = booking.Status.ToString(),
            TotalAmount = booking.TotalAmount,
            CustomerId = booking.CustomerId,
            CustomerUserId = customer.UserId,
            WasherId = booking.WasherId,
            WasherUserId = washerUserId,
            ServiceName = booking.ServiceNameSnapshot,
            AddOns = booking.AddOns.Select(ToAddOn).ToArray(),
            AfterWashPhotoUrl = booking.AfterWashPhotoUrl,
            CancelledBy = booking.CancelledBy?.ToString()
        };
    }

    private async Task<BookingDetailDto> CreateAsync(
        Caller caller,
        CreateBookingDto dto,
        BookingType type,
        DateTime bookingDate,
        CancellationToken cancellationToken)
    {
        var customer = await _customers.GetByUserAsync(caller.UserId, cancellationToken);
        if (!await _cities.IsActiveAsync(customer.CityId, cancellationToken))
            throw Input.Invalid("CityId", "City is not available.");
        var vehicle = await _customers.GetVehicleAsync(customer.CustomerId, dto.VehicleId, cancellationToken);
        if (vehicle.CustomerId != customer.CustomerId)
            throw new NotFoundException("Vehicle", dto.VehicleId);

        var promo = string.IsNullOrWhiteSpace(dto.PromoCode) ? null : dto.PromoCode.Trim().ToUpperInvariant();
        var quote = await _catalog.QuoteAsync(dto.ServiceId, dto.AddOnIds ?? [], promo, cancellationToken);
        var now = _clock.UtcNow;
        var booking = new Booking
        {
            CustomerId = customer.CustomerId,
            VehicleId = dto.VehicleId,
            ServiceId = dto.ServiceId,
            CityId = customer.CityId,
            BookingType = type,
            BookingDate = bookingDate,
            EstimatedDurationMin = quote.DurationMin,
            Address = customer.Address,
            Latitude = customer.Latitude,
            Longitude = customer.Longitude,
            Instructions = string.IsNullOrWhiteSpace(dto.Instructions) ? null : dto.Instructions.Trim(),
            ServiceNameSnapshot = quote.ServiceNameSnapshot,
            ServicePriceSnapshot = quote.ServicePriceSnapshot,
            ServiceDiscountPct = quote.DiscountPct,
            AddOnsTotal = quote.AddOnsTotal,
            PromoCode = promo,
            PromoDiscount = quote.PromoDiscount,
            TotalAmount = quote.TotalAmount,
            WaterSavedLiters = quote.WaterSavedLiters,
            Status = type == BookingType.Immediate ? BookingStatus.Pending : BookingStatus.Scheduled,
            CreatedAt = now,
            UpdatedAt = now,
            AddOns = quote.AddOns.Select(addOn => new BookingAddOn
            {
                AddOnId = addOn.Id,
                NameSnapshot = addOn.Name,
                PriceSnapshot = addOn.Price
            }).ToList()
        };

        await _bookings.CommitNewAsync(booking, caller.UserId, cancellationToken);
        if (type == BookingType.Immediate)
            _dispatch.Enqueue(new DispatchRequest(booking.BookingId, DispatchKind.Immediate));
        if (promo is not null)
            await RedeemOrCompensateAsync(booking, promo, Merchandise(quote), cancellationToken);

        _logger.LogInformation("Created booking {BookingId} for customer {CustomerId}", booking.BookingId, customer.CustomerId);
        return ToDetail(await RequireAsync(booking.BookingId, cancellationToken));
    }

    private async Task RedeemOrCompensateAsync(Booking booking, string promo, decimal subtotal, CancellationToken cancellationToken)
    {
        var redeemed = false;
        try
        {
            await _catalog.RedeemAsync(promo, subtotal, cancellationToken);
            redeemed = true;
            await _bookings.TouchAsync(booking.BookingId, _clock.UtcNow, cancellationToken);
        }
        catch (Exception exception) when (!redeemed && exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Promo redeem failed for booking {BookingId}", booking.BookingId);
            await CancelBySystemAsync(booking.BookingId, "Promo invalid", cancellationToken);
            await StopDispatchAsync(booking.BookingId, cancellationToken);
            throw new ConflictException("Promo invalid");
        }
        catch (Exception exception) when (redeemed && exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Booking save failed after promo redeem {BookingId}", booking.BookingId);
            try
            {
                await _catalog.ReleaseAsync(promo, cancellationToken);
            }
            catch (Exception releaseException)
            {
                _logger.LogError(releaseException, "Promo release failed for booking {BookingId}", booking.BookingId);
            }

            throw;
        }
    }

    private async Task CancelBySystemAsync(int bookingId, string reason, CancellationToken cancellationToken)
    {
        var booking = await RequireTrackedAsync(bookingId, cancellationToken);
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.InProgress)
            return;
        var now = _clock.UtcNow;
        booking.CancelledAt = now;
        booking.CancelledBy = CancelledBy.System;
        booking.CancelReason = reason;
        await _bookings.ChangeStatusAsync(
            booking,
            BookingStatus.Cancelled,
            SystemActor.SystemUserId,
            "System",
            reason,
            "BookingCancelled",
            now,
            cancellationToken);
        await _payments.RefundOnCancelAsync(booking, cancellationToken);
    }

    public async Task<BookingDetailDto> AssignAsync(int bookingId, int washerId, int adminUserId, CancellationToken cancellationToken)
    {
        await new AssignWasherDtoValidator().ValidateAndThrowAsync(new AssignWasherDto { WasherId = washerId }, cancellationToken);
        var booking = await _bookings.FindReadAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", bookingId);
        var washer = await _washers.GetByIdAsync(washerId, cancellationToken);
        if (!washer.IsActive || !washer.IsQualified)
            throw new ConflictException("Washer is not active and qualified.");
        if (washer.CityId != booking.CityId)
            throw new ConflictException("Washer is not in the booking city.");

        _dispatch.Cancel(bookingId);
        var attempt = await _bookings.TryAcceptAsync(
            bookingId,
            washer.WasherId,
            washer.CityId,
            adminUserId,
            _clock.UtcNow,
            cancellationToken,
            requireOffer: false,
            clearAttention: true,
            role: "Admin",
            note: "Admin assigned");
        if (attempt.Outcome == AcceptOutcome.Overlap)
            throw new ConflictException("Washer has an overlapping booking.");
        if (attempt.Outcome != AcceptOutcome.Accepted)
            throw new ConflictException("already assigned");

        try
        {
            await _washers.StageAssignedAsync(washer.WasherId, cancellationToken);
            await _washers.ConfirmAsync(washer.WasherId, bookingId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Admin assign confirm failed for booking {BookingId}", bookingId);
            await _bookings.RevertAcceptAsync(bookingId, washer.WasherId, attempt.PreviousStatus, _clock.UtcNow, cancellationToken);
            throw new BadGatewayException("Washer assignment could not be confirmed.");
        }

        await ReleaseOtherOffersAsync(bookingId, washer.WasherId, cancellationToken);
        return ToDetail(await RequireAsync(bookingId, cancellationToken));
    }

    public async Task<IReadOnlyList<BookingSummaryDto>> AttentionAsync(CancellationToken cancellationToken)
    {
        var rows = await _bookings.AttentionAsync(cancellationToken);
        return rows.Select(ToSummary).ToArray();
    }

    private async Task StopDispatchAsync(int bookingId, CancellationToken cancellationToken)
    {
        _dispatch.Cancel(bookingId);
        var released = await _bookings.ExpirePendingOffersAsync(bookingId, _clock.UtcNow, null, cancellationToken);
        foreach (var washerId in released.Distinct())
        {
            try
            {
                await _washers.ReleaseOfferAsync(washerId, cancellationToken);
            }
            catch (Exception exception) when (exception is ConflictException or NotFoundException or BadGatewayException)
            {
                _logger.LogInformation("Washer {WasherId} had no offer lease to release", washerId);
            }
        }
    }

    private async Task ReleaseOtherOffersAsync(int bookingId, int winnerId, CancellationToken cancellationToken)
    {
        var released = await _bookings.ExpirePendingOffersAsync(bookingId, _clock.UtcNow, winnerId, cancellationToken);
        foreach (var washerId in released.Distinct())
        {
            try
            {
                await _washers.ReleaseOfferAsync(washerId, cancellationToken);
            }
            catch (Exception exception) when (exception is ConflictException or NotFoundException or BadGatewayException)
            {
                _logger.LogInformation("Washer {WasherId} had no offer lease to release", washerId);
            }
        }
    }

    private async Task FreeWasherAndPromoAsync(int? washerId, string? promo, bool releasePromo, CancellationToken cancellationToken)
    {
        if (washerId is int assigned)
        {
            try
            {
                await _washers.ReleaseOfferAsync(assigned, cancellationToken);
                await _washers.MakeAvailableAsync(assigned, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to free washer {WasherId}", assigned);
            }
        }

        if (releasePromo && !string.IsNullOrWhiteSpace(promo))
        {
            try
            {
                await _catalog.ReleaseAsync(promo, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to release promo");
            }
        }
    }

    private async Task<CancelledBy> AuthorizeCancelAsync(Caller caller, Booking booking, CancellationToken cancellationToken)
    {
        if (IsRole(caller, "Admin"))
            return CancelledBy.Admin;
        if (IsRole(caller, "Customer"))
        {
            var customer = await _customers.GetByUserAsync(caller.UserId, cancellationToken);
            if (customer.CustomerId != booking.CustomerId)
                throw new ForbiddenException("You cannot cancel this booking.");
            return CancelledBy.Customer;
        }

        if (IsRole(caller, "Washer"))
        {
            var washer = await _washers.GetByUserAsync(caller.UserId, cancellationToken);
            if (booking.WasherId != washer.WasherId)
                throw new ForbiddenException("You cannot cancel this booking.");
            return CancelledBy.Washer;
        }

        throw new ForbiddenException("You cannot cancel this booking.");
    }

    private async Task<bool> CanReadAsync(Caller caller, Booking booking, CancellationToken cancellationToken)
    {
        if (IsRole(caller, "Admin"))
            return true;
        if (IsRole(caller, "Customer"))
        {
            var customer = await _customers.GetByUserAsync(caller.UserId, cancellationToken);
            return customer.CustomerId == booking.CustomerId;
        }

        if (IsRole(caller, "Washer"))
        {
            var washer = await _washers.GetByUserAsync(caller.UserId, cancellationToken);
            return booking.WasherId == washer.WasherId;
        }

        return false;
    }

    private async Task<PagedResult<BookingSummaryDto>> ListAsync(
        int? customerId,
        int? washerId,
        OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var (page, pageSize, skip) = Pagination.Normalize(query.Page, query.PageSize);
        var statuses = query.Scope.Equals("past", StringComparison.OrdinalIgnoreCase) ? PastStatuses : CurrentStatuses;
        var (items, total) = await _bookings.ListAsync(customerId, washerId, statuses, skip, pageSize, cancellationToken);
        return Pagination.Create(items.Select(ToSummary).ToArray(), page, pageSize, total);
    }

    private async Task<Booking> RequireAsync(int bookingId, CancellationToken cancellationToken) =>
        await _bookings.FindReadAsync(bookingId, cancellationToken) ?? throw new NotFoundException("Booking", bookingId);

    private async Task<Booking> RequireTrackedAsync(int bookingId, CancellationToken cancellationToken) =>
        await _bookings.FindAsync(bookingId, cancellationToken) ?? throw new NotFoundException("Booking", bookingId);

    private static bool IsRole(Caller caller, string role) =>
        caller.Role.Equals(role, StringComparison.OrdinalIgnoreCase);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    private static decimal Merchandise(QuoteSnapshot quote) =>
        quote.ServicePriceSnapshot * (1m - quote.DiscountPct / 100m) + quote.AddOnsTotal;

    private static BookingAddOnDto ToAddOn(BookingAddOn addOn) => new()
    {
        AddOnId = addOn.AddOnId,
        Name = addOn.NameSnapshot,
        Price = addOn.PriceSnapshot
    };

    private static BookingSummaryDto ToSummary(Booking booking) => new()
    {
        BookingId = booking.BookingId,
        BookingNumber = booking.BookingNumber,
        ServiceNameSnapshot = booking.ServiceNameSnapshot,
        BookingType = booking.BookingType.ToString(),
        BookingDate = booking.BookingDate,
        Status = booking.Status.ToString(),
        TotalAmount = booking.TotalAmount,
        WasherId = booking.WasherId,
        CityId = booking.CityId,
        CompletedAt = booking.CompletedAt,
        CancelledAt = booking.CancelledAt
    };

    private static BookingDetailDto ToDetail(Booking booking) => new()
    {
        BookingId = booking.BookingId,
        BookingNumber = booking.BookingNumber,
        CustomerId = booking.CustomerId,
        VehicleId = booking.VehicleId,
        ServiceId = booking.ServiceId,
        WasherId = booking.WasherId,
        CityId = booking.CityId,
        BookingType = booking.BookingType.ToString(),
        BookingDate = booking.BookingDate,
        EstimatedDurationMin = booking.EstimatedDurationMin,
        Address = booking.Address,
        Latitude = booking.Latitude,
        Longitude = booking.Longitude,
        Instructions = booking.Instructions,
        ServiceNameSnapshot = booking.ServiceNameSnapshot,
        ServicePriceSnapshot = booking.ServicePriceSnapshot,
        ServiceDiscountPct = booking.ServiceDiscountPct,
        AddOnsTotal = booking.AddOnsTotal,
        PromoCode = booking.PromoCode,
        PromoDiscount = booking.PromoDiscount,
        TotalAmount = booking.TotalAmount,
        WaterSavedLiters = booking.WaterSavedLiters,
        Status = booking.Status.ToString(),
        AfterWashPhotoUrl = booking.AfterWashPhotoUrl,
        AcceptedAt = booking.AcceptedAt,
        StartedAt = booking.StartedAt,
        CompletedAt = booking.CompletedAt,
        CancelledAt = booking.CancelledAt,
        CancelledBy = booking.CancelledBy?.ToString(),
        CancelReason = booking.CancelReason,
        AddOns = booking.AddOns.Select(ToAddOn).ToArray()
    };
}
