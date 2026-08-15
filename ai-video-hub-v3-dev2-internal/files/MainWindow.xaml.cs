using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AI.VideoHub.V3.Models;
using AI.VideoHub.V3.Services;

namespace AI.VideoHub.V3;

public partial class MainWindow : Window
{
    private readonly AccountStore _accountStore = new();
    private readonly ObservableCollection<AccountProfile> _accounts = [];
    private readonly SemaphoreSlim _sessionSwitchGate = new(1, 1);
    private WebViewSession? _session;
    private int? _lastSubmittedDuration;
    private string _lastSubmittedTaskId = "";
    private string _lastSubmittedAccountId = "";
    private string _localVideo = "";
    private bool _submitBusy;

    public MainWindow()
    {
        InitializeComponent();
        AccountList.ItemsSource = _accounts;
        DiagnosticLog.LineWritten += line => Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
        Loaded += async (_, _) => await LoadAccountsAsync();
        Closing += (_, _) =>
        {
            try { _session?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception ex) { DiagnosticLog.Write("Session dispose on close failed: " + ex.Message); }
            try { _accountStore.SaveAsync(_accounts.ToList()).GetAwaiter().GetResult(); }
            catch (Exception ex) { DiagnosticLog.Write("Account save on close failed: " + ex.Message); }
        };
    }

    private async Task LoadAccountsAsync()
    {
        foreach (var account in await _accountStore.LoadAsync()) _accounts.Add(account);
        if (_accounts.Count > 0) AccountList.SelectedIndex = 0;
    }

    private async void AddDola_Click(object sender, RoutedEventArgs e) => await AddAccountAsync(PlatformKind.Dola);
    private async void AddDoubao_Click(object sender, RoutedEventArgs e) => await AddAccountAsync(PlatformKind.Doubao);
    private async void AddQianwen_Click(object sender, RoutedEventArgs e) => await AddAccountAsync(PlatformKind.Qianwen);

    private async Task AddAccountAsync(PlatformKind platform)
    {
        var same = _accounts.Count(x => x.Platform == platform) + 1;
        var account = new AccountProfile { DisplayName = $"{platform} {same}", Platform = platform };
        _accounts.Add(account);
        await _accountStore.SaveAsync(_accounts.ToList());
        AccountList.SelectedItem = account;
    }

