using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AdElementsFree.Core;
using AdElementsFree.Logging;
using AdElementsFree.Settings;
using AdElementsFree.Shortcuts;
using AdElementsFree.Tray;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static void Run()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "isolated-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
        string shortcut = Path.Combine(root, "Test.lnk");
        const string original = "--profile \"user profile\"";
        void WriteLink(string arguments)
        {
            object link = ((dynamic)shell).CreateShortcut(shortcut);
            try { ((dynamic)link).TargetPath = Environment.ProcessPath!; ((dynamic)link).Arguments = arguments; ((dynamic)link).Save(); }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        string ReadArguments()
        {
            object link = ((dynamic)shell).CreateShortcut(shortcut);
            try { return ((dynamic)link).Arguments; }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        try
        {
            WriteLink(original);
            var manager = new ShortcutManager(Path.Combine(root, "journal.json"), new Log(root), new[] { root });
            manager.Enable((_, _) => true, 9222);
            Check(ReadArguments() == original + " --remote-debugging-port=9222", "Real shortcut modified");
            manager.Enable((_, _) => true, 9222);
            Check(ReadArguments().Split("--remote-debugging-port").Length == 2, "Real shortcut idempotency");
            WriteLink(ReadArguments() + " --user-added");
            manager = new ShortcutManager(Path.Combine(root, "journal.json"), new Log(root), new[] { root });
            manager.Restore(9222);
            Check(ReadArguments().Contains("--user-added") && !ReadArguments().Contains("--remote-debugging-port"), "Journal recovery preserves user change");
            WriteLink("--remote-debugging-port=9222");
            manager.Enable((_, _) => true, 9222); manager.Restore(9222);
            Check(ReadArguments() == "--remote-debugging-port=9222", "Preexisting port belongs to user");
            // Simulate crash after durable journal but before shortcut Save.
            WriteLink(original);
            new JsonStore<System.Collections.Generic.List<ShortcutBackup>>(Path.Combine(root, "journal.json")).Save(
                new() { new(shortcut, Environment.ProcessPath!, original, original + " --remote-debugging-port=9222") });
            manager.Restore(9222);
            Check(ReadArguments() == original, "Crash before shortcut write is harmless");
        }
        finally { Marshal.FinalReleaseComObject(shell); }

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var provider = new FakeProvider();
        var window = new AdElementsFree.UI.MainWindow(new IAdProvider[] { provider }, (p, value) => p.SetEnabledAsync(value));
        using var tray = new TrayIcon(() => { }, () => { });
        window.Show(); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "window-smoke.png"))) encoder.Save(output);
        window.Close();
        Check(!window.IsVisible, "Window closes to tray");
        tray.Dispose(); tray.Dispose();
        application.Shutdown();
        Console.WriteLine("PASS WPF window render, tray creation and safe disposal");
    }
    private sealed class FakeProvider : IAdProvider
    {
        public string Id => "test";
        public string Name => "KOOK";
        public string Status => "等待 KOOK 启动\n已检查快捷方式；若 KOOK 已运行，请完全退出后从快捷方式重启。";
        public bool Enabled { get; private set; } = true;
        public event Action? Changed;
        public Task SetEnabledAsync(bool enabled) { Enabled = enabled; Changed?.Invoke(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
