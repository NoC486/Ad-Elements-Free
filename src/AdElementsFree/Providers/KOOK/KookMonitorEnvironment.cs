using System.Net.Http;
using AdElementsFree.Monitoring;

namespace AdElementsFree.Providers.KOOK;

internal sealed class KookMonitorEnvironment
{
    public int Port { get; init; } = 9222;
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public Func<List<TargetProcess>> Find { get; init; } = () => TargetProcessMonitor.Find("KOOK");
    public Func<int, List<TargetProcess>, TargetProcess?> ValidateOwner { get; init; } = KookIdentity.ValidateOwner;
    public Func<Action, IDisposable> Listen { get; init; } = action => new WindowProcessEvents("KOOK", action);
    public HttpClient Http { get; init; } = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 1024 * 1024 };
}
