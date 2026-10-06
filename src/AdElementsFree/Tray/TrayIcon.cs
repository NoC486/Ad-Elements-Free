using System.Drawing;
using System.Windows.Forms;

namespace AdElementsFree.Tray;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly Icon trayImage;
    private bool disposed;
    public TrayIcon(Action open, Action exit, Action? settings = null)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("AdElementsFree.Assets.App.ico")
            ?? throw new InvalidOperationException("Application icon resource is missing.");
        using var source = new Icon(stream, SystemInformation.SmallIconSize);
        trayImage = (Icon)source.Clone();
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Ad Elements Free", null, (_, _) => open());
        if (settings != null) menu.Items.Add("设置", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        icon = new NotifyIcon { Text = "Ad Elements Free", Icon = trayImage, ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) open(); };
        icon.DoubleClick += (_, _) => open();
    }
    public void Dispose() { if (disposed) return; disposed = true; icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Dispose(); trayImage.Dispose(); }
}