    private async void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountList.SelectedItem is not AccountProfile account) return;
        await OpenAccountAsync(account);
    }

    private async Task OpenAccountAsync(AccountProfile account)
    {
        await _sessionSwitchGate.WaitAsync();
        AccountList.IsEnabled = false;
        SubmitButton.IsEnabled = false;
        _lastSubmittedDuration = null;
        _lastSubmittedTaskId = "";
        _lastSubmittedAccountId = "";

        try
        {
            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }

            Browser = new Microsoft.Web.WebView2.Wpf.WebView2();
            var center = (Grid)((Grid)Content).Children[1];
            if (center.Children.Count > 1) center.Children.RemoveAt(1);
            Grid.SetRow(Browser, 1);
            center.Children.Insert(1, Browser);

            var next = new WebViewSession(Browser, _accountStore, account);
            _session = next;
            PageTitle.Text = account.ToString();
            await next.InitializeAsync();

            if (next.DolaObserver is not null)
            {
                next.DolaObserver.StateChanged += state => Dispatcher.Invoke(() => UpdateProtocolUi(state));
                next.Media.Changed += () => Dispatcher.Invoke(UpdateMediaUi);
                UpdateProtocolUi(next.DolaObserver.State);
                UpdateMediaUi();
            }
            else
            {
                ProtocolStatus.Text = "当前 RC 协议学习先聚焦 Dola";
                SubmitHint.Text = "豆包/千问保留独立账号与 WebView2 登录态；视频协议将在后续适配。";
            }

            await _accountStore.SaveAsync(_accounts.ToList());
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("Open account failed: " + ex);
            SubmitButton.IsEnabled = false;
            ProtocolStatus.Text = "账号打开失败";
            SubmitHint.Text = "WebView2 初始化或页面打开失败，请查看下方日志。";
            MessageBox.Show("账号打开失败：\n" + ex.Message, "AI Video Hub");
        }
        finally
        {
            AccountList.IsEnabled = true;
            _sessionSwitchGate.Release();
        }
    }

    private void UpdateProtocolUi(DolaProtocolState state)
    {
        var learned = state.LastVideoRequest is not null;
        var cooldown = state.VideoCooldownUntilUtc is DateTime until && until > DateTime.UtcNow;
        var quotaEmpty = state.RemainingVideoCount == 0;
        var blocked = state.HasGeneratingTask || cooldown || quotaEmpty;

        ProtocolStatus.Text = $"Dola协议: {(learned ? "已学习" : "未学习")} · 15秒: {(state.ServerAdvertised15 ? "服务端已声明" : "未声明")} · 任务: {(string.IsNullOrWhiteSpace(state.LastTaskStatus) ? "无" : state.LastTaskStatus)}";
        SubmitButton.IsEnabled = learned && !_submitBusy && !blocked;

        if (!learned)
        {
            SubmitHint.Text = "请在当前 Dola 页面正常提交一次可用的视频任务，V3 会只读观察并记录真实模板。";
            return;
        }

        var blockText = state.HasGeneratingTask ? "\n当前已有生成任务，已禁止重复提交。" :
            cooldown ? $"\n当前冷却至 {state.VideoCooldownUntilUtc:yyyy-MM-dd HH:mm:ss} UTC。" :
            quotaEmpty ? "\n当前观测到剩余视频额度为 0。" : "";
        SubmitHint.Text = $"真实模板：{new Uri(state.LastVideoRequest!.Url).AbsolutePath}\n时长字段：{state.LastVideoRequest.DurationPath}\n15秒证据：{(state.ServerAdvertised15 ? state.Capability15Evidence : "无")}{blockText}";
    }

    private void UpdateMediaUi()
    {
        if (_session is null) return;
        var best = _session.Media.BestExplicitOriginal(_session.DolaObserver?.State.LastKnownVid);
        OriginalInfo.Text = best is null
            ? "尚未发现明确 original/no_watermark 资源。"
            : $"已发现明确原片证据\nVID: {best.Vid}\n来源: {best.SourcePath}\n{best.Width}x{best.Height}";
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (_submitBusy) return;
        if (_session?.DolaSubmission is null || _session.DolaObserver is null)
        {
            MessageBox.Show("当前账号不是 Dola 或 Dola 会话尚未初始化。");
            return;
        }
        if (_session.DolaObserver.State.HasGeneratingTask)
        {
            MessageBox.Show("当前账号已有生成中的任务，请等待完成后再提交。", "避免重复提交");
            return;
        }
        if (string.IsNullOrWhiteSpace(PromptBox.Text))
        {
            MessageBox.Show("请输入视频提示词。", "输入检查");
            return;
        }

        var duration = int.Parse(((ComboBoxItem)DurationBox.SelectedItem).Content.ToString()!);
        var ratio = ((ComboBoxItem)RatioBox.SelectedItem).Content.ToString()!;
        _submitBusy = true;
        SubmitButton.IsEnabled = false;
        try
        {
            var result = await _session.DolaSubmission.SubmitAsync(new VideoGenerationRequest
            {
                Prompt = PromptBox.Text.Trim(),
                AspectRatio = ratio,
                DurationSeconds = duration
            });

            if (result.Success)
            {
                _lastSubmittedDuration = duration;
                _lastSubmittedTaskId = result.TaskId;
                _lastSubmittedAccountId = _session.Account.Id;
            }

            var identity = result.Success && string.IsNullOrWhiteSpace(result.TaskId)
                ? "\nHTTP 已接受，但提交响应未暴露 task_id：本次任务不会被标记为 P0 PASS，直到身份可确认。"
                : result.Success ? $"\n本次 task_id：{result.TaskId}" : "";
            MessageBox.Show(
                result.Success
                    ? $"HTTP 层已接受（{result.HttpStatus}）。{identity}\n注意：这还不等于 {duration} 秒功能 PASS，必须等同一 task_id 的任务完成，并验证同一 VID 成片实际时长。"
                    : result.Error,
                result.Success ? "提交完成" : "提交失败");
        }
        finally
        {
            _submitBusy = false;
            if (_session?.DolaObserver is not null) UpdateProtocolUi(_session.DolaObserver.State);
        }
    }

    private async void DownloadOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (RightsCheck.IsChecked != true)
        {
            MessageBox.Show("请先确认素材为本人创作或已获授权。", "版权确认");
            return;
        }

        var button = sender as Button;
        if (button is not null) button.IsEnabled = false;
        try
        {
            var vid = _session.DolaObserver?.State.LastKnownVid ?? "";
            MediaResource? best = null;
            if (!string.IsNullOrWhiteSpace(vid) && _session.DolaOriginalResolver is not null)
            {
                best = await _session.DolaOriginalResolver.ResolveAsync(vid);
                if (best is not null) _session.Media.Add(best);
            }
            best ??= _session.Media.BestExplicitOriginal(vid);
            if (best is null)
            {
                MessageBox.Show("当前 VID 没有解析到明确 original/no_watermark 原片字段。程序不会把普通播放地址冒充原片。");
                return;
            }

            var path = await new DownloadService().DownloadAsync(_session.Core, best, AppPaths.Downloads);
            var expected = _lastSubmittedAccountId == _session.Account.Id ? _lastSubmittedDuration : null;
            var probe = await new MediaProbeService().VerifyVideoAsync(path, expected);
            var verdict = expected is null
                ? new VideoP0VerdictResult(false, "未绑定当前账号的本次提交任务，仅完成媒体文件验证。")
                : VideoP0Verdict.Evaluate(_session.DolaObserver!.State, expected.Value, probe, _lastSubmittedTaskId, best);
            MessageBox.Show($"已保存：\n{path}\n\n{probe.Message}\n{verdict.Message}", "下载完成");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "下载失败");
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
    }

    private void ChooseLocalVideo_Click(object sender, RoutedEventArgs e)
    {
        if (RightsCheck.IsChecked != true)
        {
            MessageBox.Show("请先确认素材为本人创作或已获授权。", "版权确认");
            return;
        }
        var dlg = new OpenFileDialog { Filter = "视频文件|*.mp4;*.mov;*.m4v;*.webm|所有文件|*.*" };
        if (dlg.ShowDialog(this) == true)
        {
            _localVideo = dlg.FileName;
            LocalVideoPath.Text = dlg.FileName;
        }
    }

    private async void LocalDelogo_Click(object sender, RoutedEventArgs e)
    {
        if (RightsCheck.IsChecked != true)
        {
            MessageBox.Show("请先确认素材为本人创作或已获授权。", "版权确认");
            return;
        }
        if (string.IsNullOrWhiteSpace(_localVideo) || !File.Exists(_localVideo))
        {
            MessageBox.Show("请先选择本地视频。");
            return;
        }
        if (!int.TryParse(WatermarkX.Text, out var x) || !int.TryParse(WatermarkY.Text, out var y) ||
            !int.TryParse(WatermarkW.Text, out var w) || !int.TryParse(WatermarkH.Text, out var h))
        {
            MessageBox.Show("水印区域必须填写整数：X、Y、宽、高。");
            return;
        }

        var button = sender as Button;
        if (button is not null) button.IsEnabled = false;
        try
        {
            Directory.CreateDirectory(AppPaths.Downloads);
            var output = Path.Combine(AppPaths.Downloads, $"local_delogo_{DateTime.Now:yyyyMMdd_HHmmss_fff}.mp4");
            await new FfmpegWatermarkService().RemoveAuthorizedWatermarkRegionAsync(_localVideo, output, x, y, w, h);
            var probe = await new MediaProbeService().VerifyVideoAsync(output);
            MessageBox.Show($"本地处理完成：\n{output}\n\n{probe.Message}", "完成");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "本地处理失败");
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
    }

    private void OpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.Downloads);
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Downloads) { UseShellExecute = true });
    }
}
