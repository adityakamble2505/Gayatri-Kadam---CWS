using FluentAssertions;
using FluentValidation;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.WasherService.Data;
using GreenCarWash.WasherService.DTOs;
using GreenCarWash.WasherService.Entities;
using GreenCarWash.WasherService.Queue;
using GreenCarWash.WasherService.Repositories;
using GreenCarWash.WasherService.Services;
using GreenCarWash.WasherService.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace GreenCarWash.WasherService.Tests;

public sealed class WasherFlowTests
{
    private FakeClock _clock = null!;
    private WasherDbContext _db = null!;
    private InMemoryWasherQueue _queue = null!;
    private Mock<IActiveCityCatalog> _cities = null!;
    private WasherProfileService _profiles = null!;
    private WasherAdminService _admin = null!;
    private WasherDispatchService _dispatch = null!;
    private WasherQueueRebuild _rebuild = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock();
        var options = new DbContextOptionsBuilder<WasherDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new WasherDbContext(options);
        _queue = new InMemoryWasherQueue(
            [new FifoStrategy(), new TopRatedStrategy(), new NearestStrategy()],
            _clock,
            Options.Create(new QueueOptions { DefaultStrategy = "Fifo", OfferLeaseSeconds = 120 }));
        _cities = new Mock<IActiveCityCatalog>();
        _cities.Setup(catalog => catalog.IsActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int cityId, CancellationToken _) => cityId == 1);
        _cities.Setup(catalog => catalog.NamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string> { [1] = "New York" });
        var repository = new WasherRepository(_db);
        _profiles = new WasherProfileService(
            repository,
            _cities.Object,
            _queue,
            Mock.Of<IWasherImageStore>(),
            new CreateWasherProfileDtoValidator(),
            new AvailabilityDtoValidator(),
            new LocationDtoValidator(),
            _clock,
            NullLogger<WasherProfileService>.Instance);
        _admin = new WasherAdminService(
            repository,
            _cities.Object,
            _queue,
            new AdminWasherWriteDtoValidator(),
            new AdminWasherUpdateDtoValidator(),
            _clock,
            NullLogger<WasherAdminService>.Instance);
        var lifetime = new QueueLifetime();
        lifetime.MarkReady();
        _dispatch = new WasherDispatchService(
            repository,
            _queue,
            lifetime,
            new NextWasherDtoValidator(),
            new ReleaseWasherDtoValidator(),
            new ConfirmAssignmentDtoValidator(),
            new WasherIdDtoValidator(),
            new AssignmentUpdateDtoValidator(),
            new RatingUpdateDtoValidator(),
            _clock,
            NullLogger<WasherDispatchService>.Instance);
        _rebuild = new WasherQueueRebuild(repository, _queue, _clock, NullLogger<WasherQueueRebuild>.Instance);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task TC_07_CreateProfile_AndGoAvailable_JoinsQueue()
    {
        var created = await _profiles.CreateAsync(4, new CreateWasherProfileDto { CityId = 1 }, CancellationToken.None);

        created.UserId.Should().Be(4);
        created.IsAvailable.Should().BeFalse();
        _queue.Contains(created.WasherId).Should().BeFalse();

        var online = await _profiles.SetAvailabilityAsync(4, new AvailabilityDto { IsAvailable = true }, CancellationToken.None);

        online.IsAvailable.Should().BeTrue();
        _queue.Contains(created.WasherId).Should().BeTrue();
        var again = () => _profiles.CreateAsync(4, new CreateWasherProfileDto { CityId = 1 }, CancellationToken.None);
        await again.Should().ThrowAsync<ConflictException>();
    }

    [Test]
    public async Task GoingOffline_RemovesTheWasherFromTheQueue()
    {
        var created = await _profiles.CreateAsync(4, new CreateWasherProfileDto { CityId = 1 }, CancellationToken.None);
        await _profiles.SetAvailabilityAsync(4, new AvailabilityDto { IsAvailable = true }, CancellationToken.None);

        await _profiles.SetAvailabilityAsync(4, new AvailabilityDto { IsAvailable = false }, CancellationToken.None);

        _queue.Contains(created.WasherId).Should().BeFalse();
        _queue.Snapshot(1).Should().BeEmpty();
    }

    [Test]
    public async Task Deactivate_RemovesTheWasherFromTheQueue()
    {
        var created = await _profiles.CreateAsync(4, new CreateWasherProfileDto { CityId = 1 }, CancellationToken.None);
        await _profiles.SetAvailabilityAsync(4, new AvailabilityDto { IsAvailable = true }, CancellationToken.None);

        var updated = await _admin.SetActiveAsync(created.WasherId, false, CancellationToken.None);

        updated.IsActive.Should().BeFalse();
        _queue.Contains(created.WasherId).Should().BeFalse();
    }

    [Test]
    public async Task Rebuild_LoadsAvailableWashers_AndResetsStaleAssignments()
    {
        var ready = await SaveAsync(Profile(10, available: true));
        await SaveAsync(Profile(11, available: true, active: false));
        await SaveAsync(Profile(12, available: false));
        await SaveAsync(Profile(13, available: true, qualified: false));
        var stale = await SaveAsync(Profile(14, available: true, status: AssignmentStatus.Assigned));
        var booked = await SaveAsync(Profile(15, available: true, status: AssignmentStatus.Assigned, bookingId: 99));
        await SaveAsync(Profile(16, available: true, status: AssignmentStatus.InProgress, bookingId: 100));

        await _rebuild.RebuildAsync(CancellationToken.None);

        _queue.Snapshot(1).Select(washer => washer.WasherId).Should().BeEquivalentTo([ready.WasherId, stale.WasherId]);
        (await _db.WasherProfiles.SingleAsync(washer => washer.WasherId == stale.WasherId)).AssignmentStatus
            .Should().Be(AssignmentStatus.Available);
        (await _db.WasherProfiles.SingleAsync(washer => washer.WasherId == booked.WasherId)).AssignmentStatus
            .Should().Be(AssignmentStatus.Assigned);
        _queue.Contains(booked.WasherId).Should().BeFalse();
    }

    [Test]
    public async Task Rating_UpdatesTheAverageBeforeTheReviewCount()
    {
        var washer = await SaveAsync(Profile(10, available: true, rating: 4.50m, reviews: 2));
        _queue.Enqueue(new WasherEntry { WasherId = washer.WasherId, CityId = 1, AvgRating = 4.50m, EnqueuedAtUtc = _clock.UtcNow });

        var updated = await _dispatch.RateAsync(new RatingUpdateDto { WasherId = washer.WasherId, Rating = 5 }, CancellationToken.None);

        updated.AvgRating.Should().Be(4.67m);
        updated.TotalReviews.Should().Be(3);
        _queue.Snapshot(1).Single().AvgRating.Should().Be(4.67m);
    }

    [Test]
    public async Task LeaseSweep_ReturnsAnExpiredOfferToTheTail()
    {
        var first = await SaveAsync(Profile(10, available: true));
        var second = await SaveAsync(Profile(11, available: true));
        _queue.Enqueue(new WasherEntry { WasherId = first.WasherId, CityId = 1, EnqueuedAtUtc = _clock.UtcNow });
        _queue.Enqueue(new WasherEntry { WasherId = second.WasherId, CityId = 1, EnqueuedAtUtc = _clock.UtcNow.AddMinutes(1) });
        _queue.TryDequeueNext(1, [], new WasherSelectionCriteria { Strategy = "Fifo" }, out _).Should().BeTrue();
        first.AssignmentStatus = AssignmentStatus.Assigned;
        await _db.SaveChangesAsync();

        _clock.UtcNow = _clock.UtcNow.AddSeconds(120);
        await _dispatch.SweepExpiredOffersAsync(CancellationToken.None);

        (await _db.WasherProfiles.SingleAsync(washer => washer.WasherId == first.WasherId)).AssignmentStatus
            .Should().Be(AssignmentStatus.Available);
        _queue.Snapshot(1).Select(washer => washer.WasherId).Should().Equal(second.WasherId, first.WasherId);
    }

    [Test]
    public async Task GoingAvailable_WhileOnAJob_IsConflict()
    {
        await _profiles.CreateAsync(4, new CreateWasherProfileDto { CityId = 1 }, CancellationToken.None);
        var washer = await _db.WasherProfiles.SingleAsync();
        washer.AssignmentStatus = AssignmentStatus.InProgress;
        await _db.SaveChangesAsync();

        var act = () => _profiles.SetAvailabilityAsync(4, new AvailabilityDto { IsAvailable = true }, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    private async Task<WasherProfile> SaveAsync(WasherProfile washer)
    {
        _db.WasherProfiles.Add(washer);
        await _db.SaveChangesAsync();
        return washer;
    }

    private WasherProfile Profile(
        int userId,
        bool available,
        bool active = true,
        bool qualified = true,
        AssignmentStatus status = AssignmentStatus.Available,
        int? bookingId = null,
        decimal rating = 0m,
        int reviews = 0) => new()
    {
        UserId = userId,
        CityId = 1,
        IsAvailable = available,
        IsActive = active,
        IsQualified = qualified,
        AssignmentStatus = status,
        CurrentBookingId = bookingId,
        AvgRating = rating,
        TotalReviews = reviews,
        CreatedAt = _clock.UtcNow,
        UpdatedAt = _clock.UtcNow
    };
}
