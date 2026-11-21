using FluentValidation;
using GreenCarWash.BuildingBlocks.Api;
using GreenCarWash.BuildingBlocks.Correlation;
using GreenCarWash.BuildingBlocks.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GreenCarWash.BuildingBlocks.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client closed the request. There is no body left to write.
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(exception, "Unhandled exception after the response started");
                throw;
            }

            var described = Describe(exception);
            if (described.Status >= StatusCodes.Status500InternalServerError)
            {
                logger.LogError(
                    exception,
                    "Unhandled exception. CorrelationId: {CorrelationId}",
                    CorrelationContext.Id);
            }
            else
            {
                logger.LogWarning(
                    "Request failed with status {StatusCode}. CorrelationId: {CorrelationId}",
                    described.Status,
                    CorrelationContext.Id);
            }

            Dictionary<string, object?>? extensions = null;
            if (described.Errors is { Count: > 0 })
            {
                extensions = new Dictionary<string, object?>
                {
                    ["errors"] = described.Errors
                };
            }

            var problem = ApiProblems.Create(
                context,
                described.Status,
                described.Title,
                described.Detail,
                extensions);

            await Results.Problem(problem).ExecuteAsync(context);
        }
    }

    private static (int Status, string Title, string Detail, IReadOnlyList<string>? Errors) Describe(Exception exception)
    {
        switch (exception)
        {
            case ValidationException validation:
                var errors = validation.Errors
                    .Select(error => error.ErrorMessage)
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return (StatusCodes.Status400BadRequest, "Validation failed", "One or more validation errors occurred.", errors);

            case BadHttpRequestException badRequest:
                return (StatusCodes.Status400BadRequest, "Bad request", badRequest.Message, null);

            case UnauthorizedException unauthorized:
                return (StatusCodes.Status401Unauthorized, "Unauthorized", unauthorized.Message, null);

            case UnauthorizedAccessException access:
                return (StatusCodes.Status401Unauthorized, "Unauthorized", access.Message, null);

            case ForbiddenException forbidden:
                return (StatusCodes.Status403Forbidden, "Forbidden", forbidden.Message, null);

            case NotFoundException notFound:
                return (StatusCodes.Status404NotFound, "Not found", notFound.Message, null);

            case ConflictException conflict:
                return (StatusCodes.Status409Conflict, "Conflict", conflict.Message, null);

            case BadGatewayException badGateway:
                return (StatusCodes.Status502BadGateway, "Bad gateway", badGateway.Message, null);

            case GatewayTimeoutException timeout:
                return (StatusCodes.Status504GatewayTimeout, "Gateway timeout", timeout.Message, null);

            case ServiceUnavailableException unavailable:
                return (StatusCodes.Status503ServiceUnavailable, "Service unavailable", unavailable.Message, null);

            default:
                return (
                    StatusCodes.Status500InternalServerError,
                    "Internal server error",
                    "An unexpected error occurred.",
                    null);
        }
    }
}
