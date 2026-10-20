
public interface IBackgroundEmailQueue
{
    ValueTask EnqueueAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}