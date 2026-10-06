using System.Windows;
using AdElementsFree.Settings;
using AdElementsFree.Updates;
using AdElementsFree.Rules;

namespace AdElementsFree.UI;

public partial class SettingsWindow : Window
{
    private readonly StartupRegistration startup;
    private readonly UpdateChecker updates;
    private readonly CancellationTokenSource lifetime = new();
    private Uri? releaseUrl;
    private readonly Func<Task>? reloadRules;
    private bool syncing;
    public SettingsWindow(StartupRegistration startup, UpdateChecker? updates = null, Func<Task>? reloadRules = null)
    {
        InitializeComponent();
        this.startup = startup;
        this.updates = updates ?? new();
        this.reloadRules = reloadRules;
        VersionText.Text = $"当前版本 v{UpdateChecker.CurrentVersion}";
        Activated += (_, _) => RefreshStartup();
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
        Closing += (_, e) => { if (syncing) { e.Cancel = true; RulesStatus.Text = "正在同步，请等待完成后关闭设置。"; } };
        RefreshStartup();
    }

    private async void SyncRules(object sender, RoutedEventArgs e)
    {
        syncing = true; SyncButton.IsEnabled = false;
        RulesStatus.Text = "正在下载最新规则，受保护目录可能需要 Windows 授权…";
        try
        {
            var message = await RuleSyncService.SyncAsync(lifetime.Token);
            RulesStatus.Text = message;
            try
            {
                if (reloadRules != null) await reloadRules();
                RulesStatus.Text = message + " 已启用的客户端将重新应用规则，请查看主窗口状态。";
            }
            catch { RulesStatus.Text = message + " 请关闭再开启客户端开关以重新应用。"; }
        }
        catch (InvalidOperationException ex) { RulesStatus.Text = ex.Message; }
        catch (OperationCanceledException) { RulesStatus.Text = "同步超时或已取消，请稍后重试。"; }
        catch { RulesStatus.Text = "同步失败，请检查网络、规则文件或目录权限后重试。"; }
        finally { syncing = false; SyncButton.IsEnabled = true; }
    }

    private void RefreshStartup()
    {
        try { StartupBox.IsChecked = startup.IsEnabled(); }
        catch { StartupBox.IsEnabled = false; StartupStatus.Text = "无法读取开机自启状态，请检查当前用户权限。"; }
    }

    private void ChangeStartup(object sender, RoutedEventArgs e)
    {
        try
        {
            startup.SetEnabled(StartupBox.IsChecked == true);
            StartupStatus.Text = StartupBox.IsChecked == true ? "已开启，下次登录 Windows 时生效。" : "已关闭开机自启。";
        }
        catch { StartupStatus.Text = "保存失败，请检查当前用户权限或安全软件设置。"; }
        RefreshStartup();
    }

    private async void CheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        ReleaseLinkText.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = "正在检查更新…";
        var token = lifetime.Token;
        try
        {
            var release = await updates.CheckAsync(UpdateChecker.CurrentVersion, token);
            releaseUrl = release?.Url;
            UpdateStatus.Text = release == null ? "当前已是最新版本。" : $"发现新版本 v{release.Version}，可前往发布页下载。";
            ReleaseLinkText.Visibility = release == null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (OperationCanceledException) { UpdateStatus.Text = "检查超时，请检查网络后重试。"; }
        catch (InvalidDataException ex) { UpdateStatus.Text = ex.Message; }
        catch { UpdateStatus.Text = "检查失败，请检查网络后重试，或通过主窗口的 GitHub 链接查看发布页。"; }
        finally { CheckButton.IsEnabled = true; }
    }

    private void OpenRelease(object sender, RoutedEventArgs e)
    { if (releaseUrl != null) MainWindow.OpenLink(this, releaseUrl.AbsoluteUri); }
}
