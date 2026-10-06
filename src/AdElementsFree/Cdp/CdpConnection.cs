using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace AdElementsFree.Cdp;

public sealed class CdpConnection : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim sendGate = new(1);
    private Task? reader;
    private int sequence;
    public bool Alive => socket.State == WebSocketState.Open && reader is { IsCompleted: false };

    public async Task ConnectAsync(Uri uri, CancellationToken ct)
    {
        // No Origin header and no proxy. Native CDP clients need no wildcard origin switch.
        socket.Options.Proxy = null;
        await socket.ConnectAsync(uri, ct);
        reader = ReadAsync();
    }
    public async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        int id = Interlocked.Increment(ref sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
            await sendGate.WaitAsync(timeout.Token);
            try { await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token); }
            finally { sendGate.Release(); }
            var response = await completion.Task.WaitAsync(timeout.Token);
            if (response.TryGetProperty("error", out _)) throw new InvalidOperationException("CDP 命令被拒绝。");
            var result = response.GetProperty("result");
            if (result.TryGetProperty("exceptionDetails", out _)) throw new InvalidOperationException("页面规则执行失败。");
            return result;
        }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task ReadAsync()
    {
        try
        {
            var buffer = new byte[8192];
            while (!lifetime.IsCancellationRequested)
            {
                using var data = new MemoryStream();
                WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), lifetime.Token);
                    if (received.MessageType == WebSocketMessageType.Close) return;
                    if (data.Length + received.Count > 2 * 1024 * 1024) throw new InvalidDataException("CDP 消息过大。");
                    data.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);
                using var document = JsonDocument.Parse(data.ToArray());
                if (document.RootElement.TryGetProperty("id", out var id) && pending.TryGetValue(id.GetInt32(), out var completion))
                    completion.TrySetResult(document.RootElement.Clone());
                // Other events are discarded, never logged or persisted.
            }
        }
        catch (Exception) { /* Malformed protocol data terminates only this connection. */ }
        finally
        {
            foreach (var request in pending.Values) request.TrySetException(new IOException("CDP 连接已关闭。"));
        }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        socket.Abort();
        if (reader != null) await reader;
        socket.Dispose();
        lifetime.Dispose();
        sendGate.Dispose();
    }
}
