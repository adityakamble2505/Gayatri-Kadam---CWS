using System.Collections.Concurrent;
using System.Threading.Channels;
using GreenCarWash.BookingService.Entities;
using GreenCarWash.BookingService.Repositories;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using Microsoft.Extensions.Options;

namespace GreenCarWash.BookingService.Services;

public sealed class DispatchCoordinator : BackgroundService, IDispatchQueue
{
    private readonly Channel<DispatchRequest> _channel = Channel.CreateBounded<DispatchRequest>(
        new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    private readonly ConcurrentDictionary<int, DispatchSession> _sessions = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly DispatchOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<DispatchCoordinator> _logger;

    public DispatchCoordinator(
        IServiceScopeFactory scopes,
        IOptions<DispatchOptions> options,
        IClock clock,
        ILogger<DispatchCoordinator> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public void Enqueue(DispatchRequest request)
    {
        if (_sessions.ContainsKey(request.BookingId))
            return;
        if (!_channel.Writer.TryWrite(request))
            _logger.LogError("Dispatch channel is full. Booking {BookingId} was not queued", request.BookingId);
    }

    public bool TryComplete(int bookingId, int washerId, OfferDecision decision)
    {
        if (!_sessions.TryGetValue(bookingId, out var session))
            return false;
        return session.TryComplete(washerId, decision);
    }

    public void Cancel(int bookingId)
    {
        if (_sessions.TryGetValue(bookingId, out var session))
            session.Cancel();
    }

    public bool HoldsOffer(int bookingId, int washerId) =>
        _sessions.TryGetValue(bookingId, out var session) && session.HasWasher(washerId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var session = new DispatchSession(request.BookingId);
            if (!_sessions.TryAdd(request.BookingId, session))
                continue;
            session.Work = RunAndRemoveAsync(session, request, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in _sessions.Values)
            session.Cancel();
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private async Task RunAndRemoveAsync(DispatchSession session, DispatchRequest request, CancellationToken stoppingToken)
    {
        try
        {
            if (request.Kind == DispatchKind.Scheduled)
                await RunScheduledAsync(session, request.BookingId, stoppingToken);
            else
                await RunImmediateAsync(session, request.BookingId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || session.IsCancelled)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Dispatch failed for booking {BookingId}", request.BookingId);
        }
        finally
        {
            _sessions.TryRemove(request.BookingId, out _);
        }
    }

    private async Task RunImmediateAsync(DispatchSession session, int bookingId, CancellationToken stoppingToken)
    {
        var deadline = _clock.UtcNow.Add(_options.SessionBudget);
        var attempts = 0;
        while (!session.IsCancelled && !stoppingToken.IsCancellationRequested && attempts < _options.MaxAttempts && _clock.UtcNow < deadline)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
            var washers = scope.ServiceProvider.GetRequiredService<IWasherDirectory>();
            var booking = await bookings.FindReadAsync(bookingId, stoppingToken);
            if (booking is null || booking.WasherId is not null || booking.Status != BookingStatus.Pending)
                return;

            attempts = Math.Max(attempts, await bookings.CountOffersAsync(bookingId, stoppingToken));
            if (attempts >= _options.MaxAttempts)
                break;

            var live = await bookings.LiveOffersAsync(bookingId, _clock.UtcNow, stoppingToken);
            if (live.Count > 0)
            {
                var offer = live[0];
                var decision = await WaitAsync(session, offer.WasherId, offer.ExpiresAt - _clock.UtcNow, stoppingToken);
                if (decision is OfferDecision.Accepted or OfferDecision.Cancelled)
                    return;
                if (decision == OfferDecision.Expired && await bookings.ExpireOfferIfPendingAsync(offer.OfferId, _clock.UtcNow, stoppingToken))
                    await ReleaseQuietlyAsync(washers, offer.WasherId, stoppingToken);
                continue;
            }

            WasherOffer next;
            try
            {
                var excluded = await bookings.OfferedWasherIdsAsync(bookingId, stoppingToken);
                next = await washers.NextAsync(
                    booking.CityId, excluded, _options.ImmediateStrategy, booking.Latitude, booking.Longitude, stoppingToken);
            }
            catch (NotFoundException)
            {
                attempts++;
                if (attempts >= _options.MaxAttempts)
                    break;
                await Task.Delay(_options.RetryDelay, stoppingToken);
                continue;
            }

            var now = _clock.UtcNow;
            var offerId = await PlaceOfferAsync(session, bookings, bookingId, next, now, now.Add(_options.OfferDuration), stoppingToken);
            var outcome = await WaitAsync(session, next.WasherId, _options.OfferDuration, stoppingToken);
            attempts++;
            if (outcome is OfferDecision.Accepted or OfferDecision.Cancelled)
                return;
            if (outcome == OfferDecision.Expired && await bookings.ExpireOfferIfPendingAsync(offerId, _clock.UtcNow, stoppingToken))
                await ReleaseQuietlyAsync(washers, next.WasherId, stoppingToken);
        }

        if (session.IsCancelled || stoppingToken.IsCancellationRequested)
            return;

        await using var flagScope = _scopes.CreateAsyncScope();
        var store = flagScope.ServiceProvider.GetRequiredService<IBookingRepository>();
        if (await store.FlagAdminAsync(bookingId, "Dispatch attempts exhausted", _clock.UtcNow, CancellationToken.None))
            _logger.LogInformation("Booking {BookingId} needs admin attention", bookingId);
    }

    private async Task RunScheduledAsync(DispatchSession session, int bookingId, CancellationToken stoppingToken)
    {
        DateTime windowEnd;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
            var washers = scope.ServiceProvider.GetRequiredService<IWasherDirectory>();
            var booking = await bookings.FindReadAsync(bookingId, stoppingToken);
            if (booking?.DispatchStartedAt is null || booking.WasherId is not null || booking.Status != BookingStatus.Scheduled)
                return;

            windowEnd = booking.DispatchStartedAt.Value.AddMinutes(_options.ScheduledWindowMinutes);
            var attempts = await bookings.CountOffersAsync(bookingId, stoppingToken);
            while (!session.IsCancelled && !stoppingToken.IsCancellationRequested && _clock.UtcNow < windowEnd && attempts < _options.MaxAttempts)
            {
                var current = await bookings.FindReadAsync(bookingId, stoppingToken);
                if (current is null || current.WasherId is not null || current.Status != BookingStatus.Scheduled)
                    return;

                WasherOffer next;
                try
                {
                    var excluded = await bookings.OfferedWasherIdsAsync(bookingId, stoppingToken);
                    next = await washers.NextAsync(
                        current.CityId, excluded, _options.ScheduledStrategy, current.Latitude, current.Longitude, stoppingToken);
                }
                catch (NotFoundException)
                {
                    break;
                }

                var now = _clock.UtcNow;
                await PlaceOfferAsync(session, bookings, bookingId, next, now, windowEnd, stoppingToken);
                attempts++;
            }

            while (!session.IsCancelled && !stoppingToken.IsCancellationRequested && _clock.UtcNow < windowEnd)
            {
                var currentBooking = await bookings.FindReadAsync(bookingId, stoppingToken);
                if (currentBooking is null || currentBooking.WasherId is not null || currentBooking.Status != BookingStatus.Scheduled)
                    return;
                if (session.Snapshot().Count == 0)
                    break;

                var progressed = await WaitAnyAsync(session, windowEnd, stoppingToken);
                if (progressed is OfferDecision.Accepted or OfferDecision.Cancelled || session.IsCancelled)
                    return;
                if (progressed == OfferDecision.Declined && attempts < _options.MaxAttempts)
                {
                    try
                    {
                        var excluded = await bookings.OfferedWasherIdsAsync(bookingId, stoppingToken);
                        var replacement = await washers.NextAsync(
                            currentBooking.CityId, excluded, _options.ScheduledStrategy, currentBooking.Latitude, currentBooking.Longitude, stoppingToken);
                        await PlaceOfferAsync(session, bookings, bookingId, replacement, _clock.UtcNow, windowEnd, stoppingToken);
                        attempts++;
                    }
                    catch (NotFoundException)
                    {
                    }
                }
            }
        }

        if (session.IsCancelled || stoppingToken.IsCancellationRequested)
            return;

        await using var maintenanceScope = _scopes.CreateAsyncScope();
        var maintenance = maintenanceScope.ServiceProvider.GetRequiredService<DispatchMaintenance>();
        await maintenance.EscalateAsync(bookingId, "Scheduled dispatch window elapsed", CancellationToken.None);
    }

    private async Task<long> PlaceOfferAsync(
        DispatchSession session,
        IBookingRepository bookings,
        int bookingId,
        WasherOffer next,
        DateTime now,
        DateTime expires,
        CancellationToken stoppingToken)
    {
        var completion = new TaskCompletionSource<OfferDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Arm(next.WasherId, completion);
        var offer = await bookings.AddOfferAsync(new BookingOffer
        {
            BookingId = bookingId,
            WasherId = next.WasherId,
            OfferedAt = now,
            ExpiresAt = expires,
            Response = OfferResponse.Pending
        }, now, stoppingToken);
        _logger.LogInformation("Offered booking {BookingId} to washer {WasherId}", bookingId, next.WasherId);
        return offer.OfferId;
    }

    private async Task<OfferDecision> WaitAsync(
        DispatchSession session,
        int washerId,
        TimeSpan remaining,
        CancellationToken stoppingToken)
    {
        if (!session.TryGet(washerId, out var completion))
        {
            completion = new TaskCompletionSource<OfferDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Arm(washerId, completion);
        }

        if (remaining <= TimeSpan.Zero)
            return completion.Task.IsCompleted ? await completion.Task : OfferDecision.Expired;

        using var timeout = new CancellationTokenSource(remaining);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, session.Token, stoppingToken);
        try
        {
            var decision = await completion.Task.WaitAsync(linked.Token);
            session.Disarm(washerId);
            return decision;
        }
        catch (OperationCanceledException) when (session.IsCancelled || stoppingToken.IsCancellationRequested)
        {
            return OfferDecision.Cancelled;
        }
        catch (OperationCanceledException)
        {
            session.Disarm(washerId);
            return OfferDecision.Expired;
        }
    }

