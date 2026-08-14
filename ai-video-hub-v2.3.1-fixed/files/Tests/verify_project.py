from pathlib import Path
import re, sys, xml.etree.ElementTree as ET, json

root=Path(__file__).resolve().parents[1]
errors=[]

for xaml in [root/'App.xaml', root/'MainWindow.xaml', root/'Dialogs'/'AddAccountDialog.xaml']:
    try: ET.parse(xaml)
    except Exception as e: errors.append(f'XAML parse failed {xaml.name}: {e}')

proj=(root/'AI.VideoHub.csproj').read_text(encoding='utf-8')
for required in ['net8.0-windows10.0.19041.0','Microsoft.Web.WebView2','<Version>2.3.1</Version>']:
    if required not in proj: errors.append('csproj missing '+required)

for xaml_file, code_file in [(root/'MainWindow.xaml',root/'MainWindow.xaml.cs'),(root/'Dialogs'/'AddAccountDialog.xaml',root/'Dialogs'/'AddAccountDialog.xaml.cs')]:
    code=code_file.read_text(encoding='utf-8')
    xaml=xaml_file.read_text(encoding='utf-8')
    for handler in re.findall(r'\b(?:Click|Checked|Unchecked|SelectionChanged)="([A-Za-z0-9_]+)"',xaml):
        if not re.search(r'\b'+re.escape(handler)+r'\s*\(',code): errors.append(f'missing XAML handler {xaml_file.name}:{handler}')

# lightweight brace sanity for C# sources (not a compiler). Skip files with regex/char literals that make raw counts noisy.
for cs in root.rglob('*.cs'):
    if cs.name in {'DoubaoProtocolInspector.cs','DolaProtocolInspector.cs','DolaResponseInspector.cs'}:
        continue
    text=cs.read_text(encoding='utf-8',errors='ignore')
    if text.count('{') != text.count('}'):
        errors.append(f'brace mismatch {cs.relative_to(root)}')

alltext='\n'.join(p.read_text(encoding='utf-8',errors='ignore') for p in root.rglob('*') if p.is_file() and p.suffix in {'.cs','.js','.xaml','.md','.bat','.ps1','.csproj','.json'})
for banned in ['dola.xinsiluhb.cn','/api/license/check','machine_code=']:
    if banned in alltext: errors.append('copied third-party license material: '+banned)
if re.search(r'(?i)(password|api[_-]?key|secret)\s*[=:]\s*["\'][^"\']{8,}',alltext): errors.append('possible hard-coded credential')

js=(root/'Scripts'/'capture.js').read_text(encoding='utf-8')
# It may READ quota/plan words to recognize server rejection/telemetry, but must not write them into outbound JSON.
patch_fn=re.search(r'function patchDurationRecursive\(.*?\n  \}',js,re.S)
if not patch_fn: errors.append('duration patch function missing')
else:
    fn=patch_fn.group(0)
    for privilege in ['vip','membership','subscription','entitlement','quota','plan']:
        if re.search(rf'\b{privilege}\b',fn,re.I): errors.append('duration patch function touches privilege field '+privilege)

for required in ['capability15Rejected','scanTelemetry','mediaScore','__aivhRescan']:
    if required not in js: errors.append('capture script missing '+required)


# WebView2 hotfix invariants: attach control before initialization, bound startup waits, and inject observer after navigation.
main=(root/'MainWindow.xaml.cs').read_text(encoding='utf-8')
web=(root/'Services'/'WebViewSession.cs').read_text(encoding='utf-8')
attach=main.find('BrowserHost.Child = view')
init=main.find('await session.InitializeAsync(profile, token)')
if attach < 0 or init < 0 or attach > init: errors.append('WebView2 must be attached to BrowserHost before InitializeAsync')
if 'DispatcherPriority.Loaded' not in main: errors.append('missing WPF Loaded yield before WebView2 init')
if 'WaitUntilLoadedAsync' not in web: errors.append('missing WebView2 Loaded guard')
if 'TimeSpan.FromSeconds(20)' not in web: errors.append('missing bounded WebView2 initialization timeout')
if 'AddScriptToExecuteOnDocumentCreatedAsync' in web: errors.append('capture script must not inject before page boot')
if 'InstallCaptureScriptOnCurrentDocumentAsync' not in web: errors.append('post-navigation capture installer missing')

dialog=(root/'Dialogs'/'AddAccountDialog.xaml').read_text(encoding='utf-8')
if 'SizeToContent="Height"' not in dialog or 'MinHeight="330"' not in dialog: errors.append('add-account dialog clipping guard missing')
v=json.loads((root/'VERSION.json').read_text(encoding='utf-8'))
if v.get('version')!='2.3.1' or v.get('baseline')!='2.3.1-dola-gate-and-original-fix': errors.append('VERSION.json mismatch')


# V2.1 stability invariants
jsonstore=(root/'Services'/'JsonStore.cs').read_text(encoding='utf-8')
for required in ['ConcurrentDictionary<string, SemaphoreSlim>','FileOptions.WriteThrough','PreserveCorruptFile','DescribeHealth','.bak']:
    if required not in jsonstore: errors.append('JsonStore stability feature missing '+required)
