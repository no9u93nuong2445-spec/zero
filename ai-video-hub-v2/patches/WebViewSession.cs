using AI.VideoHub.Models;
using AI.VideoHub.Platforms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AI.VideoHub.Services;

public sealed class WebViewSession : IAsyncDisposable
{
    private readonly AccountStore _accounts;
    private readonly CaptureMessageParser _parser;
    private readonly AppSettings _settings;
    private readonly DiagnosticLog _log;
    private readonly string _captureScript;

    public WebView2 WebView { get; private set; } = new();
    public AccountProfile? CurrentProfile { get; private set; }
    public bool ServerAdvertised15 { get; private set; }
    public bool Override15Enabled { get; private set; }

    public WebViewSession(AccountStore accounts, CaptureMessageParser parser, AppSettings settings, DiagnosticLog log)
    {
        _accounts = accounts; _parser = parser; _settings = settings; _log = log;
        _captureScript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripts", "capture.js"));
    }

    public async Task<WebView2> OpenAsync(AccountProfile profile)
    {
        await DisposeWebViewAsync();
        CurrentProfile = profile; ServerAdvertised15 = false; Override15Enabled = false;
        var env = await CoreWebView2Environment.CreateAsync(null, _accounts.ProfileDirectory(profile), null);
        WebView = new WebView2();
        await WebView.EnsureCoreWebView2Async(env);
        var core = WebView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = _settings.DeveloperToolsEnabled;
        core.Settings.IsStatusBarEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.WebMessageReceived += (_, e) => { try { _parser.Handle(e.TryGetWebMessageAsString()); } catch (Exception ex) { _log.Error(ex.Message); } };
        core.NavigationStarting += (_, e) => { if (!IsAllowedNavigation(profile, e.Uri)) _log.Info("external navigation: " + SafeUri(e.Uri)); };
        await core.AddScriptToExecuteOnDocumentCreatedAsync(_captureScript);
        core.Navigate(profile.HomeUrl);
        return WebView;
    }

    public void Mark15Advertised() => ServerAdvertised15 = true;
    public void Mark15Rejected() { ServerAdvertised15 = false; Override15Enabled = false; }

    public async Task<bool> Set15SecondModeAsync(bool enabled)
    {
        if (WebView.CoreWebView2 is null) return false;
        if (enabled && !ServerAdvertised15) return false;
        var js = enabled
            ? "window.__aivhSetDurationOverride&&window.__aivhSetDurationOverride(15);"
            : "window.__aivhSetDurationOverride&&window.__aivhSetDurationOverride(null);";
        var result = await WebView.CoreWebView2.ExecuteScriptAsync(js);
        Override15Enabled = enabled;
        _log.Info($"15-second mode={enabled}; result={result}");
        return true;
    }

    public async Task<bool> FillPromptAsync(string prompt)
    {
        if (WebView.CoreWebView2 is null || string.IsNullOrWhiteSpace(prompt)) return false;
        var encoded = System.Text.Json.JsonSerializer.Serialize(prompt);
        var js = $@"(() => {{
            const value = {encoded};
            const visible = el => !!(el && (el.offsetWidth || el.offsetHeight || el.getClientRects().length));
            const candidates = [
              ...document.querySelectorAll('textarea'),
              ...document.querySelectorAll('[contenteditable=""true""]'),
              ...document.querySelectorAll('input[type=""text""]')
            ].filter(visible);
            const el = candidates.find(x => /prompt|提示|描述|想法|输入/i.test((x.getAttribute('placeholder')||'') + ' ' + (x.getAttribute('aria-label')||''))) || candidates[0];
            if (!el) return false;
            el.focus();
            if ('value' in el) {{
              const proto = Object.getPrototypeOf(el);
              const desc = Object.getOwnPropertyDescriptor(proto, 'value');
              if (desc && desc.set) desc.set.call(el, value); else el.value = value;
            }} else {{
              el.textContent = value;
            }}
            el.dispatchEvent(new InputEvent('input', {{ bubbles:true, inputType:'insertText', data:value }}));
            el.dispatchEvent(new Event('change', {{ bubbles:true }}));
            return true;
        }})()";
        try
        {
            var result = await WebView.CoreWebView2.ExecuteScriptAsync(js);
            return string.Equals(result?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log.Error("fill prompt: " + ex.Message);
            return false;
        }
    }

    public async Task RescanAsync()
    {
        if (WebView.CoreWebView2 is null) return;
        try { await WebView.CoreWebView2.ExecuteScriptAsync("window.__aivhRescan&&window.__aivhRescan();"); }
        catch (Exception ex) { _log.Error("rescan: " + ex.Message); }
    }

    public Task RefreshAsync() { WebView.CoreWebView2?.Reload(); return Task.CompletedTask; }

    private static bool IsAllowedNavigation(AccountProfile p, string? u)
    {
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) return false;
        var platform = PlatformRegistry.Resolve(p.Platform);
        return platform.AllowedHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    private static string SafeUri(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u)) return "invalid";
        return u.GetLeftPart(UriPartial.Path);
    }

    private Task DisposeWebViewAsync() { try { WebView.Dispose(); } catch { } return Task.CompletedTask; }
    public async ValueTask DisposeAsync() => await DisposeWebViewAsync();
}
