using FluentAssertions;
using GreenCarWash.BuildingBlocks.Logging;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GreenCarWash.AuthService.Tests;

public class SensitiveLogTests
{
    [Test]
    public void Password_Property_IsRedacted()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With<SensitiveDataRedactionEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Login attempt with {Password}", "hunter2");

        sink.Events.Should().ContainSingle();
        var value = sink.Events[0].Properties["Password"].Should().BeOfType<ScalarValue>().Subject;
        value.Value.Should().Be("***");
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
