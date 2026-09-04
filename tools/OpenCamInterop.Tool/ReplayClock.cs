namespace OpenCamInterop.EventLab;

public interface IReplayClock
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemReplayClock : IReplayClock
{
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }
}
