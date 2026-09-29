using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AgySwitch.Dialogs;
using AgySwitch.Models;
using AgySwitch.Services;

namespace AgySwitch;

public partial class MainWindow : Window
{
    private readonly SettingsRepository _settingsRepository = new();
    private readonly IQuotaProvider _quotaProvider = new AntigravityCliQuotaProvider();
    private readonly TrayIconService _trayIconService;
    private AppSettings _settings;
    private CliProcessService _cliProcessService = null!;
    private AccountService _accountService = null!;
    private SwitchService _switchService = null!;
    private AppLogger _logger = null!;
    private readonly DispatcherTimer _countdownTimer;
    private DateTimeOffset? _quotaResetAt;
    private bool _loaded;
    private bool _quotaQueryRunning;
    private bool _showRawQuotaOutput;
    private List<QuotaModelCard> _quotaCards = [];

    public MainWindow()
    {
        InitializeComponent();
        _trayIconService = new TrayIconService();
        _trayIconService.OpenRequested += (_, _) => Dispatcher.BeginInvoke(new Action(RestoreFromTray));
        _trayIconService.ExitRequested += (_, _) => Dispatcher.BeginInvoke(new Action(ExitFromTray));
        _settings = _settingsRepository.Load();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateCountdownText();
        BuildServices();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        Close();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _trayIconService.Dispose();
    }

    private void BuildServices()
    {
        _cliProcessService = new CliProcessService();
        var paths = new AppPaths(_settings.GeminiDirectory);
        _logger = new AppLogger(paths);
        _accountService = new AccountService(_settings, new AccountIdentityReader(), _logger);
        _switchService = new SwitchService(_accountService, _cliProcessService, _logger);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        RefreshView();
        OfferToSaveFirstDetectedAccount();
    }

    private void RefreshView()
    {
        var paths = _accountService.Paths;
        var profiles = _accountService.GetProfiles();
        AccountsItems.ItemsSource = profiles;
        EmptyAccountsPanel.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountCountText.Text = $"{profiles.Count} 个账户档案";

        CurrentEmailText.Text = _accountService.CurrentAccountLabel;
        CurrentStateText.Text = !_accountService.AnyCurrentCredentialExists
            ? "未检测到当前登录文件"
            : _accountService.CurrentCredentialsExist
                ? "当前认证文件可读取"
                : "认证文件不完整，请检查所选目录";

        GeminiPathText.Text = paths.GeminiDirectory;
        AccountsPathText.Text = paths.AccountsDirectory;
        var cliPath = _cliProcessService.FindExecutable(_settings.CliPath);
        CliStatusText.Text = cliPath is null ? "未检测到 CLI" : $"CLI 已检测 · {Path.GetFileName(cliPath)}";
        CliStatusText.ToolTip = cliPath;
        var isGeminiValid = Directory.Exists(paths.GeminiDirectory);
        FooterStatusText.Text = isGeminiValid ? "就绪" : "未找到配置目录，请在设置中选择 Antigravity CLI 使用的 .gemini 目录";
        FooterStatusText.Foreground = isGeminiValid
            ? (Brush)FindResource("TextSecondary")
            : (Brush)FindResource("WarningAmber");
        FooterStatusDot.Fill = isGeminiValid
            ? (Brush)FindResource("SuccessGreen")
            : (Brush)FindResource("WarningAmber");
    }

