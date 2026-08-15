using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private WebViewSession? _session;
    private int? _lastSubmittedDuration;
    private string _localVideo = "";

    public MainWindow()
    {
        InitializeComponent();
        AccountList.ItemsSource = _accounts;
        DiagnosticLog.LineWritten += line => Dispatcher.Invoke(() => { LogBox.AppendText(line + Environment.NewLine); LogBox.ScrollToEnd(); });
        Loaded += async (_, _) => await LoadAccountsAsync();
        Closing += async (_, _) => { if (_session is not null) await _session.DisposeAsync(); await _accountStore.SaveAsync(_accounts.ToList()); };
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
        SubmitButton.IsEnabled = false;
        if (_session is not null) await _session.DisposeAsync();
        Browser = new Microsoft.Web.WebView2.Wpf.WebView2();
        ((Grid)((Grid)Content).Children[1]).Children.RemoveAt(1);
        Grid.SetRow(Browser, 1);
        ((Grid)((Grid)Content).Children[1]).Children.Insert(1, Browser);
        _session = new WebViewSession(Browser, _accountStore, account);
        PageTitle.Text = account.ToString();
        await _session.InitializeAsync();
        if (_session.DolaObserver is not null)
        {
            _session.DolaObserver.StateChanged += state => Dispatcher.Invoke(() => UpdateProtocolUi(state));
            _session.Media.Changed += () => Dispatcher.Invoke(UpdateMediaUi);
            UpdateProtocolUi(_session.DolaObserver.State);
            UpdateMediaUi();
        }
        else
        {
            ProtocolStatus.Text = "当前 dev1 的 P0 协议学习先聚焦 Dola";
            SubmitHint.Text = "豆包/千问保留独立账号与 WebView2 基础；视频协议将在后续适配。";
        }
        await _accountStore.SaveAsync(_accounts.ToList());
    }

    private void UpdateProtocolUi(DolaProtocolState state)
    {
        var learned = state.LastVideoRequest is not null;
        ProtocolStatus.Text = $"Dola协议: {(learned ? "已学习" : "未学习")} · 15秒: {(state.ServerAdvertised15 ? "服务端已声明" : "未声明")}";
        SubmitButton.IsEnabled = learned;
        SubmitHint.Text = learned
            ? $"真实模板：{new Uri(state.LastVideoRequest!.Url).AbsolutePath}\n时长字段：{state.LastVideoRequest.DurationPath}\n15秒证据：{(state.ServerAdvertised15 ? state.Capability15Evidence : "无")}" 
            : "请在当前 Dola 页面正常提交一次可用的视频任务，V3 会只读观察并记录真实模板。";
    }

    private void UpdateMediaUi()
    {
        if (_session is null) return;
        var best = _session.Media.BestExplicitOriginal(_session.DolaObserver?.State.LastKnownVid);
        OriginalInfo.Text = best is null ? "尚未发现明确 original/no_watermark 资源。" : $"已发现明确原片证据\nVID: {best.Vid}\n来源: {best.SourcePath}\n{best.Width}x{best.Height}";
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.DolaSubmission is null) { MessageBox.Show("当前账号不是 Dola 或 Dola 会话尚未初始化。"); return; }
        var duration = int.Parse(((ComboBoxItem)DurationBox.SelectedItem).Content.ToString()!);
        var ratio = ((ComboBoxItem)RatioBox.SelectedItem).Content.ToString()!;
        var result = await _session.DolaSubmission.SubmitAsync(new VideoGenerationRequest { Prompt = PromptBox.Text.Trim(), AspectRatio = ratio, DurationSeconds = duration });
        if (result.Success) _lastSubmittedDuration = duration;
        MessageBox.Show(result.Success ? $"HTTP 层已接受（{result.HttpStatus}）。\n注意：这还不等于 {duration} 秒功能 PASS，必须等成片后验证实际时长。" : result.Error, result.Success ? "提交完成" : "提交失败");
    }

    private async void DownloadOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (RightsCheck.IsChecked != true) { MessageBox.Show("请先确认素材为本人创作或已获授权。", "版权确认"); return; }
        var vid = _session.DolaObserver?.State.LastKnownVid ?? "";
        MediaResource? best = null;
        if (!string.IsNullOrWhiteSpace(vid) && _session.DolaOriginalResolver is not null)
        {
            best = await _session.DolaOriginalResolver.ResolveAsync(vid);
            if (best is not null) _session.Media.Add(best);
        }
        best ??= _session.Media.BestExplicitOriginal(vid);
        if (best is null) { MessageBox.Show("当前 VID 没有解析到明确 original/no_watermark 原片字段。程序不会把普通播放地址冒充原片。" ); return; }
        try
        {
            var path = await new DownloadService().DownloadAsync(_session.Core, best, AppPaths.Downloads);
            var probe = await new MediaProbeService().VerifyVideoAsync(path, _lastSubmittedDuration);
            var verdict = _lastSubmittedDuration is null
                ? new VideoP0VerdictResult(false, "未绑定本次提交任务，仅完成媒体文件验证。")
                : VideoP0Verdict.Evaluate(_session.DolaObserver!.State, _lastSubmittedDuration.Value, probe);
            MessageBox.Show($"已保存：\n{path}\n\n{probe.Message}\n{verdict.Message}", "下载完成");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "下载失败"); }
    }


    private void ChooseLocalVideo_Click(object sender, RoutedEventArgs e)
    {
        if (RightsCheck.IsChecked != true) { MessageBox.Show("请先确认素材为本人创作或已获授权。", "版权确认"); return; }
        var dlg = new OpenFileDialog { Filter = "视频文件|*.mp4;*.mov;*.m4v;*.webm|所有文件|*.*" };
        if (dlg.ShowDialog(this) == true)
        {
            _localVideo = dlg.FileName;
            LocalVideoPath.Text = dlg.FileName;
        }
    }

    private async void LocalDelogo_Click(object sender, RoutedEventArgs e)
    {
        if (RightsCheck.IsChecked != true) { MessageBox.Show("请先确认素材为本人创作或已获授权。", "版权确认"); return; }
        if (string.IsNullOrWhiteSpace(_localVideo) || !File.Exists(_localVideo)) { MessageBox.Show("请先选择本地视频。"); return; }
        if (!int.TryParse(WatermarkX.Text, out var x) || !int.TryParse(WatermarkY.Text, out var y) || !int.TryParse(WatermarkW.Text, out var w) || !int.TryParse(WatermarkH.Text, out var h))
        { MessageBox.Show("水印区域必须填写整数：X、Y、宽、高。"); return; }
        try
        {
            Directory.CreateDirectory(AppPaths.Downloads);
            var output = Path.Combine(AppPaths.Downloads, $"local_delogo_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            await new FfmpegWatermarkService().RemoveAuthorizedWatermarkRegionAsync(_localVideo, output, x, y, w, h);
            var probe = await new MediaProbeService().VerifyVideoAsync(output);
            MessageBox.Show($"本地处理完成：\n{output}\n\n{probe.Message}", "完成");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "本地处理失败"); }
    }

    private void OpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.Downloads);
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Downloads) { UseShellExecute = true });
    }
}
