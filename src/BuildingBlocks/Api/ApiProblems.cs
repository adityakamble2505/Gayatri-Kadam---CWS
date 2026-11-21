using GreenCarWash.BuildingBlocks.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.BuildingBlocks.Api;

public static class ApiProblems
{
    public static ProblemDetails Create(
        HttpContext httpContext,
        int statusCode,
        string title,
        string? detail,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path.Value,
            Type = $"https://httpstatuses.com/{statusCode}"
        };

        if (httpContext.Items[GreenCarWashHeaders.CorrelationId] is string correlationId
            && !string.IsNullOrWhiteSpace(correlationId))
        {
            problem.Extensions["correlationId"] = correlationId;
        }

        if (extensions is null)
            return problem;

        foreach (var (key, value) in extensions)
            problem.Extensions[key] = value;

        return problem;
    }
}
