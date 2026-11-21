using FluentAssertions;
using GreenCarWash.WasherService.Data;
using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Time;
using Moq;
using NUnit.Framework;

namespace GreenCarWash.WasherService.Tests;

public class WiringTests
{
    [Test]
    public void DbContext_UsesTheSharedServiceContext()
    {
        typeof(WasherDbContext).BaseType.Should().Be(typeof(ServiceDbContext));
    }

    [Test]
    public void Clock_CanBeFaked()
    {
        var expected = new DateTime(2026, 11, 21, 9, 30, 0, DateTimeKind.Utc);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(expected);

        clock.Object.UtcNow.Should().Be(expected);
    }
}
