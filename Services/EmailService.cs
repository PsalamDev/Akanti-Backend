using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Akanti.API.Services;

/// <summary>
/// Sends transactional email through an HTTP API (Resend/Brevo/SendGrid), falling back
/// to SMTP when no HTTP provider is configured.
///
/// HTTP is the default because hosted platforms such as Render block outbound SMTP:
/// connecting to smtp.gmail.com times out, which no port or credential change can fix.
/// SMTP remains available for local development where HTTP provider keys are absent.
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public EmailService(
        IConfiguration configuration,
        ILogger<EmailService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var provider = (_configuration["Email:Provider"] ?? "").Trim().ToLowerInvariant();

        switch (provider)
        {
            case "resend":
                await SendViaResendAsync(to, subject, htmlBody);
                break;
            case "brevo":
                await SendViaBrevoAsync(to, subject, htmlBody);
                break;
            case "sendgrid":
                await SendViaSendGridAsync(to, subject, htmlBody);
                break;
            case "":
                await SendViaSmtpAsync(to, subject, htmlBody);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown Email:Provider '{provider}'. Use resend, brevo, sendgrid, or leave empty for SMTP.");
        }
    }

    private string FromName => _configuration["Email:FromName"] ?? _configuration["Smtp:FromName"] ?? "Akanti";

    private string FromAddress =>
        _configuration["Email:FromEmail"]
        ?? _configuration["Smtp:FromEmail"]
        ?? _configuration["Smtp:Username"]
        ?? "";

    // ---------------------------------------------------------------------
    // HTTP providers
    // ---------------------------------------------------------------------

    private async Task SendViaResendAsync(string to, string subject, string htmlBody)
    {
        var apiKey = Require("Email:ApiKey", "Resend");
        var from = RequireFromAddress("Resend");

        var payload = new
        {
            from = $"{FromName} <{from}>",
            to = new[] { to },
            subject,
            html = htmlBody
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = JsonContent(payload);

        await SendAsync(req, "Resend", to, subject);
    }

    private async Task SendViaBrevoAsync(string to, string subject, string htmlBody)
    {
        var apiKey = Require("Email:ApiKey", "Brevo");
        var from = RequireFromAddress("Brevo");

        var payload = new
        {
            sender = new { name = FromName, email = from },
            to = new[] { new { email = to } },
            subject,
            htmlContent = htmlBody
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        req.Headers.TryAddWithoutValidation("api-key", apiKey);
        req.Content = JsonContent(payload);

        await SendAsync(req, "Brevo", to, subject);
    }

    private async Task SendViaSendGridAsync(string to, string subject, string htmlBody)
    {
        var apiKey = Require("Email:ApiKey", "SendGrid");
        var from = RequireFromAddress("SendGrid");

        var payload = new
        {
            personalizations = new[] { new { to = new[] { new { email = to } } } },
            from = new { email = from, name = FromName },
            subject,
            content = new[] { new { type = "text/html", value = htmlBody } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = JsonContent(payload);

        await SendAsync(req, "SendGrid", to, subject);
    }

    private async Task SendAsync(HttpRequestMessage req, string providerName, string to, string subject)
    {
        var client = _httpClientFactory.CreateClient("EmailApi");

        try
        {
            var response = await client.SendAsync(req);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent to {To} via {Provider}: {Subject}", to, providerName, subject);
                return;
            }

            // Not swallowed: a failed send must be visible, because the API still tells
            // the user to check their inbox.
            _logger.LogError(
                "Failed to send email to {To} via {Provider}. Status {Status}. Response: {Body}",
                to, providerName, (int)response.StatusCode, Truncate(body, 500));

            // Include the provider's own message: it is the part that says *why*
            // (unverified sender, disabled account, bad key) rather than just the code.
            var detail = Truncate(body, 300);
            throw new InvalidOperationException(
                $"{providerName} rejected the email ({(int)response.StatusCode}). {detail}");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error sending email to {To} via {Provider}", to, providerName);
            throw new InvalidOperationException($"{providerName} is unreachable: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout sending email to {To} via {Provider}", to, providerName);
            throw new InvalidOperationException($"{providerName} timed out.", ex);
        }
    }

    // ---------------------------------------------------------------------
    // SMTP fallback (local development)
    // ---------------------------------------------------------------------

    private async Task SendViaSmtpAsync(string to, string subject, string htmlBody)
    {
        var host = _configuration["Smtp:Host"];
        var port = int.Parse(_configuration["Smtp:Port"] ?? "465");
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var fromEmail = FromAddress;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("SMTP not configured and no Email:Provider set. Email to {To} not sent.", to);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(FromName, fromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();

        // 465 is implicit TLS; 587 and others need STARTTLS. Hardcoding SslOnConnect
        // made every non-465 port fail.
        var socketOptions = port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        try
        {
            await client.ConnectAsync(host, port, socketOptions);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Email sent to {To} via SMTP: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To} via SMTP", to);
            throw;
        }
    }

    // ---------------------------------------------------------------------

    private string Require(string key, string providerName) =>
        string.IsNullOrWhiteSpace(_configuration[key])
            ? throw new InvalidOperationException($"Email:Provider is '{providerName.ToLowerInvariant()}' but {key} is not set.")
            : _configuration[key]!;

    private string RequireFromAddress(string providerName) =>
        string.IsNullOrWhiteSpace(FromAddress)
            ? throw new InvalidOperationException($"{providerName} requires Email:FromEmail (or Smtp:FromEmail) to be set.")
            : FromAddress;

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max]);
}
