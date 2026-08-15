from pathlib import Path
import json, sys, xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1]
errors=[]
def req(path):
    p=root/path
    if not p.exists(): errors.append(f"missing {path}")
    return p
for f in ["AI.VideoHub.V3.csproj","App.xaml","MainWindow.xaml","MainWindow.xaml.cs","Services/DolaProtocolObserver.cs","Services/DolaVideoSubmissionService.cs","Services/DolaOriginalMediaResolver.cs","Services/DownloadService.cs","Services/MediaProbeService.cs","Services/FfmpegWatermarkService.cs","VERSION.json"]: req(f)
try: ET.parse(root/"MainWindow.xaml")
except Exception as e: errors.append(f"xaml xml: {e}")
try:
    version=json.loads((root/"VERSION.json").read_text(encoding="utf-8"))
    if version.get("version") != "3.0.0-dev2-internal": errors.append("version mismatch")
except Exception as e: errors.append(f"version json: {e}")
allcs="\n".join(p.read_text(encoding="utf-8") for p in root.rglob("*.cs"))
js="\n".join(p.read_text(encoding="utf-8") for p in root.rglob("*.js"))
for banned in ["PatchVideoDurations", "InspectAndMaybePatchRequest", "allowUnverified15Trial"]:
    if banned in allcs: errors.append(f"banned legacy mutation symbol present: {banned}")
for banned in ["window.fetch =", "XMLHttpRequest.prototype.send =", "__aivhSetDurationOverride"]:
    if banned in js: errors.append(f"banned JS interceptor present: {banned}")
for required in ["Network.requestWillBeSent", "Network.responseReceived", "ServerAdvertised15", "DurationPath", "ffprobe"]:
    if required not in allcs: errors.append(f"required invariant missing: {required}")
if "ExplicitOriginal" not in allcs: errors.append("explicit-original gate missing")
resolver = (root / 'Services' / 'DolaOriginalMediaResolver.cs').read_text(encoding='utf-8')
if '/samantha/media/get_play_info' not in resolver: errors.append('get_play_info resolver missing')
if not all(x in resolver for x in ['original_media_info','no_watermark_url','original_url']): errors.append('explicit original fields missing')
if 'watermark=1' in resolver or 'watermark=0' in resolver: errors.append('resolver must not rewrite watermark flags')
main = (root / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
if 'DolaOriginalResolver.ResolveAsync' not in main: errors.append('active original resolver not wired')
ff = (root / 'Services' / 'FfmpegWatermarkService.cs').read_text(encoding='utf-8')
if 'GetDimensionsAsync' not in ff or 'Math.Clamp' not in ff: errors.append('safe delogo bounds missing')
if errors:
    print('FAIL')
    print('\n'.join('- '+e for e in errors))
    sys.exit(1)
print('PASS: V3 dev2 P0 static invariants verified')
