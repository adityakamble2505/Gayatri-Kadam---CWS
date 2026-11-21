using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.PaymentService.Repositories;
using Microsoft.Extensions.Options;

namespace GreenCarWash.PaymentService.Services;

public sealed class PendingReaper : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PaymentOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<PendingReaper> _logger;

    public PendingReaper(
        IServiceScopeFactory scopes,
        IOptions<PaymentOptions> options,
        IClock clock,
        ILogger<PendingReaper> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AbandonAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Pending payment reaper failed");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<int> AbandonAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var payments = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var minutes = _options.PendingTimeoutMinutes < 1 ? 5 : _options.PendingTimeoutMinutes;
        var cutoff = _clock.UtcNow.AddMinutes(-minutes);
        var abandoned = await payments.AbandonPendingAsync(cutoff, _clock.UtcNow, cancellationToken);
        if (abandoned > 0)
            _logger.LogInformation("Marked {Count} pending payments as abandoned", abandoned);
        return abandoned;
    }
}
