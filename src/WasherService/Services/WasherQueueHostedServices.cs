using GreenCarWash.WasherService.Queue;
using GreenCarWash.WasherService.Repositories;
using GreenCarWash.BuildingBlocks.Time;

namespace GreenCarWash.WasherService.Services;

public sealed class WasherQueueRebuild
{
    private readonly IWasherRepository _washers;
    private readonly IWasherQueue _queue;
    private readonly IClock _clock;
    private readonly ILogger<WasherQueueRebuild> _logger;

    public WasherQueueRebuild(
        IWasherRepository washers,
        IWasherQueue queue,
        IClock clock,
        ILogger<WasherQueueRebuild> logger)
    {
        _washers = washers;
        _queue = queue;
        _clock = clock;
        _logger = logger;
    }

    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        var reset = await _washers.ResetStaleAssignmentsAsync(_clock.UtcNow, cancellationToken);
        var candidates = await _washers.ListQueueCandidatesAsync(cancellationToken);
        foreach (var washer in candidates)
            _queue.Enqueue(WasherQueueSync.ToEntry(washer));
        _logger.LogInformation(
            "Rebuilt washer queue. Stale assignments reset: {Reset}. Washers queued: {Queued}",
            reset,
            candidates.Count);
    }
}

public sealed class QueueRebuildHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly QueueLifetime _lifetime;

    public QueueRebuildHostedService(IServiceScopeFactory scopes, QueueLifetime lifetime)
    {
        _scopes = scopes;
        _lifetime = lifetime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<WasherQueueRebuild>();
        await rebuild.RebuildAsync(cancellationToken);
        _lifetime.MarkReady();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class LeaseReaper : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly QueueLifetime _lifetime;
    private readonly ILogger<LeaseReaper> _logger;

    public LeaseReaper(IServiceScopeFactory scopes, QueueLifetime lifetime, ILogger<LeaseReaper> logger)
    {
        _scopes = scopes;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _lifetime.Ready.WaitAsync(stoppingToken);
        await SweepAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SweepAsync(stoppingToken);
    }

    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var dispatch = scope.ServiceProvider.GetRequiredService<IWasherDispatchService>();
            await dispatch.SweepExpiredOffersAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Washer offer lease sweep failed");
        }
    }
}
