using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GreenCarWash.BuildingBlocks.Data;
using GreenCarWash.BuildingBlocks.Exceptions;
using GreenCarWash.NotificationService.Data;
using GreenCarWash.NotificationService.Entities;
using GreenCarWash.NotificationService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GreenCarWash.NotificationService.Repositories;

public interface INotificationRepository
{
    Task<bool> AddOnceAsync(
        Notification notification,
        EmailLog? email,
        string? html,
        CancellationToken cancellationToken);

    Task AddEmailAsync(EmailLog email, string html, CancellationToken cancellationToken);

    Task<int> DeliverBatchAsync(
        ISmtpEmailService smtp,
        ILogger logger,
        DateTime utcNow,
        int maxAttempts,
        CancellationToken cancellationToken);

    Task<Notification?> FindAsync(long notificationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Notification>> ListAsync(int userId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken);

    Task<int> CountAsync(int userId, bool unreadOnly, CancellationToken cancellationToken);

    Task<int> UnreadCountAsync(int userId, CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(long notificationId, int userId, DateTime utcNow, CancellationToken cancellationToken);

    Task<int> MarkAllReadAsync(int userId, DateTime utcNow, CancellationToken cancellationToken);

    Task<(IReadOnlyList<EmailLog> Items, int Total)> EmailLogsAsync(string? status, int skip, int take, CancellationToken cancellationToken);

    Task<EmailLog?> RetryEmailAsync(long emailLogId, CancellationToken cancellationToken);
}

public sealed class NotificationRepository : INotificationRepository
{
    private const int BatchSize = 20;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InsertGates = new();
    private static readonly SemaphoreSlim EmailGate = new(1, 1);

    private readonly NotificationDbContext _db;
    private readonly IEmailContentStore _bodies;

    public NotificationRepository(NotificationDbContext db, IEmailContentStore bodies)
    {
        _db = db;
        _bodies = bodies;
    }

    public async Task<bool> AddOnceAsync(Notification notification, EmailLog? email, string? html, CancellationToken cancellationToken)
    {
        var gate = InsertGates.GetOrAdd(LockName(notification), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_db.Database.IsRelational())
                return await AddRelationalAsync(notification, email, html, cancellationToken);
            return await AddMemoryAsync(notification, email, html, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AddEmailAsync(EmailLog email, string html, CancellationToken cancellationToken)
    {
        _db.EmailLogs.Add(email);
        await _db.SaveChangesAsync(cancellationToken);
        _bodies.Put(email.EmailLogId, html);
    }

    public async Task<int> DeliverBatchAsync(
        ISmtpEmailService smtp,
        ILogger logger,
        DateTime utcNow,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
        {
            await EmailGate.WaitAsync(cancellationToken);
            try
            {
                return await DeliverCoreAsync(smtp, logger, utcNow, maxAttempts, cancellationToken);
            }
            finally
            {
                EmailGate.Release();
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var ids = await ClaimRelationalIdsAsync(utcNow, maxAttempts, cancellationToken);
        var sent = 0;
        foreach (var id in ids)
        {
            var row = await _db.EmailLogs.FirstAsync(email => email.EmailLogId == id, cancellationToken);
            if (await TrySendAsync(row, smtp, logger, utcNow, maxAttempts, cancellationToken))
                sent++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sent;
    }

    public Task<Notification?> FindAsync(long notificationId, CancellationToken cancellationToken) =>
        _db.Notifications.AsNoTracking().FirstOrDefaultAsync(row => row.NotificationId == notificationId, cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListAsync(int userId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken)
    {
        var query = _db.Notifications.AsNoTracking().Where(row => row.UserId == userId);
        if (unreadOnly)
            query = query.Where(row => !row.IsRead);
        return await query.OrderByDescending(row => row.CreatedAt).Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(int userId, bool unreadOnly, CancellationToken cancellationToken)
    {
        var query = _db.Notifications.AsNoTracking().Where(row => row.UserId == userId);
        if (unreadOnly)
            query = query.Where(row => !row.IsRead);
        return query.CountAsync(cancellationToken);
    }

    public Task<int> UnreadCountAsync(int userId, CancellationToken cancellationToken) =>
        _db.Notifications.AsNoTracking().CountAsync(row => row.UserId == userId && !row.IsRead, cancellationToken);

    public async Task<bool> MarkReadAsync(long notificationId, int userId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var row = await _db.Notifications.FirstOrDefaultAsync(
            notification => notification.NotificationId == notificationId && notification.UserId == userId,
            cancellationToken);
        if (row is null)
            return false;
        if (!row.IsRead)
        {
            row.IsRead = true;
            row.ReadAt = utcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<int> MarkAllReadAsync(int userId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var rows = await _db.Notifications.Where(row => row.UserId == userId && !row.IsRead).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.IsRead = true;
            row.ReadAt = utcNow;
        }

        if (rows.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<(IReadOnlyList<EmailLog> Items, int Total)> EmailLogsAsync(string? status, int skip, int take, CancellationToken cancellationToken)
    {
        var query = _db.EmailLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EmailStatus>(status, true, out var parsed))
            query = query.Where(row => row.Status == parsed);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(row => row.CreatedAt).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<EmailLog?> RetryEmailAsync(long emailLogId, CancellationToken cancellationToken)
    {
        var row = await _db.EmailLogs.FirstOrDefaultAsync(email => email.EmailLogId == emailLogId, cancellationToken);
        if (row is null)
            return null;
        if (row.Status != EmailStatus.Failed)
            throw new ConflictException("Only a failed email can be retried.");
        row.Status = EmailStatus.Queued;
        row.Attempts = 0;
        row.LastError = null;
        row.SentAt = null;
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private async Task<bool> AddMemoryAsync(Notification notification, EmailLog? email, string? html, CancellationToken cancellationToken)
    {
        if (await ExistsAsync(notification, cancellationToken))
            return false;
        _db.Notifications.Add(notification);
        if (email is not null)
            _db.EmailLogs.Add(email);
        await _db.SaveChangesAsync(cancellationToken);
        if (email is not null && html is not null)
            _bodies.Put(email.EmailLogId, html);
        return true;
    }

    private async Task<bool> AddRelationalAsync(Notification notification, EmailLog? email, string? html, CancellationToken cancellationToken)
    {
        await _db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            if (!await LockAsync(LockName(notification), cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new ServiceUnavailableException("Notification lock timed out.");
            }

            if (await ExistsAsync(notification, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            _db.Notifications.Add(notification);
            if (email is not null)
                _db.EmailLogs.Add(email);
            try
            {
                await SqlServerErrors.ExecuteOnceAsync(() => _db.SaveChangesAsync(cancellationToken));
            }
            catch (DbUpdateException exception) when (SqlServerErrors.IsDuplicateKey(exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                return false;
            }

            if (email is not null && html is not null)
                _bodies.Put(email.EmailLogId, html);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private async Task<int> DeliverCoreAsync(
        ISmtpEmailService smtp,
        ILogger logger,
        DateTime utcNow,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var rows = await _db.EmailLogs
            .Where(row => row.Status == EmailStatus.Queued && row.Attempts < maxAttempts)
            .OrderBy(row => row.EmailLogId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var row in rows.Where(row => Eligible(row, utcNow)))
        {
            if (await TrySendAsync(row, smtp, logger, utcNow, maxAttempts, cancellationToken))
                sent++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return sent;
    }

    private async Task<List<long>> ClaimRelationalIdsAsync(DateTime utcNow, int maxAttempts, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH cte AS (
                SELECT TOP (20) EmailLogId, Attempts
                FROM EmailLogs WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status = N'Queued'
                  AND Attempts < @maxAttempts
                  AND (
                        Attempts = 0
                        OR DATEADD(
                            minute,
                            CASE
                                WHEN Attempts >= 6 THEN 30
                                ELSE CAST(POWER(CAST(2 AS float), Attempts - 1) AS int)
                            END,
                            CreatedAt) <= @now
                      )
                ORDER BY EmailLogId
            )
            UPDATE cte
            SET Attempts = Attempts
            OUTPUT inserted.EmailLogId
            """;
        var max = command.CreateParameter();
        max.ParameterName = "@maxAttempts";
        max.Value = maxAttempts;
        command.Parameters.Add(max);
        var now = command.CreateParameter();
        now.ParameterName = "@now";
        now.Value = utcNow;
        command.Parameters.Add(now);
        var ids = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(reader.GetInt64(0));
        return ids;
    }

    private async Task<bool> TrySendAsync(
        EmailLog row,
        ISmtpEmailService smtp,
        ILogger logger,
        DateTime utcNow,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var masked = EmailAddressMask.Mask(row.ToEmail);
        if (!_bodies.TryTake(row.EmailLogId, out var html))
        {
            row.Status = EmailStatus.Failed;
            row.LastError = Trim("Email content is no longer available.");
            logger.LogWarning("Skipped email {EmailLogId} to {Recipient}", row.EmailLogId, masked);
            return false;
        }

        try
        {
            await smtp.SendAsync(row.ToEmail, row.Subject, html, cancellationToken);
            row.Status = EmailStatus.Sent;
            row.SentAt = utcNow;
            row.LastError = null;
            _bodies.Remove(row.EmailLogId);
            logger.LogInformation("Sent email {EmailLogId} to {Recipient}", row.EmailLogId, masked);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            row.Attempts += 1;
            row.LastError = Trim(exception.Message);
            if (row.Attempts >= maxAttempts)
                row.Status = EmailStatus.Failed;
            _bodies.Put(row.EmailLogId, html);
            logger.LogWarning("Email {EmailLogId} to {Recipient} was not sent", row.EmailLogId, masked);
            return false;
        }
    }

    private Task<bool> ExistsAsync(Notification notification, CancellationToken cancellationToken) =>
        _db.Notifications.AnyAsync(
            row => row.UserId == notification.UserId
                && row.Type == notification.Type
                && row.ReferenceType == notification.ReferenceType
                && row.ReferenceId == notification.ReferenceId,
            cancellationToken);

    private async Task<bool> LockAsync(string name, CancellationToken cancellationToken)
    {
        var result = await ScalarAsync(
            """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @name,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 2000;
            SELECT @result;
            """,
            name,
            cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) >= 0;
    }

    private async Task<object?> ScalarAsync(string sql, string name, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = name;
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static bool Eligible(EmailLog row, DateTime utcNow) =>
        row.CreatedAt.Add(Backoff(row.Attempts)) <= utcNow;

    public static TimeSpan Backoff(int attempts)
    {
        if (attempts <= 0)
            return TimeSpan.Zero;
        var minutes = Math.Min(30d, Math.Pow(2, attempts - 1));
        return TimeSpan.FromMinutes(minutes);
    }

    private static string LockName(Notification notification)
    {
        var raw = string.Join('|', notification.Type, notification.ReferenceType, notification.ReferenceId?.ToString(CultureInfo.InvariantCulture), notification.UserId.ToString(CultureInfo.InvariantCulture));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return "notif-" + hash[..32];
    }

    private static string Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Send failed.";
        return value.Length <= 500 ? value : value[..500];
    }
}
