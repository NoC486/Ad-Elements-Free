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
        if (uri.Scheme == "http") return (uri.Host == "localhost" || uri.Host == "127.0.0.1") && uri.Port == 5890;
        if (uri.IsFile && !uri.IsUnc)
        {
            var root = Path.GetFullPath(Path.GetDirectoryName(executable)!) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(uri.LocalPath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
    public static bool IsSocket(string url, int port) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "ws" && uri.Host == "127.0.0.1" && uri.Port == port && uri.UserInfo.Length == 0
        && uri.AbsolutePath.StartsWith("/devtools/page/", StringComparison.Ordinal) && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}
