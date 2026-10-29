using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace TranXuanThanh_2380614806_N7_API.Services;

public interface IEmailSender
{
    Task SendOtpAsync(string to, string code, CancellationToken cancellationToken);
    Task SendPasswordResetOtpAsync(string to, string code, CancellationToken cancellationToken);
}

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendOtpAsync(string to, string code, CancellationToken cancellationToken)
    {
        await SendAsync(
            to,
            "Mã xác minh đăng nhập – Chấm công Nhóm 7",
            $"Mã OTP của bạn là {code}. Mã hết hạn sau 5 phút và chỉ dùng một lần. Nếu bạn không yêu cầu mã này, hãy bỏ qua email.",
            cancellationToken);
    }

    public async Task SendPasswordResetOtpAsync(string to, string code, CancellationToken cancellationToken)
    {
        await SendAsync(
            to,
            "Mã đặt lại mật khẩu – Chấm công Nhóm 7",
            $"Mã OTP đặt lại mật khẩu của bạn là {code}. Mã hết hạn sau 5 phút và chỉ dùng một lần. Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email.",
            cancellationToken);
    }

    private async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        var host = configuration["Smtp:Host"];
        var username = configuration["Smtp:Username"];
        var password = configuration["Smtp:Password"]?.Replace(" ", "").Trim();
        var from = configuration["Smtp:From"] ?? username;
        if (!string.Equals(host, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(configuration["Smtp:Port"], out var port) || port != 587 ||
            !string.Equals(username, "zbin789x@gmail.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(from, username, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "SMTP cần dùng smtp.gmail.com:587, STARTTLS và Gmail gửi đã cấu hình trong User Secrets hoặc Environment Variables.");
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from!));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain")
        {
            Text = body
        };

        using var client = new SmtpClient();
        client.Timeout = 20_000;
        await client.ConnectAsync(host!, port, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(username!, password!, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
