using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AdElementsFree.Core;

namespace AdElementsFree.UI;

public partial class MainWindow : Window
{
    private readonly Func<IAdProvider, bool, Task> toggle;
    public MainWindow(IReadOnlyList<IAdProvider> providers, Func<IAdProvider, bool, Task> toggle)
    {
        InitializeComponent(); this.toggle = toggle;
        ProviderList.ItemsSource = providers;
        foreach (var provider in providers) provider.Changed += Refresh;
        Closing += (_, e) => { e.Cancel = true; Hide(); };
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
