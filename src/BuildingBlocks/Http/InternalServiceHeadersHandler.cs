using GreenCarWash.BuildingBlocks.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace GreenCarWash.BuildingBlocks.Http;

public sealed class InternalServiceHeadersHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public InternalServiceHeadersHandler(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var internalKey = _configuration["InternalApi:Key"];
        if (!string.IsNullOrEmpty(internalKey))
            request.Headers.TryAddWithoutValidation(GreenCarWashHeaders.InternalKey, internalKey);

        var correlationId = CorrelationContext.Id
            ?? _httpContextAccessor.HttpContext?.Items[GreenCarWashHeaders.CorrelationId]?.ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation(GreenCarWashHeaders.CorrelationId, correlationId);

        return base.SendAsync(request, cancellationToken);
    }
}
