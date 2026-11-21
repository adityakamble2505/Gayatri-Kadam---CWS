using System.Globalization;
using GreenCarWash.NotificationService.DTOs;

namespace GreenCarWash.NotificationService.Services;

public sealed record NotificationDraft(
    int UserId,
    string Type,
    string Title,
    string Message,
    string ReferenceType,
    int ReferenceId,
    bool SendEmail,
    string? Template,
    IReadOnlyDictionary<string, string> Tokens);

public sealed class ReceiptDetails
{
    public string Package { get; init; } = string.Empty;

    public IReadOnlyList<string> AddOnLines { get; init; } = [];

    public decimal Total { get; init; }

    public string? PhotoUrl { get; init; }
}

public sealed class NotificationMapContext
{
    public IReadOnlyList<int> AdminUserIds { get; init; } = [];

    public string? CancelledBy { get; init; }

    public ReceiptDetails? Receipt { get; init; }
}

public static class EventNotificationMapper
{
    public const string Booking = "Booking";
    public const string WashRequest = "WashRequest";
    public const string Accepted = "Accepted";
    public const string Started = "Started";
    public const string Completed = "Completed";
    public const string Receipt = "Receipt";
    public const string Reminder = "Reminder2h";
    public const string Cancelled = "Cancelled";
    public const string Payment = "Payment";
    public const string AdminAlert = "AdminAlert";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "BookingCreated",
        "WashRequest",
        "WasherAccepted",
        "ServiceStarted",
        "ServiceCompleted",
        "PaymentSucceeded",
        "PaymentFailed",
        "PaymentRefunded",
        "Reminder2h",
        "BookingCancelled",
        "AdminAlert",
        "RefundPending"
    };

    public static bool IsKnown(string? eventType) =>
        !string.IsNullOrWhiteSpace(eventType) && Known.Contains(eventType);

    public static IReadOnlyList<NotificationDraft> Map(NotificationEventDto evt, NotificationMapContext context)
    {
        var number = evt.BookingNumber ?? evt.BookingId.ToString(CultureInfo.InvariantCulture);
        return evt.EventType switch
        {
            "BookingCreated" =>
            [
                Customer(evt, Booking, "Booking received", $"We received {number}.", true, "booking-received", number)
            ],
            "WashRequest" => WasherOrEmpty(evt, WashRequest, "New wash request", $"Wash request for {number}.", false, null, number),
            "WasherAccepted" =>
            [
                Customer(evt, Accepted, "Washer accepted your request", $"A washer accepted {number}.", true, "washer-accepted", number)
            ],
            "ServiceStarted" =>
            [
                Customer(evt, Started, "Service started", $"Your wash {number} has started.", true, "service-started", number)
            ],
            "ServiceCompleted" =>
            [
                Customer(evt, Completed, "Service completed", $"Your wash {number} is complete.", true, "service-completed", number)
            ],
            "PaymentSucceeded" => PaymentSucceeded(evt, context, number),
            "PaymentFailed" =>
            [
                Customer(evt, Payment, "Payment failed", $"Payment for {number} was declined.", true, "payment-failed", number, PaymentReference(evt))
            ],
            "PaymentRefunded" =>
            [
                Customer(evt, Payment, "Payment refunded", RefundMessage(evt, number), true, "payment-refunded", number, PaymentReference(evt))
            ],
            "Reminder2h" => WasherOrEmpty(evt, Reminder, "Wash in two hours", $"You have a wash for {number} in about two hours.", true, "reminder", number),
            "BookingCancelled" => CancelledDrafts(evt, context, number),
            "AdminAlert" or "RefundPending" => AdminDrafts(evt, context, number),
            _ => []
        };
    }

    private static IReadOnlyList<NotificationDraft> PaymentSucceeded(NotificationEventDto evt, NotificationMapContext context, string number)
    {
        var receipt = context.Receipt;
        var lines = receipt is null ? string.Empty : string.Join("; ", receipt.AddOnLines);
        var drafts = new List<NotificationDraft>
        {
            new(
                evt.CustomerUserId,
                Receipt,
                "Receipt",
                $"Receipt for {number}.",
                "Booking",
                evt.BookingId,
                true,
                "receipt",
                Tokens(number, evt, extra: new Dictionary<string, string>
                {
                    ["package"] = receipt?.Package ?? string.Empty,
                    ["addOns"] = lines,
                    ["total"] = (receipt?.Total ?? evt.Amount ?? 0m).ToString("0.00", CultureInfo.InvariantCulture),
                    ["photoUrl"] = receipt?.PhotoUrl ?? string.Empty
                }))
        };
        if (evt.WasherUserId is int washerId)
        {
            drafts.Add(new NotificationDraft(
                washerId,
                Payment,
                "Payment received",
                $"Payment received for {number}.",
                "Payment",
                PaymentReference(evt),
                false,
                null,
                Tokens(number, evt)));
        }

        return drafts;
    }

    private static IReadOnlyList<NotificationDraft> CancelledDrafts(NotificationEventDto evt, NotificationMapContext context, string number)
    {
        var actor = context.CancelledBy ?? string.Empty;
        var message = $"{number} was cancelled.";
        var drafts = new List<NotificationDraft>();
        var notifyCustomer = actor.Equals("Washer", StringComparison.OrdinalIgnoreCase)
            || actor.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || actor.Equals("System", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(actor);
        var notifyWasher = actor.Equals("Customer", StringComparison.OrdinalIgnoreCase)
            || actor.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        if (notifyCustomer)
            drafts.Add(Customer(evt, Cancelled, "Booking cancelled", message, true, "cancelled", number));
        if (notifyWasher && evt.WasherUserId is int washerId)
        {
            drafts.Add(new NotificationDraft(
                washerId,
                Cancelled,
                "Booking cancelled",
                message,
                "Booking",
                evt.BookingId,
                true,
                "cancelled",
                Tokens(number, evt)));
        }

        return drafts;
    }

    private static IReadOnlyList<NotificationDraft> AdminDrafts(NotificationEventDto evt, NotificationMapContext context, string number)
    {
        var title = evt.EventType == "RefundPending" ? "Refund pending" : "Admin alert";
        var message = string.IsNullOrWhiteSpace(evt.Reason) ? $"{title} for {number}." : evt.Reason!.Trim();
        return context.AdminUserIds
            .Where(id => id > 0)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                AdminAlert,
                title,
                message,
                "Booking",
                evt.BookingId,
                true,
                "admin-alert",
                Tokens(number, evt, extra: new Dictionary<string, string> { ["reason"] = message })))
            .ToArray();
    }

    private static IReadOnlyList<NotificationDraft> WasherOrEmpty(
        NotificationEventDto evt,
        string type,
        string title,
        string message,
        bool email,
        string? template,
        string number)
    {
        if (evt.WasherUserId is not int washerId)
            return [];
        return
        [
            new NotificationDraft(washerId, type, title, message, "Booking", evt.BookingId, email, template, Tokens(number, evt))
        ];
    }

    private static NotificationDraft Customer(
        NotificationEventDto evt,
        string type,
        string title,
        string message,
        bool email,
        string template,
        string number,
        int? referenceId = null) =>
        new(
            evt.CustomerUserId,
            type,
            title,
            message,
            referenceId is null ? "Booking" : "Payment",
            referenceId ?? evt.BookingId,
            email,
            template,
            Tokens(number, evt));

    private static int PaymentReference(NotificationEventDto evt) =>
        int.TryParse(evt.ReferenceId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : evt.BookingId;

    private static string RefundMessage(NotificationEventDto evt, string number)
    {
        var amount = evt.Amount?.ToString("0.00", CultureInfo.InvariantCulture);
        return amount is null ? $"A refund was issued for {number}." : $"A refund of {amount} was issued for {number}.";
    }

    private static Dictionary<string, string> Tokens(
        string number,
        NotificationEventDto evt,
        Dictionary<string, string>? extra = null)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bookingNumber"] = number,
            ["status"] = evt.Status ?? string.Empty,
            ["amount"] = evt.Amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty
        };
        if (extra is not null)
        {
            foreach (var pair in extra)
                tokens[pair.Key] = pair.Value;
        }

        return tokens;
    }
}
