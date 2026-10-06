using System.Windows;
using AdElementsFree.Core;
using AdElementsFree.Logging;
using AdElementsFree.Providers.KOOK;
using AdElementsFree.Settings;
using AdElementsFree.Tray;

namespace AdElementsFree;

public partial class App : Application
{
    private Mutex? singleton;
    private TrayIcon? tray;
    private UI.MainWindow? window;
    private readonly List<IAdProvider> providers = new();
    private JsonStore<AppSettings> store = null!;
    private AppSettings settings = null!;
    private Log log = null!;
    private bool exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        singleton = new Mutex(true, "Local\\AdElementsFree", out bool first);
        if (!first) { MessageBox.Show("Ad Elements Free 已在运行，请从系统托盘打开。", "Ad Elements Free"); Shutdown(); return; }
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdElementsFree");
        log = new(Path.Combine(directory, "Logs"));
        log.Write("Ad Elements Free started");
        try
        {
            store = new(Path.Combine(directory, "settings.json"));
            settings = store.Load();
            // Composition root: adding providers does not change UI, tray, or settings code.
            providers.Add(new KookProvider(directory, log));
            window = new(providers, ToggleAsync);
            MainWindow = window;
            tray = new(OpenWindow, ExitAsync);
            foreach (var provider in providers)
                await provider.SetEnabledAsync(settings.Providers.GetValueOrDefault(provider.Id));
        }
        catch (Exception ex)
        {
            log.Error("Startup", ex);
            MessageBox.Show("初始化失败。请检查 %LOCALAPPDATA%\\AdElementsFree 中的设置与日志；原文件未被覆盖。", "Ad Elements Free");
            ExitAsync();
        }
    }
    private async Task ToggleAsync(IAdProvider provider, bool enabled)
    {
        bool previous = settings.Providers.GetValueOrDefault(provider.Id);
        settings.Providers[provider.Id] = enabled;
        try { store.Save(settings); }
        catch { settings.Providers[provider.Id] = previous; throw; }
        await provider.SetEnabledAsync(enabled);
    }
    private void OpenWindow()
    {
        if (exiting || window == null) return;
        window.Show(); window.WindowState = WindowState.Normal; window.Activate();
    }
    private async void ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        if (window != null) window.IsEnabled = false;
        try { foreach (var provider in providers) await provider.DisposeAsync(); }
        catch (Exception ex) { log.Error("Shutdown", ex); }
        tray?.Dispose();
        log.Write("Ad Elements Free stopped");
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    { tray?.Dispose(); singleton?.Dispose(); base.OnExit(e); }
}
