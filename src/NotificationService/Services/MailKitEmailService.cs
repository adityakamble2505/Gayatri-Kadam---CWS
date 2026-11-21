using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GreenCarWash.NotificationService.Services;

public interface ISmtpEmailService
{
    Task SendAsync(string toEmail, string subject, string html, CancellationToken cancellationToken);
}

public sealed class MailKitEmailService : ISmtpEmailService
{
    private readonly SmtpOptions _options;

    public MailKitEmailService(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(string toEmail, string subject, string html, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

        using var client = new SmtpClient();
        var socket = _options.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(_options.Host, _options.Port, socket, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_options.Username))
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
