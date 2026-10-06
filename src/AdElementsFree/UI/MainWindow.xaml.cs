using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AdElementsFree.Core;
using AdElementsFree.Settings;
using AdElementsFree.Updates;
using System.Diagnostics;
using System.Windows.Navigation;

namespace AdElementsFree.UI;

public partial class MainWindow : Window
{
    private readonly Func<IAdProvider, bool, Task> toggle;
    private SettingsWindow? settingsWindow;
    public MainWindow(IReadOnlyList<IAdProvider> providers, Func<IAdProvider, bool, Task> toggle)
    {
        InitializeComponent(); this.toggle = toggle;
        ProviderList.ItemsSource = providers;
        VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
        foreach (var provider in providers) provider.Changed += Refresh;
        Closing += (_, e) => { e.Cancel = true; settingsWindow?.Close(); Hide(); };
    }
    public void ShowSettings()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        try
        {
            settingsWindow = new(new StartupRegistration(Environment.ProcessPath!)) { Owner = this };
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Show();
        }
        catch { MessageBox.Show(this, "无法打开设置，请检查程序所在路径与当前用户权限。", "Ad Elements Free"); }
    }
    private void OpenSettings(object sender, RoutedEventArgs e) => ShowSettings();
    private void NavigateLink(object sender, RequestNavigateEventArgs e)
    { OpenLink(this, e.Uri.AbsoluteUri); e.Handled = true; }
    public static void OpenLink(Window owner, string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { MessageBox.Show(owner, "无法打开浏览器，请手动访问：\n" + url, "Ad Elements Free"); }
    }
    private void Refresh() => Dispatcher.BeginInvoke(() => ProviderList.Items.Refresh());
    private async void ToggleProvider(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: IAdProvider provider } box) return;
        ProviderList.IsEnabled = false;
        try { await toggle(provider, box.IsChecked == true); }
        catch (Exception) { MessageBox.Show(this, "无法保存或应用设置，请检查本地数据目录的写入权限。", "Ad Elements Free"); }
        finally { ProviderList.IsEnabled = true; Refresh(); }
    }
}
