using GreenCarWash.BuildingBlocks.Correlation;
using GreenCarWash.BuildingBlocks.Http;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace GreenCarWash.BuildingBlocks.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[GreenCarWashHeaders.CorrelationId].ToString();
        var correlationId = string.IsNullOrWhiteSpace(incoming)
            ? Guid.NewGuid().ToString("D")
            : incoming.Trim();

        context.Items[GreenCarWashHeaders.CorrelationId] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[GreenCarWashHeaders.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        var previous = CorrelationContext.Id;
        CorrelationContext.Id = correlationId;
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            try
            {
                await next(context);
            }
            finally
            {
                CorrelationContext.Id = previous;
            }
        }
    }
}
