using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using AgySwitch.Models;
using AgySwitch.Services;

namespace AgySwitch.Dialogs;

public partial class SettingsWindow : Window
{
    private readonly CliProcessService _cliProcessService;

    public SettingsWindow(AppSettings settings, CliProcessService cliProcessService)
    {
        _cliProcessService = cliProcessService;
        InitializeComponent();
        GeminiPathTextBox.Text = settings.GeminiDirectory;
        CliPathTextBox.Text = settings.CliPath ?? cliProcessService.FindExecutable() ?? string.Empty;
        UpdateGeminiStatus();
        UpdateCliStatus();
    }

    public AppSettings? ResultSettings { get; private set; }

    private void GeminiPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateGeminiStatus();
    }

    private void CliPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCliStatus();
    }

    private void UpdateGeminiStatus()
    {
        if (GeminiStatusText is null || GeminiPathTextBox is null)
        {
            return;
        }

        var path = GeminiPathTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            GeminiStatusText.Text = "⚠ 目录路径不能为空";
            GeminiStatusText.Foreground = (Brush)FindResource("DangerRed");
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                GeminiStatusText.Text = "✓ 目录存在且有效";
                GeminiStatusText.Foreground = (Brush)FindResource("SuccessGreen");
            }
            else
            {
                GeminiStatusText.Text = "ℹ 目录尚不存在，保存后将自动建立";
                GeminiStatusText.Foreground = (Brush)FindResource("WarningAmber");
            }
        }
        catch
        {
            GeminiStatusText.Text = "⚠ 路径格式无效";
            GeminiStatusText.Foreground = (Brush)FindResource("DangerRed");
        }
    }

    private void UpdateCliStatus()
    {
        if (CliStatusBadge is null || CliPathTextBox is null || _cliProcessService is null)
        {
            return;
        }

        var path = CliPathTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var detected = _cliProcessService.FindExecutable();
                if (detected is not null)
                {
                    CliStatusBadge.Text = $"✓ PATH 中已检测到：{Path.GetFileName(detected)}";
                    CliStatusBadge.Foreground = (Brush)FindResource("SuccessGreen");
                }
                else
                {
                    CliStatusBadge.Text = "ℹ 留空（未在 PATH 检测到 CLI，建议手动指定）";
                    CliStatusBadge.Foreground = (Brush)FindResource("WarningAmber");
                }
            }
            catch
            {
                CliStatusBadge.Text = "ℹ 未在 PATH 检测到 CLI";
                CliStatusBadge.Foreground = (Brush)FindResource("WarningAmber");
            }
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                CliStatusBadge.Text = $"✓ CLI 文件存在：{Path.GetFileName(path)}";
                CliStatusBadge.Foreground = (Brush)FindResource("SuccessGreen");
            }
            else
            {
                CliStatusBadge.Text = "⚠ 指定的文件不存在";
                CliStatusBadge.Foreground = (Brush)FindResource("DangerRed");
            }
        }
        catch
        {
            CliStatusBadge.Text = "⚠ 路径格式无效";
            CliStatusBadge.Foreground = (Brush)FindResource("DangerRed");
        }
    }

    private void BrowseGemini_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 Antigravity CLI 使用的 .gemini 目录",
            InitialDirectory = Directory.Exists(GeminiPathTextBox.Text)
                ? GeminiPathTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog(this) == true)
        {
            GeminiPathTextBox.Text = dialog.FolderName;
        }
    }

    private void BrowseCli_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Antigravity CLI 可执行文件",
            Filter = "Antigravity CLI (agy.exe;antigravity.exe)|agy.exe;antigravity.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = File.Exists(CliPathTextBox.Text)
                ? Path.GetDirectoryName(CliPathTextBox.Text)
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        if (dialog.ShowDialog(this) == true)
        {
            CliPathTextBox.Text = dialog.FileName;
        }
    }

    private void DetectCli_Click(object sender, RoutedEventArgs e)
    {
        CliPathTextBox.Text = _cliProcessService.FindExecutable() ?? string.Empty;
        UpdateCliStatus();
        if (string.IsNullOrWhiteSpace(CliPathTextBox.Text))
        {
            MessageBox.Show(this, "PATH 中未检测到 agy.exe 或 antigravity.exe。", "未检测到 CLI", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GeminiPathTextBox.Text))
        {
            MessageBox.Show(this, "请选择 Gemini 配置目录。", "路径无效", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string geminiDirectory;
        string? cliPath;
        try
        {
            geminiDirectory = Path.GetFullPath(GeminiPathTextBox.Text.Trim());
            cliPath = string.IsNullOrWhiteSpace(CliPathTextBox.Text) ? null : Path.GetFullPath(CliPathTextBox.Text.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(this, "CLI 路径格式无效。", "路径无效", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (cliPath is not null && !File.Exists(cliPath))
        {
            MessageBox.Show(this, "指定的 CLI 文件不存在。", "路径无效", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ResultSettings = new AppSettings
        {
            GeminiDirectory = geminiDirectory,
            CliPath = cliPath
        };
        DialogResult = true;
    }
}