    private async Task<OfferDecision> WaitAnyAsync(DispatchSession session, DateTime deadline, CancellationToken stoppingToken)
    {
        var remaining = deadline - _clock.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return OfferDecision.Expired;
        var pending = session.Snapshot();
        if (pending.Count == 0)
            return OfferDecision.Expired;

        var delay = Task.Delay(remaining, stoppingToken);
        var finished = await Task.WhenAny(pending.Select(item => item.Task).Append(delay));
        if (finished == delay || stoppingToken.IsCancellationRequested)
            return OfferDecision.Expired;
        var winner = pending.First(item => item.Task == finished);
        session.Disarm(winner.WasherId);
        return await winner.Task;
    }

    private async Task ReleaseQuietlyAsync(IWasherDirectory washers, int washerId, CancellationToken cancellationToken)
    {
        try
        {
            await washers.ReleaseOfferAsync(washerId, cancellationToken);
        }
        catch (Exception exception) when (exception is ConflictException or NotFoundException or BadGatewayException)
        {
            _logger.LogInformation("Washer {WasherId} was not holding an offer lease", washerId);
        }
    }

    private sealed class DispatchSession
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<OfferDecision>> _pending = new();
        private readonly CancellationTokenSource _stop = new();

        public DispatchSession(int bookingId) => BookingId = bookingId;

        public int BookingId { get; }

        public Task? Work { get; set; }

        public bool IsCancelled => _stop.IsCancellationRequested;

        public CancellationToken Token => _stop.Token;

        public void Cancel() => _stop.Cancel();

        public void Arm(int washerId, TaskCompletionSource<OfferDecision> completion) => _pending[washerId] = completion;

        public void Disarm(int washerId) => _pending.TryRemove(washerId, out _);

        public bool HasWasher(int washerId) => _pending.ContainsKey(washerId);

        public bool TryGet(int washerId, out TaskCompletionSource<OfferDecision> completion) =>
            _pending.TryGetValue(washerId, out completion!);

        public bool TryComplete(int washerId, OfferDecision decision) =>
            _pending.TryGetValue(washerId, out var completion) && completion.TrySetResult(decision);

        public IReadOnlyList<(int WasherId, Task<OfferDecision> Task)> Snapshot() =>
            _pending.Select(pair => (pair.Key, pair.Value.Task)).ToArray();
    }
}
