using System.ComponentModel;
using System.Diagnostics;

namespace AdElementsFree.Rules;

public static class RuleSyncService
{
    public static async Task<string> SyncAsync(CancellationToken token)
    {
        try { return "规则已同步（" + await new RuleSynchronizer().SyncAsync(AppContext.BaseDirectory, token) + "）。"; }
        catch (UnauthorizedAccessException)
        {
            token.ThrowIfCancellationRequested();
            // No supplied paths, URLs or staged user files are passed to the elevated process.
            // It downloads from the fixed repository and writes only beside its own executable.
            try
            {
                using var helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--sync-rules-elevated")
                    { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory })
                    ?? throw new IOException("无法启动规则同步助手。");
                await helper.WaitForExitAsync();
                if (helper.ExitCode != 0) throw new IOException("同步未完成，请检查网络或目录权限后重试。");
                return "规则已同步。";
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            { throw new InvalidOperationException("已取消 Windows 授权，原规则保持不变。"); }
        }
    }
}
