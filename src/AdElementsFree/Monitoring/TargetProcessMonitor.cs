using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AdElementsFree.Monitoring;

public sealed record TargetProcess(int Id, long Started, string Path);

public static class TargetProcessMonitor
{
    public static List<TargetProcess> Find(string executableName)
    {
        var result = new List<TargetProcess>();
        foreach (var process in Process.GetProcessesByName(executableName))
        {
            using (process)
            {
                try
                {
                    var path = GetPath(process.Id);
                    if (path != null && System.IO.Path.GetFileNameWithoutExtension(path).Equals(executableName, StringComparison.OrdinalIgnoreCase))
                        result.Add(new(process.Id, process.StartTime.ToUniversalTime().Ticks, path));
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        return result;
    }

    private static string? GetPath(int pid)
    {
        // Only named target PIDs, QUERY_LIMITED_INFORMATION only. No game process handles.
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(32768);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
