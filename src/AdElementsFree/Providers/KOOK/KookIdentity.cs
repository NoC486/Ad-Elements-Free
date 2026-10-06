using System.Diagnostics;
using AdElementsFree.Monitoring;

namespace AdElementsFree.Providers.KOOK;

public static class KookIdentity
{
    public static bool IsExecutable(string path)
    {
        if (!Path.GetFileName(path).Equals("KOOK.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
        var product = FileVersionInfo.GetVersionInfo(path).ProductName ?? "";
        return product.Contains("KOOK", StringComparison.OrdinalIgnoreCase) || product.Contains("开黑啦")
            || product.Contains("Kaiheila", StringComparison.OrdinalIgnoreCase);
    }
    public static TargetProcess? ValidateOwner(int port, List<TargetProcess> targets)
    {
        int? owner = LoopbackPort.GetOwner(port);
        if (owner == null) return null;
        var target = targets.FirstOrDefault(p => p.Id == owner);
        if (target == null || !IsExecutable(target.Path))
            throw new InvalidOperationException($"调试端口 {port} 已被其他应用占用，或无法确认其 KOOK 身份。");
        return target;
    }
    public static bool IsPage(string url, string executable)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.AbsolutePath.Contains("/app/", StringComparison.OrdinalIgnoreCase)) return false;
        if (uri.UserInfo.Length > 0) return false;
        if (uri.Scheme == "https" && uri.IsDefaultPort)
            return new[] { "kookapp.cn", "www.kookapp.cn", "kaiheila.cn", "www.kaiheila.cn" }.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        // Read the renderer's actual URL from the verified KOOK CDP target list.
        // Its local page-server port can vary independently of the CDP port.
        if (uri.Scheme == "http") return (uri.Host == "localhost" || uri.Host == "127.0.0.1")
            && uri.Port is >= 1 and <= 65535
            && uri.AbsolutePath.StartsWith("/app/", StringComparison.Ordinal);
        if (uri.IsFile && !uri.IsUnc)
        {
            var root = Path.GetFullPath(Path.GetDirectoryName(executable)!) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(uri.LocalPath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
    public static bool IsBrowserSocket(string url, int port) => IsDebugSocket(url, port, "/devtools/browser/");
    // The injected script pins origin/path, not query or fragment.
    public static bool SameRuleScope(string first, string second) =>
        Uri.TryCreate(first, UriKind.Absolute, out var a) && Uri.TryCreate(second, UriKind.Absolute, out var b)
        && a.Scheme == b.Scheme && a.Host == b.Host && a.Port == b.Port && a.AbsolutePath == b.AbsolutePath;
    public static bool IsSocket(string url, int port) => IsDebugSocket(url, port, "/devtools/page/");
    private static bool IsDebugSocket(string url, int port, string path) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "ws" && uri.Host == "127.0.0.1" && uri.Port == port && uri.UserInfo.Length == 0
        && uri.AbsolutePath.StartsWith(path, StringComparison.Ordinal) && uri.AbsolutePath.Length > path.Length
        && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}
