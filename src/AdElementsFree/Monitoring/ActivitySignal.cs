using System.Threading.Channels;

namespace AdElementsFree.Monitoring;

public sealed class ActivitySignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(1);
    private int reasons;
    public void Set(int reason)
    {
        Interlocked.Or(ref reasons, reason);
        channel.Writer.TryWrite(true);
    }
    public async Task<int> WaitAsync(TimeSpan? timeout, CancellationToken ct)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout != null) wait.CancelAfter(timeout.Value);
        try { await channel.Reader.ReadAsync(wait.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return 0; }
        return Interlocked.Exchange(ref reasons, 0);
    }
}
