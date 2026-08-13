using System.Threading.Channels;

namespace Akanti.API.Services;

public interface IBackgroundEmailQueue
{
    ValueTask EnqueueAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

public class BackgroundEmailQueue : IBackgroundEmailQueue
{
    private readonly Channel<(string To, string Subject, string HtmlBody)> _channel =
        Channel.CreateBounded<(string, string, string)>(
            new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

    public ValueTask EnqueueAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync((to, subject, htmlBody), cancellationToken);

    public ChannelReader<(string To, string Subject, string HtmlBody)> Reader => _channel.Reader;
}

public class BackgroundEmailService : BackgroundService
{
    private readonly BackgroundEmailQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundEmailService> _logger;

    public BackgroundEmailService(
        BackgroundEmailQueue queue,
        IServiceProvider serviceProvider,
        ILogger<BackgroundEmailService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (to, subject, htmlBody) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                await emailService.SendAsync(to, subject, htmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {To}", to);
            }
        }
    }
}