app=(root/'App.xaml.cs').read_text(encoding='utf-8')
for required in ['SingleInstanceGuard','--selftest-storage','DispatcherUnhandledException']:
    if required not in app: errors.append('App stability feature missing '+required)
if 'StartupUri=' in (root/'App.xaml').read_text(encoding='utf-8'): errors.append('App.xaml must not auto-create MainWindow before single-instance guard')
if 'BrowserProcessFailed' not in web or 'SessionId' not in web: errors.append('WebView crash/session isolation hook missing')
parser=(root/'Services'/'CaptureMessageParser.cs').read_text(encoding='utf-8')
if 'CaptureContext context' not in parser: errors.append('capture parser context binding missing')
if 'hostAllowed' not in js or 'window.__aivhPlatformPolicy' not in js: errors.append('platform capture policy missing')
if '/^(POST|PUT|PATCH)$/i.test(method)' not in js: errors.append('duration patch method guard missing')
if 'allowUnverified15Trial' not in js: errors.append('explicit 15s user-trial guard missing')
if '__aivhSetDurationOverride(15,true)' not in (root/'Tests'/'test_capture.js').read_text(encoding='utf-8'): errors.append('15s trial regression missing')
download=(root/'Services'/'DownloadService.cs').read_text(encoding='utf-8')
for required in ['for (var attempt = 1; attempt <= 3; attempt++)','request.Headers.Referrer','FileOptions.WriteThrough','ContentLength']:
    if required not in download: errors.append('download stability feature missing '+required)
main=(root/'MainWindow.xaml.cs').read_text(encoding='utf-8')
for required in ['IsCurrentCapture','HandleBrowserProcessFailureAsync','AutoRecoverTasksForCurrentProfileAsync','RunUiAsync']:
    if required not in main: errors.append('MainWindow stability feature missing '+required)

# V2.2 Doubao host-network protocol invariants
inspector=(root/'Services'/'DoubaoProtocolInspector.cs').read_text(encoding='utf-8')
for required in ['InspectAndMaybePatchRequest','ScanResponse','IsStrongVideoUrl','no_watermark','original']:
    if required not in inspector: errors.append('Doubao protocol inspector missing '+required)
if 'WebResourceRequested' not in web or 'WebResourceResponseReceived' not in web:
    errors.append('host-level WebView2 network interception missing')
if 'HostNetwork' not in web: errors.append('host network request source marker missing')
if "'url'" in re.search(r'const mediaKeys = \[(.*?)\];',js,re.S).group(1): errors.append('generic url media key must not be treated as video')
if 'heic' not in js.lower() or 'heic' not in inspector.lower(): errors.append('image false-positive guard missing')
media_ranker=(root/'Services'/'MediaRanker.cs').read_text(encoding='utf-8')
if 'ChooseBestOriginal' not in media_ranker or 'IsUsableVideo' not in media_ranker: errors.append('verified-original media gate missing')


# V2.3.1 Dola regression invariants
main_xaml=(root/'MainWindow.xaml').read_text(encoding='utf-8')
dola_inspector=(root/'Services'/'DolaProtocolInspector.cs').read_text(encoding='utf-8')
dola_response=(root/'Services'/'DolaResponseInspector.cs').read_text(encoding='utf-8')
dola_media=(root/'Services'/'DolaMediaResolver.cs').read_text(encoding='utf-8')
dola_capture=(root/'Scripts'/'dola_capture.js').read_text(encoding='utf-8')
protocol_test=(root/'Services'/'ProtocolSelfTest.cs').read_text(encoding='utf-8')
if 'Dola 15 秒模式' not in main_xaml or '检测 Dola 15 秒能力' not in main_xaml: errors.append('Dola 15-second UI missing')
if 'CurrentProfile?.Platform, "Dola"' not in main: errors.append('15-second UI platform gate is not Dola')
if 'allowUnverifiedTrial' in main: errors.append('MainWindow still exposes unverified 15-second trial')
if 'serverAdvertised15' not in dola_inspector or 'enable15 && serverAdvertised15' not in dola_inspector: errors.append('Dola protocol bottom-layer capability gate missing')
for required in ['supported_durations','max_duration','Capability15Evidence']:
    if required not in dola_response: errors.append('Dola response capability detector missing '+required)
for required in ['web_tab_id','/samantha/media/get_play_info','video_model','use-olympus-account=1']:
    if required not in dola_capture: errors.append('Dola capture parity feature missing '+required)
for required in ['NormalizeExplicitOriginalUrl','video_gen_no_watermark','LooksLikelyWatermarked','original_media_info','no_watermark_url']:
    if required not in dola_media: errors.append('Dola original resolver missing '+required)
if 'DolaProtocolLearned +=' not in main: errors.append('Dola runtime protocol snapshot is not persisted by MainWindow')
for required in ['serverAdvertised15: false','serverAdvertised15: true','Unrelated numeric 15','lr=video_gen_no_watermark']:
    if required not in protocol_test: errors.append('V2.3.1 protocol regression test missing '+required)

if errors:
    print('\n'.join(errors)); sys.exit(1)
print('project static checks passed')
