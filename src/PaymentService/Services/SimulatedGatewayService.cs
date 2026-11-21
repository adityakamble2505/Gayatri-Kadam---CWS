namespace GreenCarWash.PaymentService.Services;

public sealed class GatewayResult
{
    public bool Approved { get; init; }

    public bool TimedOut { get; init; }

    public string? Reason { get; init; }

    public string? TransactionReference { get; init; }

    public static GatewayResult Approve(string reference) => new()
    {
        Approved = true,
        TransactionReference = reference
    };

    public static GatewayResult Decline(string reason) => new()
    {
        Reason = reason
    };

    public static GatewayResult Timeout() => new()
    {
        TimedOut = true,
        Reason = "Gateway timed out"
    };
}

public interface IPaymentGateway
{
    Task<GatewayResult> ChargeAsync(string cardLast4, CancellationToken cancellationToken);
}

public interface IGatewayDelay
{
    Task WaitAsync(CancellationToken cancellationToken);
}

public sealed class ConfiguredGatewayDelay : IGatewayDelay
{
    private readonly PaymentOptions _options;

    public ConfiguredGatewayDelay(Microsoft.Extensions.Options.IOptions<PaymentOptions> options)
    {
        _options = options.Value;
    }

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        var min = Math.Max(0, _options.GatewayMinDelayMs);
        var max = Math.Max(min, _options.GatewayMaxDelayMs);
        var delay = min == max ? min : Random.Shared.Next(min, max + 1);
        return delay == 0 ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
    }
}

public sealed class SimulatedGatewayService : IPaymentGateway
{
    private readonly IGatewayDelay _delay;

    public SimulatedGatewayService(IGatewayDelay delay)
    {
        _delay = delay;
    }

    public async Task<GatewayResult> ChargeAsync(string cardLast4, CancellationToken cancellationToken)
    {
        await _delay.WaitAsync(cancellationToken);
        if (cardLast4 == "9999")
            return GatewayResult.Timeout();
        if (cardLast4 == "0002")
            return GatewayResult.Decline("Card declined");
        if (cardLast4 == "1111")
            return GatewayResult.Approve("SIM" + Guid.NewGuid().ToString("N"));

        return GatewayResult.Decline("Card declined");
    }
}
