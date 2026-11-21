using System.Collections.Concurrent;
using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.BuildingBlocks.Outbox;
using GreenCarWash.PaymentService.Data;
using GreenCarWash.PaymentService.Entities;
using GreenCarWash.PaymentService.Services;
using Microsoft.EntityFrameworkCore;

namespace GreenCarWash.PaymentService.Repositories;

public enum RefundDecision
{
    Applied,
    ExceedsBalance,
    NotRefundable,
    NotFound
}

public readonly record struct RefundResult(RefundDecision Decision, int RefundId);

public sealed class PaymentClaim
{
    public Payment Payment { get; init; } = null!;

    public bool Replay { get; init; }

    public bool ShouldCharge { get; init; }
}

public interface IPaymentRepository
{
    Task<PaymentClaim> ClaimAsync(
        int bookingId,
        int customerId,
        decimal amount,
        string cardBrand,
        string cardLast4,
        string idempotencyKey,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task FinishAsync(
        Payment payment,
        PaymentStatus status,
        string? transactionReference,
        string? failureReason,
        string eventType,
        int bookingId,
        string bookingNumber,
        int customerUserId,
        int? washerUserId,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<Payment?> FindByBookingAsync(int bookingId, CancellationToken cancellationToken);

    Task<Payment?> FindAsync(int paymentId, CancellationToken cancellationToken);

    Task<RefundResult> RefundAsync(
        int paymentId,
        decimal amount,
        string reason,
        int processedByUserId,
        int bookingId,
        string bookingNumber,
        int customerUserId,
        int? washerUserId,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<int> AbandonPendingAsync(DateTime cutoffUtc, DateTime utcNow, CancellationToken cancellationToken);
}

public sealed class PaymentRepository : IPaymentRepository
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> BookingGates = new();
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> RefundGates = new();

    private readonly PaymentDbContext _db;

    public PaymentRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<PaymentClaim> ClaimAsync(
        int bookingId,
        int customerId,
        decimal amount,
        string cardBrand,
        string cardLast4,
        string idempotencyKey,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var gate = BookingGates.GetOrAdd(bookingId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var byKey = await _db.Payments.FirstOrDefaultAsync(
                payment => payment.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (byKey is not null)
                return new PaymentClaim { Payment = byKey, Replay = true };

            var existing = await _db.Payments.FirstOrDefaultAsync(
                payment => payment.BookingId == bookingId,
                cancellationToken);
            if (existing is null)
            {
                var created = NewPending(bookingId, customerId, amount, cardBrand, cardLast4, idempotencyKey, utcNow);
                _db.Payments.Add(created);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception) when (DuplicateKey.Is(exception))
                {
                    _db.Entry(created).State = EntityState.Detached;
                    return await ResolveDuplicateAsync(bookingId, idempotencyKey, cancellationToken);
                }

                return new PaymentClaim { Payment = created, ShouldCharge = true };
            }

            if (existing.PaymentStatus == PaymentStatus.Failed)
            {
                var reclaimed = await ReclaimAsync(existing, customerId, amount, cardBrand, cardLast4, idempotencyKey, utcNow, cancellationToken);
                if (reclaimed is null)
                    throw new ConflictException("A payment for this booking is already in progress.");
                return new PaymentClaim { Payment = reclaimed, ShouldCharge = true };
            }

            throw ConflictFor(existing);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task FinishAsync(
        Payment payment,
        PaymentStatus status,
        string? transactionReference,
        string? failureReason,
        string eventType,
        int bookingId,
        string bookingNumber,
        int customerUserId,
        int? washerUserId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        async Task ApplyAsync()
        {
            payment.PaymentStatus = status;
            payment.TransactionReference = transactionReference;
            payment.FailureReason = failureReason;
            payment.PaymentDate = status == PaymentStatus.Success ? utcNow : null;
            payment.UpdatedAt = utcNow;
            _db.OutboxEvents.Add(PaymentEvent.Create(
                eventType,
                payment,
                bookingId,
                bookingNumber,
                customerUserId,
                washerUserId,
                payment.Amount,
                utcNow));
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (_db.Database.IsRelational())
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await ApplyAsync();
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await ApplyAsync();
    }

    public Task<Payment?> FindByBookingAsync(int bookingId, CancellationToken cancellationToken) =>
        _db.Payments.AsNoTracking().FirstOrDefaultAsync(payment => payment.BookingId == bookingId, cancellationToken);

    public Task<Payment?> FindAsync(int paymentId, CancellationToken cancellationToken) =>
        _db.Payments.AsNoTracking().FirstOrDefaultAsync(payment => payment.PaymentId == paymentId, cancellationToken);

    public async Task<RefundResult> RefundAsync(
        int paymentId,
        decimal amount,
        string reason,
        int processedByUserId,
        int bookingId,
        string bookingNumber,
        int customerUserId,
        int? washerUserId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var gate = RefundGates.GetOrAdd(paymentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_db.Database.IsRelational())
                return await RefundRelationalAsync(paymentId, amount, reason, processedByUserId, bookingId, bookingNumber, customerUserId, washerUserId, utcNow, cancellationToken);

            return await RefundMemoryAsync(paymentId, amount, reason, processedByUserId, bookingId, bookingNumber, customerUserId, washerUserId, utcNow, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> AbandonPendingAsync(DateTime cutoffUtc, DateTime utcNow, CancellationToken cancellationToken)
    {
        var stale = await _db.Payments
            .Where(payment => payment.PaymentStatus == PaymentStatus.Pending && payment.UpdatedAt <= cutoffUtc)
            .ToListAsync(cancellationToken);
        foreach (var payment in stale)
        {
            payment.PaymentStatus = PaymentStatus.Failed;
            payment.FailureReason = "Abandoned";
            payment.UpdatedAt = utcNow;
        }

        if (stale.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    private async Task<RefundResult> RefundMemoryAsync(
        int paymentId,
        decimal amount,
        string reason,
        int processedByUserId,
        int bookingId,
        string bookingNumber,
        int customerUserId,
        int? washerUserId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(row => row.PaymentId == paymentId, cancellationToken);
        if (payment is null)
            return new RefundResult(RefundDecision.NotFound, 0);
        if (payment.PaymentStatus is not (PaymentStatus.Success or PaymentStatus.PartiallyRefunded))
            return new RefundResult(RefundDecision.NotRefundable, 0);
        if (payment.RefundedAmount + amount > payment.Amount)
            return new RefundResult(RefundDecision.ExceedsBalance, 0);

        payment.RefundedAmount += amount;
        payment.PaymentStatus = payment.RefundedAmount == payment.Amount
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        payment.UpdatedAt = utcNow;
        var refund = AddRefund(payment, amount, reason, processedByUserId, bookingId, bookingNumber, customerUserId, washerUserId, utcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new RefundResult(RefundDecision.Applied, refund.RefundId);
    }

    private async Task<RefundResult> RefundRelationalAsync(
        int paymentId,
        decimal amount,
        string reason,
        int processedByUserId,
        int bookingId,
        string bookingNumber,
        int customerUserId,
        int? washerUserId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await ExecuteHotAsync(
            $"""
            UPDATE Payments
            SET PaymentStatus = CASE
                    WHEN RefundedAmount + {amount} = Amount THEN 'Refunded'
                    ELSE 'PartiallyRefunded'
                END,
                RefundedAmount = RefundedAmount + {amount},
                UpdatedAt = {utcNow}
            WHERE PaymentId = {paymentId}
              AND PaymentStatus IN ('Success', 'PartiallyRefunded')
              AND RefundedAmount + {amount} <= Amount
            """,
            cancellationToken);
        if (rows != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            var current = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(row => row.PaymentId == paymentId, cancellationToken);
            if (current is null)
                return new RefundResult(RefundDecision.NotFound, 0);
            if (current.PaymentStatus is not (PaymentStatus.Success or PaymentStatus.PartiallyRefunded))
                return new RefundResult(RefundDecision.NotRefundable, 0);
            return new RefundResult(RefundDecision.ExceedsBalance, 0);
        }

        var payment = await _db.Payments.FirstAsync(row => row.PaymentId == paymentId, cancellationToken);
        _db.Entry(payment).State = EntityState.Detached;
        payment = await _db.Payments.FirstAsync(row => row.PaymentId == paymentId, cancellationToken);
        var refund = AddRefund(payment, amount, reason, processedByUserId, bookingId, bookingNumber, customerUserId, washerUserId, utcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefundResult(RefundDecision.Applied, refund.RefundId);
    }

    private async Task<int> ExecuteHotAsync(FormattableString sql, CancellationToken cancellationToken)
    {
        var rows = 0;
        await SqlServerErrors.ExecuteOnceAsync(async () =>
        {
            rows = await _db.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
        });
        return rows;
    }

    private PaymentRefund AddRefund(
        Payment payment,
        decimal amount,
        string reason,
        int processedByUserId,
        int bookingId,
        string bookingNumber,
        int customerUserId,
        int? washerUserId,
        DateTime utcNow)
    {
        var refund = new PaymentRefund
        {
            PaymentId = payment.PaymentId,
            Amount = amount,
            Reason = reason,
            ProcessedByUserId = processedByUserId,
            CreatedAt = utcNow
        };
        _db.PaymentRefunds.Add(refund);
        _db.OutboxEvents.Add(PaymentEvent.Create(
            "PaymentRefunded",
            payment,
            bookingId,
            bookingNumber,
            customerUserId,
            washerUserId,
            amount,
            utcNow));
        return refund;
    }

    private async Task<Payment?> ReclaimAsync(
        Payment payment,
        int customerId,
        decimal amount,
        string cardBrand,
        string cardLast4,
        string idempotencyKey,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (await _db.Payments.AnyAsync(row => row.IdempotencyKey == idempotencyKey && row.PaymentId != payment.PaymentId, cancellationToken))
            throw new ConflictException("Idempotency key is already in use.");

        if (!_db.Database.IsRelational())
        {
            if (payment.PaymentStatus != PaymentStatus.Failed)
                return null;
            ApplyReclaim(payment, customerId, amount, cardBrand, cardLast4, idempotencyKey, utcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return payment;
        }

        var rows = await ExecuteHotAsync(
            $"""
            UPDATE Payments
            SET CustomerId = {customerId},
                Amount = {amount},
                CardBrand = {cardBrand},
                CardLast4 = {cardLast4},
                IdempotencyKey = {idempotencyKey},
                PaymentStatus = 'Pending',
                FailureReason = NULL,
                TransactionReference = NULL,
                PaymentDate = NULL,
                UpdatedAt = {utcNow}
            WHERE PaymentId = {payment.PaymentId}
              AND PaymentStatus = 'Failed'
            """,
            cancellationToken);
        if (rows != 1)
            return null;

        _db.Entry(payment).State = EntityState.Detached;
        return await _db.Payments.FirstAsync(row => row.PaymentId == payment.PaymentId, cancellationToken);
    }

    private static void ApplyReclaim(
        Payment payment,
        int customerId,
        decimal amount,
        string cardBrand,
        string cardLast4,
        string idempotencyKey,
        DateTime utcNow)
    {
        payment.CustomerId = customerId;
        payment.Amount = amount;
        payment.CardBrand = cardBrand;
        payment.CardLast4 = cardLast4;
        payment.IdempotencyKey = idempotencyKey;
        payment.PaymentStatus = PaymentStatus.Pending;
        payment.FailureReason = null;
        payment.TransactionReference = null;
        payment.PaymentDate = null;
        payment.UpdatedAt = utcNow;
    }

    private async Task<PaymentClaim> ResolveDuplicateAsync(int bookingId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var byKey = await _db.Payments.FirstOrDefaultAsync(payment => payment.IdempotencyKey == idempotencyKey, cancellationToken);
        if (byKey is not null)
            return new PaymentClaim { Payment = byKey, Replay = true };

        var existing = await _db.Payments.FirstOrDefaultAsync(payment => payment.BookingId == bookingId, cancellationToken)
            ?? throw new ConflictException("A payment for this booking is already in progress.");
        throw ConflictFor(existing);
    }

    private static ConflictException ConflictFor(Payment payment) =>
        payment.PaymentStatus switch
        {
            PaymentStatus.Pending => new ConflictException("A payment for this booking is already in progress."),
            PaymentStatus.Failed => new ConflictException("A payment for this booking is already in progress."),
            _ => new ConflictException("This booking has already been paid.")
        };

    private static Payment NewPending(
        int bookingId,
        int customerId,
        decimal amount,
        string cardBrand,
        string cardLast4,
        string idempotencyKey,
        DateTime utcNow) => new()
    {
        BookingId = bookingId,
        CustomerId = customerId,
        Amount = amount,
        Currency = "USD",
        PaymentMethod = PaymentMethod.Card,
        CardBrand = cardBrand,
        CardLast4 = cardLast4,
        PaymentStatus = PaymentStatus.Pending,
        IdempotencyKey = idempotencyKey,
        RefundedAmount = 0,
        CreatedAt = utcNow,
        UpdatedAt = utcNow
    };

}
