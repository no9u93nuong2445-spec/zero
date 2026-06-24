#!/usr/bin/env bash
set -u
mkdir -p e2e-fast-results
APK='jhmin/app/build/outputs/apk/debug/app-debug.apk'
PKG='com.bianzhifeng.jinghua'
INPUT='functional-results/input-audio.mp4'
DEVICE_INPUT='/sdcard/Download/JingHua-E2E-input.mp4'

exec > >(tee e2e-fast-results/ui-console.txt) 2>&1

INSTALL=false
HOME=false
TEMPLATE=false
PICK=false
EDITOR=false
PREVIEW=false
EXPORT=false
OUTPUT=false
NO_CRASH=false

adb_safe() { timeout 20s adb "$@"; }
dump_ui() {
  local tag="$1"
  timeout 12s adb shell uiautomator dump /sdcard/window.xml >/dev/null 2>&1 || return 1
  timeout 12s adb pull /sdcard/window.xml "e2e-fast-results/${tag}.xml" >/dev/null 2>&1 || return 1
}
shot() { timeout 12s adb exec-out screencap -p > "e2e-fast-results/$1.png" 2>/dev/null || true; }
node_xy() {
  python3 - "$1" "$2" <<'PY'
import re,sys,xml.etree.ElementTree as ET
p,q=sys.argv[1:]
try: root=ET.parse(p).getroot()
except Exception: raise SystemExit(1)
for n in root.iter('node'):
    vals=(n.attrib.get('text',''),n.attrib.get('content-desc',''),n.attrib.get('resource-id',''))
    if not any(q in v for v in vals): continue
    m=re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]',n.attrib.get('bounds',''))
    if m:
        x1,y1,x2,y2=map(int,m.groups()); print((x1+x2)//2,(y1+y2)//2); raise SystemExit(0)
raise SystemExit(1)
PY
}
tap_text() {
  local text="$1" tag="$2" xy
  dump_ui "$tag" || return 1
  xy="$(node_xy "e2e-fast-results/${tag}.xml" "$text" 2>/dev/null)" || return 1
  adb_safe shell input tap $xy >/dev/null 2>&1 || return 1
  sleep 2
}
find_and_tap_scroll() {
  local text="$1" tag="$2" max="$3" i
  for i in $(seq 0 "$max"); do
    if tap_text "$text" "${tag}-${i}"; then return 0; fi
    adb_safe shell input swipe 540 1750 540 500 350 >/dev/null 2>&1 || true
    sleep 1
  done
  return 1
}

adb_safe install -r "$APK" > e2e-fast-results/install.txt 2>&1 && INSTALL=true
adb_safe shell pm clear "$PKG" > e2e-fast-results/clear.txt 2>&1 || true
adb_safe push "$INPUT" "$DEVICE_INPUT" > e2e-fast-results/push.txt 2>&1 || true
adb_safe shell am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d "file://$DEVICE_INPUT" > e2e-fast-results/scan.txt 2>&1 || true
adb logcat -c
adb_safe shell am start -n "$PKG/.HomeActivity" > e2e-fast-results/home-start.txt 2>&1 || true
sleep 8
shot home
dump_ui home || true
if adb_safe shell pidof "$PKG" | grep -q '[0-9]'; then HOME=true; fi

if find_and_tap_scroll '底部字幕' home-template 8; then TEMPLATE=true; fi
sleep 5
shot picker
dump_ui picker || true
if find_and_tap_scroll 'JingHua-E2E-input.mp4' picker-file 8; then PICK=true; fi
sleep 12
shot editor
dump_ui editor || true
if adb_safe shell dumpsys activity activities | grep -q 'com.bianzhifeng.jinghua/.MainActivity'; then EDITOR=true; fi

if find_and_tap_scroll '生成当前帧处理预览' preview-button 14; then PREVIEW=true; sleep 8; fi
shot preview
if find_and_tap_scroll '开始本地批量导出' export-button 20; then EXPORT=true; fi
shot export-start

OUT=''
for _ in $(seq 1 90); do
  OUT="$(timeout 12s adb shell "find /sdcard/Movies -type f -name '*.mp4' 2>/dev/null" | tr -d '\r' | tail -n 1)"
  [ -n "$OUT" ] && break
  sleep 2
done
if [ -n "$OUT" ]; then
  adb_safe pull "$OUT" e2e-fast-results/ui-output.mp4 > e2e-fast-results/pull-output.txt 2>&1 || true
  [ -s e2e-fast-results/ui-output.mp4 ] && OUTPUT=true
fi
shot finished
timeout 20s adb logcat -d -v threadtime > e2e-fast-results/logcat.txt 2>&1 || true
if ! grep -E 'FATAL EXCEPTION|AndroidRuntime.*Process: com\.bianzhifeng\.jinghua' e2e-fast-results/logcat.txt; then NO_CRASH=true; fi

export INSTALL HOME TEMPLATE PICK EDITOR PREVIEW EXPORT OUTPUT NO_CRASH OUT
python3 - <<'PY'
import json,os
keys=['INSTALL','HOME','TEMPLATE','PICK','EDITOR','PREVIEW','EXPORT','OUTPUT','NO_CRASH']
r={k.lower():os.environ[k].lower()=='true' for k in keys}
r['output_device_path']=os.environ.get('OUT','')
r['full_ui_flow_pass']=all(r[k.lower()] for k in keys)
open('e2e-fast-results/ui-result.json','w').write(json.dumps(r,ensure_ascii=False,indent=2))
print(json.dumps(r,ensure_ascii=False,indent=2))
raise SystemExit(0 if r['full_ui_flow_pass'] else 1)
PY
