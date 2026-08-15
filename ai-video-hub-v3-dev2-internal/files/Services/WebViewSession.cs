using AI.VideoHub.V3.Models;
using AI.VideoHub.V3.Platforms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AI.VideoHub.V3.Services;

public sealed class WebViewSession : IAsyncDisposable
{
    private readonly WebView2 _webView;
    private readonly AccountStore _accounts;
    public AccountProfile Account { get; }
    public DolaProtocolObserver? DolaObserver { get; private set; }
    public DolaVideoSubmissionService? DolaSubmission { get; private set; }
    public DolaOriginalMediaResolver? DolaOriginalResolver { get; private set; }
    public MediaCatalog Media { get; } = new();

    public WebViewSession(WebView2 webView, AccountStore accounts, AccountProfile account)
    {
        _webView = webView;
        _accounts = accounts;
        Account = account;
    }

    public async Task InitializeAsync()
    {
        var profileDir = _accounts.GetProfileDirectory(Account);
        var options = new CoreWebView2EnvironmentOptions("--disable-features=msEdgeSidebarV2");
        var env = await CoreWebView2Environment.CreateAsync(null, profileDir, options);
        await _webView.EnsureCoreWebView2Async(env);
        var core = _webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };
        core.ProcessFailed += (_, e) => DiagnosticLog.Write("WebView2 process failed: " + e.ProcessFailedKind);
        if (Account.Platform == PlatformKind.Dola)
        {
            DolaObserver = new DolaProtocolObserver(core, Account.Id);
            DolaObserver.MediaObserved += Media.Add;
            await DolaObserver.StartAsync();
            DolaSubmission = new DolaVideoSubmissionService(core, DolaObserver);
            DolaOriginalResolver = new DolaOriginalMediaResolver(core);
        }
        var def = PlatformDefinition.For(Account.Platform);
        core.Navigate(def.HomeUrl);
        Account.LastOpenedAtUtc = DateTime.UtcNow;
    }

    public CoreWebView2 Core => _webView.CoreWebView2;

    public async ValueTask DisposeAsync()
    {
        if (DolaObserver is not null) await DolaObserver.DisposeAsync();
        _webView.Dispose();
    }
}
