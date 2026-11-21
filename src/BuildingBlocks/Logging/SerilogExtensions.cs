using Microsoft.AspNetCore.Builder;
using Serilog;

namespace GreenCarWash.BuildingBlocks.Logging;

public static class SerilogExtensions
{
    public static WebApplicationBuilder AddGreenCarWashLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, _, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.With<CorrelationIdEnricher>()
                .Enrich.With<SensitiveDataRedactionEnricher>()
                .Destructure.With<SensitiveDataDestructuringPolicy>()
                .WriteTo.Console()
                .WriteTo.File(
                    path: "logs/log-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true);
        });

        return builder;
    }
}
