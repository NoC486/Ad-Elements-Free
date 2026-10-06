using System.Windows;
using AdElementsFree.Settings;
using AdElementsFree.Updates;

namespace AdElementsFree.UI;

public partial class SettingsWindow : Window
{
    private readonly StartupRegistration startup;
    private readonly UpdateChecker updates;
    private readonly CancellationTokenSource lifetime = new();
    private Uri? releaseUrl;
    public SettingsWindow(StartupRegistration startup, UpdateChecker? updates = null)
    {
        InitializeComponent();
        this.startup = startup;
        this.updates = updates ?? new();
        VersionText.Text = $"当前版本 v{UpdateChecker.CurrentVersion}";
        Activated += (_, _) => RefreshStartup();
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
        RefreshStartup();
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
