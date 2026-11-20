namespace MochiDock.Core;

public sealed class MediaSeekExecutor : IDisposable
{
    CancellationTokenSource? active;
    readonly TimeSpan timeout;
    public MediaSeekExecutor(TimeSpan? timeout = null) => this.timeout = timeout ?? TimeSpan.FromSeconds(2);
    public void Cancel() => active?.Cancel();
    public async Task<MediaSeekResult> ExecuteAsync(Func<CancellationToken, Task<bool>> action)
    {
        Cancel();
        using var request = new CancellationTokenSource();
        active = request;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
        deadline.CancelAfter(timeout);
        try { return await action(deadline.Token).WaitAsync(deadline.Token) ? MediaSeekResult.Accepted : MediaSeekResult.Rejected; }
        catch (OperationCanceledException) { return request.IsCancellationRequested ? MediaSeekResult.Cancelled : MediaSeekResult.TimedOut; }
        catch { return MediaSeekResult.Rejected; }
        finally { if (ReferenceEquals(active, request)) active = null; }
    }
    public void Dispose() => Cancel();
}
