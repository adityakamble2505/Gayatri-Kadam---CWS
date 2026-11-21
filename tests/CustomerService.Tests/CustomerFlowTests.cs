using FluentAssertions;
using FluentValidation;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.CustomerService.Data;
using GreenCarWash.CustomerService.DTOs;
using GreenCarWash.CustomerService.Repositories;
using GreenCarWash.CustomerService.Services;
using GreenCarWash.CustomerService.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace GreenCarWash.CustomerService.Tests;

public sealed class CustomerFlowTests
{
    private CustomerDbContext _db = null!;
    private Mock<IActiveCityCatalog> _cities = null!;
    private Mock<IVehicleImageStore> _images = null!;
    private ProfileService _profiles = null!;
    private VehicleService _vehicles = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CustomerDbContext(options);
        _cities = new Mock<IActiveCityCatalog>();
        _cities.Setup(catalog => catalog.IsActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int cityId, CancellationToken _) => Task.FromResult(cityId is >= 1 and <= 5));
        _images = new Mock<IVehicleImageStore>();
        var profiles = new ProfileRepository(_db);
        var vehicles = new VehicleRepository(_db);
        var clock = new FakeClock();
        _profiles = new ProfileService(profiles, _cities.Object, new ProfileWriteDtoValidator(), clock, NullLogger<ProfileService>.Instance);
        _vehicles = new VehicleService(
            profiles,
            vehicles,
            _images.Object,
            new VehicleWriteDtoValidator(),
            clock,
            NullLogger<VehicleService>.Instance);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task TC_05_CreateProfile_Succeeds()
    {
        var profile = await _profiles.CreateAsync(7, Profile(1, "10 Main St"), CancellationToken.None);

        profile.UserId.Should().Be(7);
        profile.CityId.Should().Be(1);
        profile.Address.Should().Be("10 Main St");
        profile.CustomerId.Should().BeGreaterThan(0);
        (await _db.CustomerProfiles.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task TC_06_AddVehicle_Succeeds()
    {
        await _profiles.CreateAsync(7, Profile(1, "10 Main St"), CancellationToken.None);

        var vehicle = await _vehicles.AddAsync(7, Vehicle("ka01ab1234", "Sedan"), CancellationToken.None);
        var listed = await _vehicles.ListActiveAsync(7, CancellationToken.None);

        vehicle.VehicleNumber.Should().Be("KA01AB1234");
        vehicle.VehicleType.Should().Be("Sedan");
        listed.Should().ContainSingle().Which.VehicleId.Should().Be(vehicle.VehicleId);
    }

    [Test]
    public async Task Ownership_OtherCustomerCannotSeeOrDeleteVehicle()
    {
        var owner = await _profiles.CreateAsync(7, Profile(1, "10 Main St"), CancellationToken.None);
        var other = await _profiles.CreateAsync(8, Profile(2, "20 Side St"), CancellationToken.None);
        var vehicle = await _vehicles.AddAsync(7, Vehicle("KA01AB1234", "SUV"), CancellationToken.None);

        var otherList = await _vehicles.ListActiveAsync(8, CancellationToken.None);
        var delete = () => _vehicles.DeleteAsync(8, vehicle.VehicleId, CancellationToken.None);
        var wrongOwner = () => _vehicles.GetForBookingAsync(other.CustomerId, vehicle.VehicleId, CancellationToken.None);

        otherList.Should().BeEmpty();
        await delete.Should().ThrowAsync<NotFoundException>();
        await wrongOwner.Should().ThrowAsync<NotFoundException>();
        (await _vehicles.ListActiveAsync(7, CancellationToken.None)).Should().ContainSingle();

        var booking = await _vehicles.GetForBookingAsync(owner.CustomerId, vehicle.VehicleId, CancellationToken.None);
        booking.UserId.Should().Be(7);
        booking.CustomerId.Should().Be(owner.CustomerId);

        await _vehicles.DeleteAsync(7, vehicle.VehicleId, CancellationToken.None);
        (await _vehicles.ListActiveAsync(7, CancellationToken.None)).Should().BeEmpty();
        (await _db.Vehicles.SingleAsync()).IsActive.Should().BeFalse();
    }

    [Test]
    public async Task DuplicateVehicleNumber_IsConflict()
    {
        await _profiles.CreateAsync(7, Profile(1, "10 Main St"), CancellationToken.None);
        await _vehicles.AddAsync(7, Vehicle("KA01AB1234", "Hatchback"), CancellationToken.None);

        var act = () => _vehicles.AddAsync(7, Vehicle("ka01ab1234", "Sedan"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await _db.Vehicles.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task DuplicateProfile_IsConflict()
    {
        await _profiles.CreateAsync(7, Profile(1, "10 Main St"), CancellationToken.None);

        var act = () => _profiles.CreateAsync(7, Profile(2, "20 Side St"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await _db.CustomerProfiles.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task InvalidCity_IsRejected()
    {
        var act = () => _profiles.CreateAsync(7, Profile(99, "10 Main St"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*City is not available*");
        (await _db.CustomerProfiles.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task Image_RejectsBadBytes_AndStoresAJpeg()
    {
        await _profiles.CreateAsync(7, Profile(1, "10 Main St"), CancellationToken.None);
        var vehicle = await _vehicles.AddAsync(7, Vehicle("KA01AB1234", "Sedan"), CancellationToken.None);
        var pngTypeJpegBytes = () => _vehicles.SaveImageAsync(7, vehicle.VehicleId, "image/png", [0xFF, 0xD8, 0xFF], CancellationToken.None);

        await pngTypeJpegBytes.Should().ThrowAsync<ValidationException>();
        _images.Verify(
            store => store.SaveAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _images.Setup(store => store.SaveAsync(vehicle.VehicleId, ".jpg", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/uploads/vehicles/1-test.jpg");
        var saved = await _vehicles.SaveImageAsync(7, vehicle.VehicleId, "image/jpeg", [0xFF, 0xD8, 0xFF, 0x00], CancellationToken.None);

        saved.ImageUrl.Should().Be("/uploads/vehicles/1-test.jpg");
    }

    private static ProfileWriteDto Profile(int cityId, string address) => new()
    {
        CityId = cityId,
        Address = address,
        Latitude = 40.712800m,
        Longitude = -74.006000m
    };

    private static VehicleWriteDto Vehicle(string number, string type) => new()
    {
        VehicleNumber = number,
        Brand = "Honda",
        Model = "City",
        VehicleType = type
    };

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    }
}
