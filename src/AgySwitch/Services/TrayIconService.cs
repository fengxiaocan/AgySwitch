using System.Diagnostics;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AgySwitch.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Drawing.Icon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.NotifyIcon _notifyIcon;

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        _icon = executablePath is not null
            ? Drawing.Icon.ExtractAssociatedIcon(executablePath) ?? CloneDefaultIcon()
            : CloneDefaultIcon();

        var openItem = new Forms.ToolStripMenuItem("打开 AgySwitch");
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add(openItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "AgySwitch · 账户切换",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    private static Drawing.Icon CloneDefaultIcon() =>
        (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
}
