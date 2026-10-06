using AdElementsFree.Shortcuts;
using AdElementsFree.Providers.KOOK;
using AdElementsFree.Settings;
using AdElementsFree.Monitoring;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name)
{
    try { action(); } catch (Exception ex) when (ex is InvalidOperationException or FormatException) { Check(true, name); return; }
    throw new Exception(name);
}
const string original = "--profile \"C:\\folder with spaces\" --flag";
var applied = WindowsArguments.EnsurePort(original, 9222, out var added);
Check(added && applied.StartsWith(original), "Preserve original arguments");
Check(WindowsArguments.EnsurePort(applied, 9222, out added) == applied && !added, "Idempotent enable");
Check(WindowsArguments.EnsurePort("--remote-debugging-port 9222", 9222, out added) == "--remote-debugging-port 9222" && !added, "Split port value");
Check(WindowsArguments.EnsurePort("\"--remote-debugging-port=9222\"", 9222, out added).Length > 0 && !added, "Quoted flag");
Reject(() => WindowsArguments.EnsurePort("--remote-debugging-port=9000", 9222, out _), "Conflicting port");
Reject(() => WindowsArguments.EnsurePort("--remote-debugging-port=9222 --remote-debugging-port=9222", 9222, out _), "Duplicate flags");
Reject(() => WindowsArguments.EnsurePort("--foo \"open", 9222, out _), "Malformed quotes");
Reject(() => WindowsArguments.EnsurePort("-- file", 9222, out _), "End of options");
Check(WindowsArguments.RemoveOwnedPort(applied, original, applied, 9222) == original, "Exact restoration");
var edited = applied + " --user-added=value";
Check(WindowsArguments.RemoveOwnedPort(edited, original, applied, 9222).Contains("--user-added=value"), "Preserve user edits");
Check(WindowsArguments.RemoveOwnedPort("--remote-debugging-port=9000", original, applied, 9222) == "--remote-debugging-port=9000", "Preserve changed port");
Check(WindowsArguments.Parse("\"a b\" c").Select(t => t.Value).SequenceEqual(new[] { "a b", "c" }), "Tokenize quoted spaces");
Check(KookIdentity.IsSocket("ws://127.0.0.1:9222/devtools/page/abc", 9222), "Loopback socket");
foreach (var url in new[] { "ws://localhost:9222/devtools/page/abc", "ws://evil.test:9222/devtools/page/abc", "ws://127.0.0.1:9999/devtools/page/abc", "ws://user@127.0.0.1:9222/devtools/page/abc" })
    Check(!KookIdentity.IsSocket(url, 9222), "Reject untrusted socket " + url);
Check(KookIdentity.IsPage("https://www.kookapp.cn/app/main", "C:\\KOOK\\KOOK.exe"), "KOOK web page");
Check(KookIdentity.IsPage("http://localhost:5890/app/index.html", "C:\\KOOK\\KOOK.exe"), "KOOK local renderer");
foreach (var url in new[] { "https://evil.test/app/", "https://kookapp.cn.evil.test/app/", "http://localhost:9000/app/", "https://www.kookapp.cn/login", "file:///C:/elsewhere/app/index.html" })
    Check(!KookIdentity.IsPage(url, "C:\\KOOK\\KOOK.exe"), "Reject unrelated page " + url);
var temp = Path.Combine(AppContext.BaseDirectory, "aef-test-" + Guid.NewGuid() + ".json");
try
{
    var store = new JsonStore<AppSettings>(temp);
    var settings = store.Load(); settings.Providers["kook"] = true; store.Save(settings);
    Check(store.Load().Providers["kook"], "Settings persistence");
    File.WriteAllText(temp, "bad-json");
    try { store.Load(); throw new Exception("Corrupt data accepted"); }
    catch (System.Text.Json.JsonException) { Check(true, "Corruption fails closed"); }
}
finally { File.Delete(temp); }
using (var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
{
    listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    Check(LoopbackPort.GetOwner(port) == Environment.ProcessId, "Windows TCP owner lookup");
    Reject(() => KookIdentity.ValidateOwner(port, new()), "Reject foreign port without process access");
}
await CdpChecks.RunAsync(Check);
await SettingsChecks.RunAsync(Check);
Console.WriteLine($"{count} checks passed.");
