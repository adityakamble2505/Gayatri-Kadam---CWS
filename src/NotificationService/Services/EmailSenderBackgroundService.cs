using GreenCarWash.BuildingBlocks.Time;
using GreenCarWash.NotificationService.Repositories;
using Microsoft.Extensions.Options;

namespace GreenCarWash.NotificationService.Services;

public sealed class EmailSenderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly NotificationOptions _options;
    private readonly ILogger<EmailSenderBackgroundService> _logger;

    public EmailSenderBackgroundService(
        IServiceScopeFactory scopes,
        IClock clock,
        IOptions<NotificationOptions> options,
        ILogger<EmailSenderBackgroundService> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Email sender failed");
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

    public async Task<int> DeliverAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var smtp = scope.ServiceProvider.GetRequiredService<ISmtpEmailService>();
        var max = _options.MaxEmailAttempts < 1 ? 5 : _options.MaxEmailAttempts;
        return await notifications.DeliverBatchAsync(smtp, _logger, _clock.UtcNow, max, cancellationToken);
    }
}