    private void OfferToSaveFirstDetectedAccount()
    {
        if (!_accountService.CurrentCredentialsExist || _accountService.GetProfiles().Any(profile => profile.IsCurrent))
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"检测到当前 Antigravity 登录账户：\n\n{_accountService.CurrentAccountLabel}\n\n要将它保存到账户列表吗？",
            "检测到当前账户", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Yes)
        {
            SaveCurrentInteractively();
        }
    }

    private void SaveCurrent_Click(object sender, RoutedEventArgs e) => SaveCurrentInteractively();

    private void SaveCurrentInteractively()
    {
        if (!_accountService.CurrentCredentialsExist)
        {
            ShowError("当前认证文件不完整，无法备份。请检查 `.gemini` 目录是否包含 google_accounts.json 和 antigravity-oauth-token。", "无法备份");
            return;
        }

        var identity = _accountService.GetCurrentIdentity();
        var existing = identity is null
            ? null
            : _accountService.GetProfiles().FirstOrDefault(profile => _accountService.Matches(profile, identity));
        var saveAsNew = false;
        if (existing is not null)
        {
            var duplicateChoice = MessageBox.Show(this,
                $"这个 Google 账户已经保存在列表中：\n\n{existing.Name}\n{existing.Email ?? "未知邮箱"}\n\n是：更新现有备份\n否：另存为一个账户\n取消：不操作",
                "账户已保存", MessageBoxButton.YesNoCancel, MessageBoxImage.Information, MessageBoxResult.Yes);
            if (duplicateChoice == MessageBoxResult.Cancel)
            {
                return;
            }

            saveAsNew = duplicateChoice == MessageBoxResult.No;
        }

        var suggestedName = existing?.Name
            ?? (!string.IsNullOrWhiteSpace(identity?.Email) ? identity.Email : "Google 账户");
        var prompt = new InputNameWindow("备份当前账户",
            $"当前账户：{_accountService.CurrentAccountLabel}\n为账户档案输入名称。",
            suggestedName) { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var profile = _accountService.SaveCurrent(prompt.Value, saveAsNew);
            RefreshView();
            FooterStatusText.Text = $"已备份账户：{profile.Name}";
        }
        catch (Exception ex)
        {
            _logger.Error("保存当前账户失败。");
            ShowError(ex.Message, "备份失败");
        }
    }

    private void UpdateBackup_Click(object sender, RoutedEventArgs e)
    {
        if (GetProfile(sender) is not { } profile)
        {
            return;
        }

        try
        {
            _accountService.UpdateCurrentBackup(profile);
            RefreshView();
            FooterStatusText.Text = $"已更新备份：{profile.Name}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "更新备份失败");
        }
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (GetProfile(sender) is not { } profile)
        {
            return;
        }

        var runningProcesses = _switchService.GetRunningCliProcesses();
        var allowWhileRunning = false;
        if (runningProcesses.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"检测到 Antigravity CLI 正在运行：{string.Join("、", runningProcesses)}。\n\n为避免 CLI 会话继续使用切换前的认证信息，建议先关闭它。仍要继续切换吗？",
                "CLI 正在运行", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            allowWhileRunning = true;
        }

        try
        {
            _switchService.SwitchTo(profile, allowWhileRunning);
            ResetQuotaPanel();
            RefreshView();
            FooterStatusText.Text = $"已切换到：{profile.Name}";
            MessageBox.Show(this, $"账户切换成功。\n\n当前账户：{_accountService.CurrentAccountLabel}", "切换成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            RefreshView();
            _logger.Error($"账户切换失败：{profile.Id}");
            ShowError(ex.Message, "切换失败");
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (GetProfile(sender) is not { } profile)
        {
            return;
        }

        var prompt = new InputNameWindow("重命名账户", "新名称只保存在 profile.json 中，不会修改 Google 账户文件。", profile.Name) { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _accountService.Rename(profile, prompt.Value);
            RefreshView();
            FooterStatusText.Text = $"已重命名账户：{prompt.Value}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "重命名失败");
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GetProfile(sender) is not { } profile)
        {
            return;
        }

        try
        {
            var directory = _accountService.GetProfileDirectory(profile);
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "无法打开目录");
        }
    }

    private void OpenGeminiFolder_Click(object sender, RoutedEventArgs e) => OpenDirectory(_accountService.Paths.GeminiDirectory);

    private void OpenAccountsFolder_Click(object sender, RoutedEventArgs e) => OpenDirectory(_accountService.Paths.AccountsDirectory);

    private void OpenDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "无法打开目录");
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (GetProfile(sender) is not { } profile)
        {
            return;
        }

        var isCurrent = _accountService.IsCurrent(profile);
        var warning = isCurrent
            ? "\n\n这是当前正在使用的账户档案。删除只会移除档案副本，不会删除或退出 CLI 当前登录状态。"
            : "\n\n这将删除该账户保存的认证文件和 profile.json。";
        var answer = MessageBox.Show(this,
            $"确定删除账户档案？\n\n{profile.Name}\n{profile.Email ?? "未知邮箱"}{warning}",
            "删除账户", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _accountService.Delete(profile);
            RefreshView();
            FooterStatusText.Text = $"已删除账户档案：{profile.Name}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "删除失败");
        }
    }

    private void CopyEmail_Click(object sender, RoutedEventArgs e)
    {
        var email = _accountService.CurrentAccountLabel;
        if (!string.IsNullOrWhiteSpace(email) && email != "正在读取..." && email != "未识别邮箱")
        {
            try
            {
                Clipboard.SetText(email);
                FooterStatusText.Text = $"已复制当前账户邮箱：{email}";
            }
            catch
            {
                // Best effort clipboard set
            }
        }
    }

    private void ToggleQuotaView_Click(object sender, RoutedEventArgs e)
    {
        _showRawQuotaOutput = !_showRawQuotaOutput;
        ApplyQuotaViewState();
    }

    private void ApplyQuotaViewState()
    {
        if (_showRawQuotaOutput)
        {
            QuotaCardsScrollViewer.Visibility = Visibility.Collapsed;
            QuotaRawBorder.Visibility = Visibility.Visible;
            ToggleQuotaViewButton.Content = "卡片视图";
        }
        else
        {
            QuotaCardsScrollViewer.Visibility = Visibility.Visible;
            QuotaRawBorder.Visibility = Visibility.Collapsed;
            ToggleQuotaViewButton.Content = "原始文本";
            QuotaEmptyPrompt.Visibility = _quotaCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void RefreshQuota_Click(object sender, RoutedEventArgs e)
    {
        if (_quotaQueryRunning)
        {
            return;
        }

        if (!_accountService.CurrentCredentialsExist)
        {
            SetQuotaCountdown(null);
            QuotaStatusText.Text = "无法查询";
            _quotaCards = [];
            QuotaCardsItems.ItemsSource = null;
            QuotaOutputText.Text = "未检测到完整的当前账户认证文件。";
            _showRawQuotaOutput = true;
            ApplyQuotaViewState();
            QuotaUpdatedText.Text = "无法查询";
            return;
        }

        var cliPath = _cliProcessService.FindExecutable(_settings.CliPath);
        if (cliPath is null)
        {
            SetQuotaCountdown(null);
            QuotaStatusText.Text = "未检测到 Antigravity CLI";
            _quotaCards = [];
            QuotaCardsItems.ItemsSource = null;
            QuotaOutputText.Text = "请在设置中选择 agy.exe 或 antigravity.exe。";
            _showRawQuotaOutput = true;
            ApplyQuotaViewState();
            QuotaUpdatedText.Text = "无法查询";
            return;
        }

        _quotaQueryRunning = true;
        RefreshQuotaButton.IsEnabled = false;
        SetQuotaCountdown(null);
        QuotaStatusText.Text = "正在查询...";
        QuotaOutputText.Text = "正在运行 Antigravity CLI /usage。";
        _quotaCards = [];
        QuotaCardsItems.ItemsSource = null;
        ApplyQuotaViewState();
        QuotaUpdatedText.Text = "查询中...";
        RemainingTimeText.Text = "查询中";
        ResetAtText.Text = "正在读取额度重置时间";
        try
        {
            var result = await _quotaProvider.QueryAsync(cliPath, _settings.GeminiDirectory);
            if (result.Succeeded)
            {
                QuotaStatusText.Text = $"查询完成 · {result.QueriedAt:yyyy-MM-dd HH:mm:ss}";
                QuotaOutputText.Text = QuotaOutputFormatter.Format(result.Output);
                _quotaCards = QuotaOutputFormatter.ParseModelCards(result.Output);
                QuotaCardsItems.ItemsSource = _quotaCards;
                _showRawQuotaOutput = _quotaCards.Count == 0;
                ApplyQuotaViewState();
                QuotaUpdatedText.Text = result.QueriedAt.ToString("yyyy-MM-dd HH:mm:ss");
                SetQuotaCountdown(result.ResetAt);
                _logger.Info("用户手动查询额度成功。");
                FooterStatusText.Text = "额度信息已刷新";
                FooterStatusDot.Fill = (Brush)FindResource("SuccessGreen");
            }
            else
            {
                QuotaStatusText.Text = $"查询失败 · {result.QueriedAt:yyyy-MM-dd HH:mm:ss}";
                QuotaOutputText.Text = result.Error ?? "无法获取 Token 使用情况。";
                _quotaCards = [];
                QuotaCardsItems.ItemsSource = null;
                _showRawQuotaOutput = true;
                ApplyQuotaViewState();
                QuotaUpdatedText.Text = "查询失败";
                SetQuotaCountdown(null);
                _logger.Error("用户手动查询额度失败。");
                FooterStatusDot.Fill = (Brush)FindResource("DangerRed");
            }
        }
        catch (Exception ex)
        {
            QuotaStatusText.Text = "查询失败";
            QuotaOutputText.Text = "无法获取 Token 使用情况。请确认 Antigravity CLI 已安装并已登录。";
            _quotaCards = [];
            QuotaCardsItems.ItemsSource = null;
            _showRawQuotaOutput = true;
            ApplyQuotaViewState();
            QuotaUpdatedText.Text = "查询失败";
            SetQuotaCountdown(null);
            _logger.Error($"额度查询异常：{ex.GetType().Name}");
            FooterStatusDot.Fill = (Brush)FindResource("DangerRed");
        }
        finally
        {
            _quotaQueryRunning = false;
            RefreshQuotaButton.IsEnabled = true;
        }
    }

    private void SetQuotaCountdown(DateTimeOffset? resetAt)
    {
        _quotaResetAt = resetAt;
        if (resetAt is null)
        {
            _countdownTimer.Stop();
            RemainingTimeText.Text = "无数据";
            ResetAtText.Text = "CLI 未返回可识别的重置时间";
            return;
        }

        var localResetAt = resetAt.Value.ToLocalTime();
        ResetAtText.Text = $"预计重置于 {localResetAt:yyyy-MM-dd HH:mm:ss}（本地）";
        _countdownTimer.Start();
        UpdateCountdownText();
    }

    private void ResetQuotaPanel()
    {
        SetQuotaCountdown(null);
        QuotaStatusText.Text = "点击「刷新额度」获取模型用量";
        QuotaOutputText.Text = "额度查询结果会显示在这里。";
        _quotaCards = [];
        QuotaCardsItems.ItemsSource = null;
        _showRawQuotaOutput = false;
        ApplyQuotaViewState();
        QuotaUpdatedText.Text = "尚未查询";
    }

    private void UpdateCountdownText()
    {
        if (_quotaResetAt is not { } resetAt)
        {
            _countdownTimer.Stop();
            return;
        }

        var remaining = resetAt - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            RemainingTimeText.Text = "已到重置时间";
            ResetAtText.Text = "请刷新额度以获取重置后的最新状态";
            _countdownTimer.Stop();
            return;
        }

        var days = (int)remaining.TotalDays;
        RemainingTimeText.Text = days > 0
            ? $"{days}天 {remaining:hh\\:mm\\:ss}"
            : $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings, _cliProcessService) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultSettings is null)
        {
            return;
        }

        try
        {
            _settingsRepository.Save(dialog.ResultSettings);
            _settings = dialog.ResultSettings;
            BuildServices();
            ResetQuotaPanel();
            RefreshView();
            FooterStatusText.Text = "设置已保存";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "保存设置失败");
        }
    }

    private static AccountProfile? GetProfile(object sender) =>
        (sender as FrameworkElement)?.Tag as AccountProfile;

    private void ShowError(string message, string title)
    {
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        FooterStatusText.Text = title;
        FooterStatusText.Foreground = (Brush)FindResource("DangerRed");
        FooterStatusDot.Fill = (Brush)FindResource("DangerRed");
    }
}
