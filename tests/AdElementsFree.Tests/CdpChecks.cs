using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdElementsFree.Cdp;

internal static class CdpChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        bool originAbsent = false;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream = client.GetStream();
            var header = new StringBuilder();
            var single = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(single, deadline.Token) == 0) throw new IOException();
                header.Append((char)single[0]);
                if (header.Length > 8192) throw new IOException();
            }
            originAbsent = !header.ToString().Contains("\r\nOrigin:", StringComparison.OrdinalIgnoreCase);
            string key = header.ToString().Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
            string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), deadline.Token);
            using var ws = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
            for (int index = 0; index < 4; index++)
            {
                var buffer = new byte[8192];
                var request = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
                using var json = JsonDocument.Parse(buffer.AsMemory(0, request.Count));
                int id = json.RootElement.GetProperty("id").GetInt32();
                if (index == 3) { ws.Abort(); return; }
                var message = index switch
                {
                    0 => JsonSerializer.SerializeToUtf8Bytes(new { id, result = new { identifier = "registered" } }),
                    1 => JsonSerializer.SerializeToUtf8Bytes(new { id, error = new { code = -32601, message = "rejected" } }),
                    _ => JsonSerializer.SerializeToUtf8Bytes(new { id, result = new { exceptionDetails = new { text = "failed" } } })
                };
                if (index == 0)
                {
                    await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"method\":\"Page.testEvent\"}")), WebSocketMessageType.Text, true, deadline.Token);
                    await ws.SendAsync(new ArraySegment<byte>(message, 0, 5), WebSocketMessageType.Text, false, deadline.Token);
                    await ws.SendAsync(new ArraySegment<byte>(message, 5, message.Length - 5), WebSocketMessageType.Text, true, deadline.Token);
                }
                else await ws.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, deadline.Token);
            }
        }, deadline.Token);
        await using var connection = new CdpConnection();
        await connection.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/devtools/page/test"), deadline.Token);
        var result = await connection.CallAsync("Page.addScriptToEvaluateOnNewDocument", new { source = "test" }, deadline.Token);
        check(result.GetProperty("identifier").GetString() == "registered", "CDP matches replies across events and fragmented frames");
        check(originAbsent, "Native client sends no Origin header");
        for (int i = 0; i < 2; i++)
        {
            try { await connection.CallAsync("Runtime.evaluate", new { expression = "test" }, deadline.Token); throw new Exception("Expected CDP failure"); }
            catch (InvalidOperationException) { check(true, i == 0 ? "CDP protocol error is not success" : "JavaScript exception is not success"); }
        }
        try { await connection.CallAsync("Page.getFrameTree", new { }, deadline.Token); throw new Exception("Expected disconnect"); }
        catch (IOException) { check(true, "CDP disconnect releases pending request"); }
        await server;
    }
}
