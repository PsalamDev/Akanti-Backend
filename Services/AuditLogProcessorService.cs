using System.Threading.Channels;
using Akanti.API.Models;

namespace Akanti.API.Services;

public interface IAuditLogQueue
{
    ValueTask EnqueueAsync(AuditLog entry, CancellationToken cancellationToken = default);
}

public class AuditLogQueue : IAuditLogQueue
{
    private readonly Channel<AuditLog> _channel =
        Channel.CreateBounded<AuditLog>(
            new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

    public ValueTask EnqueueAsync(AuditLog entry, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(entry, cancellationToken);

    public ChannelReader<AuditLog> Reader => _channel.Reader;
}

public class AuditLogProcessorService : BackgroundService
{
    private readonly AuditLogQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditLogProcessorService> _logger;

    public AuditLogProcessorService(
        AuditLogQueue queue,
        IServiceProvider serviceProvider,
        ILogger<AuditLogProcessorService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditLog>(200);

        await foreach (var entry in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            batch.Add(entry);

            if (batch.Count < 200)
                continue;

            await FlushAsync(batch, stoppingToken);
            batch.Clear();
        }

        if (batch.Count > 0)
            await FlushAsync(batch, stoppingToken);
    }

    private async Task FlushAsync(List<AuditLog> batch, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();
            context.AuditLogs.AddRange(batch);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist {Count} audit log entries", batch.Count);
        }
    }
}
