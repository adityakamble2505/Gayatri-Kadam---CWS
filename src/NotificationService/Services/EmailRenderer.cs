using System.Net;
using System.Reflection;
using System.Text;

namespace GreenCarWash.NotificationService.Services;

public static class EmailAddressMask
{
    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "***";
        var at = email.IndexOf('@');
        if (at <= 0)
            return "***";
        return email[0] + "***" + email[at..];
    }
}

public static class EmailRenderer
{
    public static string Render(string templateName, IReadOnlyDictionary<string, string> tokens)
    {
        var encoded = tokens.ToDictionary(
            pair => pair.Key,
            pair => WebUtility.HtmlEncode(pair.Value ?? string.Empty),
            StringComparer.Ordinal);
        var inner = Apply(Read(templateName + ".html"), encoded);
        return Apply(Read("layout.html"), new Dictionary<string, string> { ["content"] = inner });
    }

    public static string Compose(NotificationDraft draft, string name)
    {
        var tokens = new Dictionary<string, string>(draft.Tokens, StringComparer.Ordinal)
        {
            ["name"] = name,
            ["message"] = draft.Message
        };
        return Render(draft.Template ?? "plain", tokens);
    }

    private static string Apply(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var builder = new StringBuilder(template);
        foreach (var pair in tokens)
            builder.Replace("{{" + pair.Key + "}}", pair.Value);
        return builder.ToString();
    }

    private static string Read(string fileName)
    {
        var assembly = typeof(EmailRenderer).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(resource => resource.EndsWith("." + fileName, StringComparison.Ordinal));
        if (name is null)
            throw new InvalidOperationException($"Email template '{fileName}' is missing.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public interface IEmailContentStore
{
    void Put(long emailLogId, string html);

    bool TryTake(long emailLogId, out string html);

    void Remove(long emailLogId);
}

public sealed class EmailContentStore : IEmailContentStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, string> _bodies = new();

    public void Put(long emailLogId, string html) => _bodies[emailLogId] = html;

    public bool TryTake(long emailLogId, out string html) => _bodies.TryGetValue(emailLogId, out html!);

    public void Remove(long emailLogId) => _bodies.TryRemove(emailLogId, out _);
}
