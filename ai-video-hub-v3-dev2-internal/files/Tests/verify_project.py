from pathlib import Path
import json, re, sys, xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1]
errors=[]
def req(path):
    p=root/path
    if not p.exists(): errors.append(f"missing {path}")
    return p
for f in ["AI.VideoHub.V3.csproj","App.xaml","MainWindow.xaml","MainWindow.xaml.cs","Services/DolaProtocolObserver.cs","Services/DolaVideoSubmissionService.cs","Services/DolaLifecycleInspector.cs","Services/VideoP0Verdict.cs","Services/DownloadService.cs","Services/MediaProbeService.cs","VERSION.json"]: req(f)
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
if errors:
    print("FAIL")
    print("\n".join("- "+e for e in errors))
    sys.exit(1)
print("PASS: V3 dev2 base static invariants verified")

# V3 dev2 P0 invariants
resolver = (root / 'Services' / 'DolaOriginalMediaResolver.cs').read_text(encoding='utf-8')
assert '/samantha/media/get_play_info' in resolver
assert 'original_media_info' in resolver and 'no_watermark_url' in resolver and 'original_url' in resolver
assert 'watermark=1' not in resolver and 'watermark=0' not in resolver, 'resolver must not rewrite watermark flags'
main = (root / 'MainWindow.xaml.cs').read_text(encoding='utf-8')
assert 'DolaOriginalResolver.ResolveAsync' in main
ff = (root / 'Services' / 'FfmpegWatermarkService.cs').read_text(encoding='utf-8')
assert 'GetDimensionsAsync' in ff and 'Math.Clamp' in ff
print('PASS: V3 dev2 P0 static invariants verified')

# 15-second lifecycle invariants
for required in ["LastTaskStatus", "HasGeneratingTask", "LastTaskDurationSeconds", "LastLifecycleEvidence", "DolaLifecycleInspector.ApplyObject"]:
    if required not in allcs:
        errors.append(f"15s lifecycle invariant missing: {required}")
if errors:
    print("FAIL")
    print("\n".join("- "+e for e in errors))
    sys.exit(1)
print("PASS: V3 dev2 15s lifecycle invariants verified")

verdict=(root/'Services'/'VideoP0Verdict.cs').read_text(encoding='utf-8')
for required in ['LastTaskId','LastTaskStatus','LastTaskDurationSeconds','LastKnownVid','probe.DurationSeconds']:
    if required not in verdict: errors.append(f'P0 verdict gate missing: {required}')
if 'VideoP0Verdict.Evaluate' not in allcs: errors.append('P0 final verdict not wired')
if errors:
    print('FAIL'); print('\n'.join('- '+e for e in errors)); sys.exit(1)
print('PASS: V3 dev2 final P0 certification gate verified')
