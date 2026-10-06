using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdElementsFree.Logging;
using AdElementsFree.Monitoring;
using AdElementsFree.Providers.KOOK;

internal static class MonitorChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = new Server();
        int running = 0, ready = 0, finds = 0, ownerChecks = 0;
        long generation = 1;
        Action? wake = null;
        var handler = new Handler(server);
        var environment = new KookMonitorEnvironment
        {
            Port = server.Port, Http = new HttpClient(handler),
            Find = () => { Interlocked.Increment(ref finds); return running == 0 ? new() : new() { new(100, generation, @"C:\KOOK\KOOK.exe") }; },
            ValidateOwner = (_, processes) => { Interlocked.Increment(ref ownerChecks); return ready == 0 ? null : processes.Single(); },
            Listen = callback => { wake = callback; return new Empty(); }
        };
        string root = Path.Combine(AppContext.BaseDirectory, "monitor-tests");
        Directory.CreateDirectory(root);
        await using var provider = new KookProvider(root, new Log(root), environment);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var monitor = provider.MonitorAsync(stop.Token);
        await Until(() => finds > 0, deadline.Token);
        await Task.Delay(500, deadline.Token);
        check(finds == 1 && ownerChecks == 0 && handler.Requests == 0, "No client: one initial snapshot, no TCP/HTTP scans or idle polling");
        running = 1; wake!();
        await Until(() => ownerChecks > 0, deadline.Token);
        check(handler.Requests == 0, "Client without debug port: no page requests");
        var elapsed = Stopwatch.StartNew();
        ready = 1;
        await Until(() => server.Injections == 1, deadline.Token);
        check(elapsed.Elapsed < TimeSpan.FromSeconds(3), "Page ready: injection without fixed four-second delay");
        await Task.Delay(400, deadline.Token);
        int requests = handler.Requests, scans = finds;
        wake(); // Showing an already-running client's window must not re-query it.
        await Task.Delay(600, deadline.Token);
        check(handler.Requests == requests && finds == scans && server.Injections == 1, "Successful session and window show: no timer scans or duplicate injection");
        await server.Event("Target.targetInfoChanged", new { targetInfo = new { type = "page", targetId = "one", url = Server.Url, title = "first" } });
        await Task.Delay(250, deadline.Token);
        requests = handler.Requests;
        await server.Event("Target.targetInfoChanged", new { targetInfo = new { type = "page", targetId = "one", url = Server.Url, title = "second" } });
        await Task.Delay(300, deadline.Token);
        check(handler.Requests == requests && server.Injections == 1, "Page title changes do not query targets or re-inject");
        server.Address = "http://localhost:5888/login";
        await server.Event("Target.targetInfoChanged", new { targetInfo = new { type = "page", targetId = "one", url = server.Address } });
        await Until(() => server.Removals > 0, deadline.Token);
        check(server.Injections == 1, "Leaving allowed page removes registration without injecting into login");
        server.Address = Server.Url;
        await server.Event("Target.targetInfoChanged", new { targetInfo = new { type = "page", targetId = "one", url = server.Address } });
        await Until(() => server.Injections == 2, deadline.Token);
        check(server.Injections == 2, "Returning to allowed page registers fresh rules");
        server.Targets = new[] { "one", "two" };
        await server.Event("Target.targetCreated", new { targetInfo = new { type = "page", targetId = "two", url = Server.Url } });
        await Until(() => server.Injections == 3, deadline.Token);
        check(server.Injections == 3, "New window event injects only the new page");
        running = 0; server.Browser!.Abort();
        await Until(() => provider.Status.Contains("等待 KOOK 启动"), deadline.Token);
        await Task.Delay(250, deadline.Token);
        requests = handler.Requests; scans = finds;
        await Task.Delay(500, deadline.Token);
        check(handler.Requests == requests && finds == scans, "Client exit returns to no-poll idle state");
        generation++; running = 1; server.Targets = new[] { "one" }; wake();
        await Until(() => server.Injections == 4, deadline.Token);
        check(server.Injections == 4, "Next client instance gets a fresh injection");
        stop.Cancel(); await monitor;
        check(server.Removals > 0, "Stopping monitor removes live registered rules");

        int timeoutScans = 0;
        var unavailable = new KookMonitorEnvironment
        {
            StartupTimeout = TimeSpan.FromMilliseconds(600),
            Find = () => { Interlocked.Increment(ref timeoutScans); return new() { new(200, 1, @"C:\KOOK\KOOK.exe") }; },
            ValidateOwner = (_, _) => null,
            Listen = _ => new Empty()
        };
        await using var waiting = new KookProvider(root, new Log(root), unavailable);
        using var end = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var waitTask = waiting.MonitorAsync(end.Token);
        await Until(() => waiting.Status.Contains("等待超时"), deadline.Token);
        int before = timeoutScans;
        await Task.Delay(500, deadline.Token);
        check(timeoutScans == before, "Unavailable debug interface stops retrying at the deadline");
        end.Cancel(); await waitTask;
        check(true, "Idle event wait cancels immediately");
    }

    private static async Task Until(Func<bool> condition, CancellationToken ct)
    { while (!condition()) await Task.Delay(20, ct); }
    private sealed class Empty : IDisposable { public void Dispose() { } }
    private sealed class Handler(Server server) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            object content = request.RequestUri!.AbsolutePath == "/json/version"
                ? new { webSocketDebuggerUrl = $"ws://127.0.0.1:{server.Port}/devtools/browser/test" }
                : server.Targets.Select(id => new { type = "page", url = server.Address, webSocketDebuggerUrl = $"ws://127.0.0.1:{server.Port}/devtools/page/{id}" }).ToArray();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(content)) });
        }
    }
    private sealed class Server : IAsyncDisposable
    {
        public const string Url = "http://localhost:5888/app/discover";
        public string Address = Url;
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly List<Task> clients = new();
        private readonly SemaphoreSlim browserWrites = new(1);
        private readonly Task accept;
        public WebSocket? Browser;
        public string[] Targets = new[] { "one" };
        public int Injections, Removals;
        public int Port { get; }
        public Server()
        {
            listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            accept = Accept();
        }
        private async Task Accept()
        {
            try { while (!stop.IsCancellationRequested) clients.Add(Serve(await listener.AcceptTcpClientAsync(stop.Token))); }
            catch (OperationCanceledException) { }
        }
        public async Task Event(string method, object parameters)
        {
            await browserWrites.WaitAsync(stop.Token);
            try { await Browser!.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(new { method, @params = parameters })), WebSocketMessageType.Text, true, stop.Token); }
            finally { browserWrites.Release(); }
        }
        private async Task Serve(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var header = new StringBuilder(); var single = new byte[1];
                    while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    { if (await stream.ReadAsync(single, stop.Token) == 0) return; header.Append((char)single[0]); }
                    bool browser = header.ToString().Contains("/devtools/browser/");
                    string key = header.ToString().Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
                    string digest = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {digest}\r\n\r\n"), stop.Token);
                    using var socket = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
                    if (browser) Browser = socket;
                    var buffer = new byte[65536];
                    while (!stop.IsCancellationRequested)
                    {
                        using var data = new MemoryStream();
                        WebSocketReceiveResult received;
                        do { received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token); if (received.MessageType == WebSocketMessageType.Close) return; data.Write(buffer, 0, received.Count); } while (!received.EndOfMessage);
                        using var json = JsonDocument.Parse(data.ToArray());
                        var request = json.RootElement;
                        string method = request.GetProperty("method").GetString()!;
                        object result = new { };
                        if (method == "Page.getFrameTree") result = new { frameTree = new { frame = new { url = Address } } };
                        if (method == "Page.addScriptToEvaluateOnNewDocument") result = new { identifier = Guid.NewGuid().ToString() };
                        if (method == "Page.removeScriptToEvaluateOnNewDocument") Interlocked.Increment(ref Removals);
                        if (method == "Runtime.evaluate")
                        {
                            if (request.GetProperty("params").GetProperty("expression").GetString()!.Contains("const apply")) Interlocked.Increment(ref Injections);
                            result = new { result = new { value = true } };
                        }
                        var reply = JsonSerializer.SerializeToUtf8Bytes(new { id = request.GetProperty("id").GetInt32(), result });
                        if (browser) await browserWrites.WaitAsync(stop.Token);
                        try { await socket.SendAsync(new ArraySegment<byte>(reply), WebSocketMessageType.Text, true, stop.Token); }
                        finally { if (browser) browserWrites.Release(); }
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException) { }
        }
        public async ValueTask DisposeAsync()
        { stop.Cancel(); listener.Stop(); await accept; await Task.WhenAll(clients); stop.Dispose(); browserWrites.Dispose(); }
    }
}
