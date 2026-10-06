using System.Net.Http;
using System.Text.Json;
using AdElementsFree.Cdp;
using AdElementsFree.Core;
using AdElementsFree.Logging;
using AdElementsFree.Monitoring;
using AdElementsFree.Shortcuts;

namespace AdElementsFree.Providers.KOOK;

public sealed class KookProvider : IAdProvider
{
    public string Id => "kook";
    public string Name => "KOOK";
    public string Status { get; private set; } = "已关闭";
    public bool Enabled { get; private set; }
    public event Action? Changed;
    private const int Port = 9222;
    private readonly ShortcutManager shortcuts;
    private readonly Log log;
    private readonly SemaphoreSlim transitions = new(1);
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 1024 * 1024 };
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private string shortcutStatus = "";

    public KookProvider(string dataDirectory, Log log)
    { this.log = log; shortcuts = new(Path.Combine(dataDirectory, "kook-shortcuts.json"), log); }

    public async Task SetEnabledAsync(bool enabled)
    {
        await transitions.WaitAsync();
        try
        {
            await StopAsync();
            Enabled = enabled;
            log.Write($"KOOK provider: {(enabled ? "enabled" : "disabled")}");
            try
            {
                shortcutStatus = enabled ? shortcuts.Enable((path, _) => KookIdentity.IsExecutable(path), Port) : shortcuts.Restore(Port);
            }
            catch (Exception ex)
            {
                log.Error("Shortcut operation", ex);
                shortcutStatus = "快捷方式处理失败，未覆盖原配置；请检查恢复记录或文件权限。";
            }
            if (enabled)
            {
                cancellation = new();
                SetStatus("等待 KOOK 启动");
                worker = Task.Run(() => MonitorAsync(cancellation.Token));
            }
            else SetStatus("已关闭；若清理未完成，请完全重启 KOOK");
        }
        finally { transitions.Release(); }
    }

    private void SetStatus(string status)
    {
        string updated = status + "\n" + shortcutStatus;
        if (Status == updated) return;
        Status = updated;
        log.Write("KOOK status: " + status);
        Changed?.Invoke();
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        var sessions = new Dictionary<string, RuleSession>();
        int failures = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var processes = TargetProcessMonitor.Find("KOOK");
                    var owner = KookIdentity.ValidateOwner(Port, processes);
                    if (owner == null)
                    {
                        await ClearAsync(sessions, false);
                        SetStatus(processes.Count == 0 ? "等待 KOOK 启动（未安装时请先安装 KOOK）" : "KOOK 已运行，但调试端口未就绪；请从已配置的快捷方式重启");
                    }
                    else
                    {
                        using var response = await http.GetAsync($"http://127.0.0.1:{Port}/json", ct);
                        response.EnsureSuccessStatusCode();
                        using var targets = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
                        var live = new HashSet<string>();
                        foreach (var target in targets.RootElement.EnumerateArray())
                        {
                            if (!target.TryGetProperty("type", out var type) || type.GetString() != "page") continue;
                            if (!target.TryGetProperty("url", out var urlElement) || !target.TryGetProperty("webSocketDebuggerUrl", out var wsElement)) continue;
                            var url = urlElement.GetString() ?? "";
                            var ws = wsElement.GetString() ?? "";
                            if (!KookIdentity.IsPage(url, owner.Path) || !KookIdentity.IsSocket(ws, Port)) continue;
                            string key = $"{owner.Id}:{owner.Started}:{ws}";
                            live.Add(key);
                            if (sessions.TryGetValue(key, out var existing))
                            {
                                if (existing.Connection.Alive && existing.PageUrl == url) continue;
                                await existing.DisposeAsync(true);
                                sessions.Remove(key);
                            }
                            SetStatus("已识别 KOOK 页面，等待稳定后应用规则");
                            await Task.Delay(TimeSpan.FromSeconds(4), ct);
                            var fresh = KookIdentity.ValidateOwner(Port, TargetProcessMonitor.Find("KOOK"));
                            if (fresh == null || fresh.Id != owner.Id || fresh.Started != owner.Started) throw new IOException("端口所有者已变化。");
                            var session = await ApplyAsync(ws, url, owner, ct);
                            sessions.Add(key, session);
                        }
                        foreach (var key in sessions.Keys.Where(k => !live.Contains(k)).ToArray())
                        {
                            await sessions[key].DisposeAsync(false); sessions.Remove(key);
                        }
                        SetStatus(sessions.Count > 0 ? "已应用规则（实际隐藏效果取决于 KOOK 当前页面结构）" : "等待可验证的 KOOK /app/ 页面；不识别的版本不会执行规则");
                    }
                    failures = 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    await ClearAsync(sessions, false);
                    failures = Math.Min(failures + 1, 4);
                    log.Error("KOOK CDP", ex);
                    SetStatus(ex is InvalidOperationException ? ex.Message : "CDP 暂未就绪或连接失败，将低频重试；请检查 KOOK 启动参数");
                }
                await Task.Delay(TimeSpan.FromSeconds(failures == 0 ? 5 : Math.Min(60, 5 * (1 << failures))), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { await ClearAsync(sessions, true); }
    }

    private async Task<RuleSession> ApplyAsync(string ws, string url, TargetProcess owner, CancellationToken ct)
    {
        var connection = new CdpConnection();
        RuleSession? session = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await connection.ConnectAsync(new Uri(ws), timeout.Token);
            var frame = await connection.CallAsync("Page.getFrameTree", new { }, timeout.Token);
            var actual = frame.GetProperty("frameTree").GetProperty("frame").GetProperty("url").GetString();
            if (actual != url || !KookIdentity.IsPage(actual, owner.Path)) throw new InvalidOperationException("页面身份已变化，未应用规则。");
            var currentOwner = KookIdentity.ValidateOwner(Port, TargetProcessMonitor.Find("KOOK"));
            if (currentOwner?.Id != owner.Id || currentOwner.Started != owner.Started)
                throw new InvalidOperationException("端口身份已变化，未应用规则。");
            var script = KookRules.Build(url);
            var registration = await connection.CallAsync("Page.addScriptToEvaluateOnNewDocument", new { source = script }, timeout.Token);
            session = new(connection, registration.GetProperty("identifier").GetString()!, url);
            var evaluation = await connection.CallAsync("Runtime.evaluate", new { expression = script, returnByValue = true }, timeout.Token);
            if (!evaluation.GetProperty("result").TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("页面尚未就绪，稍后重试。");
            log.Write("KOOK rules applied; CDP acknowledged");
            return session;
        }
        catch
        {
            if (session != null) await session.DisposeAsync(true); else await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ClearAsync(Dictionary<string, RuleSession> sessions, bool remove)
    { foreach (var session in sessions.Values) await session.DisposeAsync(remove); sessions.Clear(); }
    private async Task StopAsync()
    {
        cancellation?.Cancel();
        if (worker != null) await worker;
        worker = null; cancellation?.Dispose(); cancellation = null;
    }
    public async ValueTask DisposeAsync()
    {
        await transitions.WaitAsync();
        try { await StopAsync(); http.Dispose(); }
        finally { transitions.Release(); }
        // Keep enabled setting and owned shortcut flags on normal app exit.
    }

    private sealed record RuleSession(CdpConnection Connection, string ScriptId, string PageUrl)
    {
        public async Task DisposeAsync(bool remove)
        {
            if (remove && Connection.Alive)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await Connection.CallAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier = ScriptId }, timeout.Token);
                    await Connection.CallAsync("Runtime.evaluate", new { expression = KookRules.Remove(PageUrl) }, timeout.Token);
                }
                catch (Exception) { /* Disconnected renderer needs no live cleanup; restart clears residual CSS. */ }
            }
            await Connection.DisposeAsync();
        }
    }
}
