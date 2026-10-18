using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CodexQuotaBar.Core;
using Microsoft.Win32;

namespace CodexQuotaBar;

internal sealed class SetupWindow : Window
{
    private readonly string configPath;
    private readonly Func<string, CancellationToken, Task<AccountInspection>> inspect;
    private readonly Action<string> login;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBox executable = new() { MinWidth = 350 };
    private readonly TextBox desktopEmail = new() { MinWidth = 280 };
    private readonly CheckBox confirmed = new() { Content = "已核对：桌面为同一账号的个人空间，额度周期和重置时间与下方一致。", Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 18) };
    private readonly Button check = new() { Content = "检查连接", Padding = new Thickness(14, 8, 14, 8) };
    private readonly Button signIn = new() { Content = "登录自己的账号", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(10, 0, 0, 0) };
    private readonly Button start = new() { Content = "确认并开始使用", Padding = new Thickness(16, 9, 16, 9), IsEnabled = false };
    private AccountInspection? candidate;
    private Task operation = Task.CompletedTask;
    private bool busy, closing, allowClose;
    internal event Action<LiveConfiguration>? Configured;
    internal string ExecutablePath { get => executable.Text; set => executable.Text = value; }
    internal string DesktopEmail { get => desktopEmail.Text; set => desktopEmail.Text = value; }
    internal bool PersonalConfirmed { get => confirmed.IsChecked == true; set => confirmed.IsChecked = value; }
    internal bool CanStart => start.IsEnabled;
    internal string StatusText => status.Text;
    internal bool QueryStopped { get; private set; } = true;

    internal SetupWindow(string configPath, Func<string, CancellationToken, Task<AccountInspection>>? inspect = null,
        Action<string>? login = null)
    {
        this.configPath = configPath;
        this.inspect = inspect ?? ((path, token) => new CodexUsageProvider(path).InspectAsync(token));
        this.login = login ?? (path =>
        {
            var info = new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! };
            info.ArgumentList.Add("login");
            using var process = Process.Start(info);
        });
        Title = "Codex额度条 · 首次使用 / 重新核对";
        Width = 720; Height = 680; MinWidth = 620; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(247, 248, 250));
        var panel = new StackPanel { Margin = new Thickness(28) };
        void Text(string value, int size = 14) => panel.Children.Add(new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        Text("用你自己的账号连接", 24);
        Text("请先打开官方 Codex 桌面端，确认当前使用的是个人空间。本工具只显示该账号的 Codex 套餐额度。");
        Text("1. 检查连接；未登录时，点击登录按钮，在官方浏览器页面亲自完成登录，再回来检查。");
        var buttons = new WrapPanel(); buttons.Children.Add(check); buttons.Children.Add(signIn); panel.Children.Add(buttons);
        panel.Children.Add(status);
        Text("2. 打开 Codex 桌面端的账号菜单，将其中的登录邮箱填在这里：");
        panel.Children.Add(desktopEmail);
        panel.Children.Add(confirmed);
        Text("若桌面与这里显示的账号、空间或额度周期不同，请先完成核对。公司/团队空间暂不支持。");
        panel.Children.Add(start);
        var advanced = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        advanced.Children.Add(new TextBlock { Text = "连接组件位置（已随包提供；通常无需修改）", Margin = new Thickness(0, 0, 0, 6) });
        advanced.Children.Add(executable);
        var browse = new Button { Content = "选择其他 codex.exe", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        advanced.Children.Add(browse);
        panel.Children.Add(new Expander { Header = "高级设置", Content = advanced, Margin = new Thickness(0, 20, 0, 0) });
        Text("账号绑定与显示设置只保存在这台电脑。密码和登录令牌由官方 Codex 管理，本工具不读取凭据文件。");
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetAutomationId(executable, "setup-executable");
        AutomationProperties.SetAutomationId(desktopEmail, "setup-desktop-email");
        AutomationProperties.SetAutomationId(confirmed, "setup-confirm");
        AutomationProperties.SetAutomationId(start, "setup-start");
        executable.Text = File.Exists(AppPaths.BundledCodex) ? AppPaths.BundledCodex : LiveConfiguration.Load(configPath)?.CodexExecutable ?? "";
        status.Text = File.Exists(executable.Text) ? "已找到连接组件，点击“检查连接”。" : "未找到连接组件。请完整解压分享包，或在高级设置中选择 codex.exe。";
        executable.TextChanged += (_, _) => { candidate = null; confirmed.IsChecked = false; UpdateReady(); };
        desktopEmail.TextChanged += (_, _) => UpdateReady();
        confirmed.Checked += (_, _) => UpdateReady(); confirmed.Unchecked += (_, _) => UpdateReady();
        check.Click += async (_, _) => await CheckAsync();
        start.Click += async (_, _) => await CommitAsync();
        signIn.Click += (_, _) => Login();
        browse.Click += (_, _) =>
        {
            if (busy || closing) return;
            var dialog = new OpenFileDialog { Filter = "Codex连接组件 (codex.exe)|codex.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) executable.Text = dialog.FileName;
        };
        Closing += CloseAsync;
        Closed += (_, _) => lifetime.Dispose();
    }
    private void Login()
    {
        if (busy || closing) return;
        candidate = null; confirmed.IsChecked = false; UpdateReady();
        try
        {
            if (!Path.IsPathFullyQualified(executable.Text) || !File.Exists(executable.Text)) throw new IOException();
            login(executable.Text);
            status.Text = "官方登录窗口已打开。请在浏览器中选择自己的账号；登录完成后回到这里点击“检查连接”。";
        }
        catch (Exception error) when (error is IOException or ArgumentException or Win32Exception or InvalidOperationException)
        { status.Text = "登录窗口未能打开。请检查连接组件是否完整，再重试。"; }
    }
    private void UpdateReady()
    {
        start.IsEnabled = !busy && !closing && candidate is not null && confirmed.IsChecked == true &&
            candidate.Snapshot.IdentityConsistent && !string.IsNullOrEmpty(candidate.Snapshot.UsageAccountFingerprint) &&
            IdentityBinding.Fingerprint(desktopEmail.Text) == candidate.Snapshot.AccountFingerprint;
    }
    internal Task CheckAsync() => BeginOperation(false);
    internal Task CommitAsync() => BeginOperation(true);
    private Task BeginOperation(bool commit)
    {
        if (busy || closing) return Task.CompletedTask;
        operation = RunAsync(commit); return operation;
    }
    private async Task RunAsync(bool commit)
    {
        LiveConfiguration? completed = null;
        var selectedPath = executable.Text;
        busy = true; QueryStopped = false; check.IsEnabled = signIn.IsEnabled = executable.IsEnabled = false; UpdateReady();
        try
        {
            if (!Path.IsPathFullyQualified(selectedPath) || !File.Exists(selectedPath)) throw new UsageFailureException(UsageState.Incompatible);
            var previous = candidate;
            if (commit)
            {
                if (previous is null) throw new InvalidOperationException();
                _ = SetupBinding.Confirm(previous.Snapshot, desktopEmail.Text, PersonalConfirmed);
            }
            else { candidate = null; confirmed.IsChecked = false; }
            status.Text = commit ? "正在重新核对账号并保存…" : "正在检查账号与额度…";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var result = await inspect(selectedPath, timeout.Token);
            if (closing) return;
            if (executable.Text != selectedPath) throw new InvalidOperationException();
            if (!result.Snapshot.IdentityConsistent || string.IsNullOrEmpty(result.Snapshot.AccountFingerprint) ||
                string.IsNullOrEmpty(result.Snapshot.UsageAccountFingerprint) || string.IsNullOrWhiteSpace(result.Email))
                throw new InvalidOperationException();
            if (commit && (previous is null || !SetupBinding.SameIdentity(previous.Snapshot, result.Snapshot)))
            {
                candidate = null; confirmed.IsChecked = false;
                status.Text = "检查期间账号或工作区发生变化，请重新检查并核对。"; return;
            }
            candidate = result;
            var quotaLines = result.Snapshot.Groups.SelectMany(g => g.Windows.Select(w =>
                $"{g.Name} · {w.PeriodLabel}：剩余 {w.PercentText}，{w.ResetText(DateTimeOffset.UtcNow)}"));
            status.Text = "连接账号：" + result.Email + "\n" + string.Join("\n", quotaLines) + "\n请与桌面端账号、个人空间及额度周期核对。";
            if (commit)
            {
                var pendingConfig = new LiveConfiguration(selectedPath, SetupBinding.Confirm(result.Snapshot, desktopEmail.Text, PersonalConfirmed));
                pendingConfig.Save(configPath);
                completed = pendingConfig;
            }
        }
        catch (UsageFailureException error)
        {
            candidate = null; confirmed.IsChecked = false;
            status.Text = error.State == UsageState.NeedsLogin ? "尚未登录。请点击“登录自己的账号”，完成后再检查连接。" :
                "暂时无法读取额度。请确认网络可用、使用 ChatGPT 账号登录，并稍后重试。";
        }
        catch (OperationCanceledException) { if (!closing) status.Text = "检查超时，请确认网络可用后重试。"; candidate = null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        { candidate = null; confirmed.IsChecked = false; status.Text = "连接或保存未完成。请核对账号、组件路径和本机文件夹权限，再重新检查。"; }
        finally
        {
            QueryStopped = true; busy = false;
            if (!closing) { check.IsEnabled = signIn.IsEnabled = executable.IsEnabled = true; UpdateReady(); }
        }
        if (completed is not null && !closing)
        {
            var saved = completed;
            _ = Dispatcher.BeginInvoke(new Action(() => { if (!closing) Configured?.Invoke(saved); }));
        }
    }
    private async void CloseAsync(object? sender, CancelEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true; if (closing) return;
        closing = true; lifetime.Cancel(); UpdateReady();
        try { await operation; }
        finally { allowClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }
}



