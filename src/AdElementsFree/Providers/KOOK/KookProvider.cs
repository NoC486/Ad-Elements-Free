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
    private readonly int Port;
    private readonly KookMonitorEnvironment environment;
    private readonly ShortcutManager shortcuts;
    private readonly Log log;
    private readonly SemaphoreSlim transitions = new(1);
    private readonly HttpClient http;
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private string shortcutStatus = "";

    public KookProvider(string dataDirectory, Log log)
        : this(dataDirectory, log, new KookMonitorEnvironment()) { }
    internal KookProvider(string dataDirectory, Log log, KookMonitorEnvironment environment)
    {
        this.log = log;
        this.environment = environment;
        Port = environment.Port;
        http = environment.Http;
        shortcuts = new(Path.Combine(dataDirectory, "kook-shortcuts.json"), log);
    }

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

    internal async Task MonitorAsync(CancellationToken ct)
    {
        const int Window = 1, Target = 2, Disconnect = 4;
        var signal = new ActivitySignal();
        var sessions = new Dictionary<string, RuleSession>();
        var attempted = new HashSet<(int Id, long Started)>();
        CdpConnection? browser = null;
        TargetProcess? browserOwner = null;
        long retryUntil = 0;
        Type? lastFailure = null;
        bool retry = false;
        try
        {
            using var windows = environment.Listen(() => signal.Set(Window));
            signal.Set(Window); // One snapshot after subscribing also catches an already-running client.
            while (!ct.IsCancellationRequested)
            {
                int reason = await signal.WaitAsync(retry ? TimeSpan.FromMilliseconds(250) : null, ct);
                if (!retry && reason == Window && browser?.Alive == true) continue;
                try
                {
                    var processes = environment.Find();
                    attempted.RemoveWhere(p => !processes.Any(current => current.Id == p.Id && current.Started == p.Started));
                    if (processes.Count == 0)
                    {
                        await ClearAsync(sessions, false);
                        if (browser != null) { await browser.DisposeAsync(); browser = null; }
                        browserOwner = null;
                        retry = false;
                        SetStatus("等待 KOOK 启动（事件监听，无定时扫描）");
                        continue; // Do not inspect TCP metadata or request HTTP when no client is running.
                    }
                    if (!retry && browser == null && processes.All(p => attempted.Contains((p.Id, p.Started)))) continue;
                    if (!retry)
                    {
                        retryUntil = Environment.TickCount64 + (long)environment.StartupTimeout.TotalMilliseconds;
                        retry = true;
                        foreach (var process in processes) attempted.Add((process.Id, process.Started));
                    }
                    var owner = environment.ValidateOwner(Port, processes);
                    if (owner == null)
                    {
                        SetStatus("KOOK 已运行，等待调试接口就绪");
                    }
                    else
                    {
                        if (browser != null && (!browser.Alive || browserOwner?.Id != owner.Id || browserOwner.Started != owner.Started))
                        {
                            await browser.DisposeAsync(); browser = null;
                            await ClearAsync(sessions, true);
                        }
                        if (browser == null)
                        {
                            using var versionResponse = await http.GetAsync($"http://127.0.0.1:{Port}/json/version", ct);
                            versionResponse.EnsureSuccessStatusCode();
                            using var version = JsonDocument.Parse(await versionResponse.Content.ReadAsByteArrayAsync(ct));
                            string socket = version.RootElement.GetProperty("webSocketDebuggerUrl").GetString() ?? "";
                            if (!KookIdentity.IsBrowserSocket(socket, Port)) throw new InvalidOperationException("无法验证调试事件接口。");
                            var candidate = new CdpConnection();
                            try
                            {
                                var knownTargets = new Dictionary<string, string>();
                                candidate.EventReceived += message =>
                                {
                                    var method = message.GetProperty("method").GetString();
                                    if (!message.TryGetProperty("params", out var data)) return;
                                    if (method == "Target.targetDestroyed" && data.TryGetProperty("targetId", out var removed))
                                    {
                                        if (knownTargets.Remove(removed.GetString() ?? "")) signal.Set(Target);
                                    }
                                    else if (method is "Target.targetCreated" or "Target.targetInfoChanged"
                                        && data.TryGetProperty("targetInfo", out var info)
                                        && info.TryGetProperty("type", out var type) && type.GetString() == "page"
                                        && info.TryGetProperty("targetId", out var id) && info.TryGetProperty("url", out var url))
                                    {
                                        string key = id.GetString() ?? "", address = url.GetString() ?? "";
                                        // Title/attachment changes are not navigation and need no query.
                                        if (!knownTargets.TryGetValue(key, out var previous) || previous != address)
                                        { knownTargets[key] = address; signal.Set(Target); }
                                    }
                                };
                                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                connectTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                                await candidate.ConnectAsync(new Uri(socket), connectTimeout.Token);
                                // Subscribe before the snapshot so new windows cannot fall between the two.
                                await candidate.CallAsync("Target.setDiscoverTargets", new { discover = true }, ct);
                                candidate.Closed += () => signal.Set(Disconnect);
                                browser = candidate;
                                browserOwner = owner;
                                if (!browser.Alive) signal.Set(Disconnect);
                            }
                            catch { await candidate.DisposeAsync(); throw; }
                        }
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
                                if (existing.Connection.Alive && KookIdentity.SameRuleScope(existing.PageUrl, url)) continue;
                                await existing.DisposeAsync(true);
                                sessions.Remove(key);
                            }
                            SetStatus("已识别 KOOK 页面，正在应用规则");
                            var session = await ApplyAsync(ws, url, owner, ct);
                            session.Connection.Closed += () => signal.Set(Disconnect);
                            sessions.Add(key, session);
                        }
                        foreach (var key in sessions.Keys.Where(k => !live.Contains(k)).ToArray())
                        {
                            await sessions[key].DisposeAsync(true); sessions.Remove(key);
                        }
                        // After discovery succeeds, CDP events wake us for page creation/navigation.
                        // No periodic process, TCP, or target-list queries remain.
                        retry = false;
                        lastFailure = null;
                        SetStatus(sessions.Count > 0 ? "已应用规则（事件监听，无定时扫描）" : "等待 KOOK /app/ 页面（事件监听）");
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (lastFailure != ex.GetType()) log.Error("KOOK CDP", ex);
                    lastFailure = ex.GetType();
                    SetStatus(ex is InvalidOperationException ? ex.Message : "调试接口或页面暂未就绪，启动期间短暂重试");
                }
                if (retry && Environment.TickCount64 >= retryUntil)
                {
                    retry = false;
                    SetStatus("等待超时，已停止重试；请从配置好的快捷方式重启 KOOK，或重新开启开关");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.Error("KOOK event listener", ex);
            SetStatus("启动事件监听失败，请重新开启开关；未启用定时扫描");
        }
        finally
        {
            if (browser != null) await browser.DisposeAsync();
            await ClearAsync(sessions, true);
        }
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
            var currentOwner = environment.ValidateOwner(Port, environment.Find());
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
