using System.Runtime.InteropServices;
using AdElementsFree.Logging;
using AdElementsFree.Settings;

namespace AdElementsFree.Shortcuts;

public sealed record ShortcutBackup(string Path, string Target, string Original, string Applied);

public sealed class ShortcutManager
{
    private readonly JsonStore<List<ShortcutBackup>> store;
    private readonly Log log;
    private readonly IReadOnlyList<string>? rootsOverride;
    public ShortcutManager(string journal, Log log, IReadOnlyList<string>? roots = null)
    { store = new(journal); this.log = log; rootsOverride = roots; }

    // Called on the WPF STA thread; WScript.Shell is never shared across threads.
    public string Enable(Func<string, string, bool> isTarget, int port)
    {
        var backups = store.Load();
        int found = 0, errors = 0;
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!);
            foreach (var path in EnumerateLinks())
            {
                object? link = null;
                try
                {
                    link = ((dynamic)shell!).CreateShortcut(path);
                    string target = ((dynamic)link).TargetPath;
                    string args = ((dynamic)link).Arguments;
                    if (!isTarget(target, args)) continue;
                    found++;
                    string updated = WindowsArguments.EnsurePort(args, port, out bool added);
                    if (!added) continue;
                    // Journal is durable BEFORE mutating the shortcut (crash recovery).
                    backups.RemoveAll(b => b.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                    backups.Add(new(path, target, args, updated));
                    store.Save(backups);
                    ((dynamic)link).Arguments = updated;
                    ((dynamic)link).Save();
                    log.Write("Shortcut: launch argument added");
                }
                catch (Exception ex) { errors++; log.Error("Shortcut update", ex); }
                finally { Release(link); }
            }
        }
        finally { Release(shell); }
        return found == 0 ? "未找到可处理的 KOOK 直启快捷方式；请手动添加启动参数。"
            : errors > 0 ? $"发现 {found} 个快捷方式，{errors} 项未能处理（权限或参数冲突）；请检查。"
            : $"已检查 {found} 个快捷方式；若 KOOK 已运行，请完全退出后从快捷方式重启。";
    }

    public string Restore(int port)
    {
        var backups = store.Load();
        int failures = 0;
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!);
            foreach (var backup in backups.ToArray())
            {
                object? link = null;
                try
                {
                    if (!File.Exists(backup.Path)) { backups.Remove(backup); continue; }
                    link = ((dynamic)shell!).CreateShortcut(backup.Path);
                    string target = ((dynamic)link).TargetPath;
                    string current = ((dynamic)link).Arguments;
                    if (!string.Equals(target, backup.Target, StringComparison.OrdinalIgnoreCase))
                    { failures++; continue; }
                    string restored = WindowsArguments.RemoveOwnedPort(current, backup.Original, backup.Applied, port);
                    if (restored != current)
                    {
                        ((dynamic)link).Arguments = restored;
                        ((dynamic)link).Save();
                        log.Write("Shortcut: owned argument removed");
                    }
                    else if (WindowsArguments.Parse(current).Any(t => t.Value.StartsWith("--remote-debugging-port", StringComparison.OrdinalIgnoreCase)))
                    { failures++; continue; }
                    backups.Remove(backup);
                }
                catch (Exception ex) { failures++; log.Error("Shortcut restore", ex); }
                finally { Release(link); }
            }
            store.Save(backups);
        }
        finally { Release(shell); }
        return failures == 0 ? "快捷方式已恢复；已运行的 KOOK 调试端口将在完全退出后关闭。"
            : $"{failures} 个快捷方式已被改动或无写权限，保留恢复记录，请手动检查。";
    }

    private IEnumerable<string> EnumerateLinks()
    {
        var roots = new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory,
            Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in rootsOverride ?? roots.Select(Environment.GetFolderPath).ToArray())
        {
            if (!Directory.Exists(path)) continue;
            string[] files;
            try { files = Directory.GetFiles(path, "*.lnk", new EnumerationOptions {
                RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }); }
            catch (IOException ex) { log.Error("Shortcut discovery", ex); continue; }
            foreach (var file in files) if (seen.Add(file)) yield return file;
        }
    }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
