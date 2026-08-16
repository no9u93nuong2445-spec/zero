#!/usr/bin/env bash
set -u
mkdir -p v409-results/android
OUT='v409-results/android'
APK='jhmin/app/build/outputs/apk/debug/app-debug.apk'
PKG='com.bianzhifeng.jinghua'
DEVICE_VIDEO='/sdcard/Movies/JingHuaTest.mp4'

exec > >(tee "$OUT/ui-console.txt") 2>&1
INSTALL=false
SIMPLE_HOME=false
PHOTO_PICKER=false
EDITOR_SIMPLE=false
AUTOPLAY=false
TIMELINE_MOVES=false
PREVIEW=false
EXPORT_CLICK=false
OUTPUT=false
NO_CRASH=false
OUTPUT_DEVICE=''

adb_safe() { timeout 25s adb "$@"; }
dump_ui() {
  local tag="$1"
  timeout 15s adb shell uiautomator dump /sdcard/v409-window.xml >/dev/null 2>&1 || return 1
  timeout 15s adb pull /sdcard/v409-window.xml "$OUT/${tag}.xml" >/dev/null 2>&1 || return 1
}
shot() { timeout 15s adb exec-out screencap -p > "$OUT/$1.png" 2>/dev/null || true; }
node_xy() {
  python3 - "$1" "$2" <<'PY'
import re,sys,xml.etree.ElementTree as ET
path,q=sys.argv[1:]
try: root=ET.parse(path).getroot()
except Exception: raise SystemExit(1)
for n in root.iter('node'):
    vals=(n.attrib.get('text',''),n.attrib.get('content-desc',''),n.attrib.get('resource-id',''))
    if not any(q in v for v in vals): continue
    m=re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]',n.attrib.get('bounds',''))
    if not m: continue
    x1,y1,x2,y2=map(int,m.groups())
    if x2>x1 and y2>y1:
        print((x1+x2)//2,(y1+y2)//2); raise SystemExit(0)
raise SystemExit(1)
PY
}
tap_text() {
  local text="$1" tag="$2" xy
  dump_ui "$tag" || return 1
  xy="$(node_xy "$OUT/${tag}.xml" "$text" 2>/dev/null)" || return 1
  adb_safe shell input tap $xy >/dev/null 2>&1 || return 1
  sleep 2
}
tap_scroll() {
  local text="$1" tag="$2" max="$3" i
  for i in $(seq 0 "$max"); do
    if tap_text "$text" "${tag}-${i}"; then return 0; fi
    adb_safe shell input swipe 540 1800 540 550 400 >/dev/null 2>&1 || true
    sleep 1
  done
  return 1
}

adb_safe install -r "$APK" > "$OUT/install.txt" 2>&1 && INSTALL=true
adb_safe shell pm clear "$PKG" > "$OUT/clear.txt" 2>&1 || true
adb_safe shell pm grant "$PKG" android.permission.POST_NOTIFICATIONS >/dev/null 2>&1 || true
adb_safe push v409-results/fixtures/subtitle.mp4 "$DEVICE_VIDEO" > "$OUT/push-video.txt" 2>&1
adb_safe shell am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d "file://$DEVICE_VIDEO" > "$OUT/media-scan.txt" 2>&1 || true
sleep 3
adb logcat -c

adb_safe shell am start -W -n "$PKG/.HomeActivity" > "$OUT/home-start.txt" 2>&1 || true
sleep 5
shot home
dump_ui home || true
if grep -q '选择视频开始去字幕' "$OUT/home.xml" 2>/dev/null \
   && ! grep -Eq '快速模板|项目与历史|云端修复|自动跟踪|关键帧|裁剪到' "$OUT/home.xml" 2>/dev/null; then
  SIMPLE_HOME=true
fi

# Open the real Android photo picker and save visual evidence of the larger thumbnail UI.
if tap_text '选择视频开始去字幕' open-picker; then
  sleep 5
  shot photo-picker
  dump_ui photo-picker || true
  adb shell dumpsys window windows > "$OUT/photo-picker-window.txt" 2>&1 || true
  if grep -Eqi 'photopicker|providers\.media|picker' "$OUT/photo-picker-window.txt"; then PHOTO_PICKER=true; fi
fi
adb_safe shell input keyevent 4 >/dev/null 2>&1 || true
sleep 2

# Resolve the scanned MediaStore content URI and open it directly in the same production editor.
adb shell content query --uri content://media/external/video/media \
  --projection _id:_display_name > "$OUT/media-query.txt" 2>&1 || true
MEDIA_ID="$(python3 - <<'PY'
import re
text=open('v409-results/android/media-query.txt',encoding='utf-8',errors='ignore').read()
for row in text.splitlines():
    if 'JingHuaTest.mp4' in row:
        m=re.search(r'_id=(\d+)',row)
        if m: print(m.group(1)); break
PY
)"
if [ -n "$MEDIA_ID" ]; then
  adb_safe shell am start -W -a android.intent.action.VIEW \
    -d "content://media/external/video/media/$MEDIA_ID" -t video/mp4 -f 1 \
    -n "$PKG/.MainActivity" > "$OUT/editor-start.txt" 2>&1 || true
fi

for i in $(seq 1 25); do
  sleep 1
  dump_ui "editor-wait-$i" || true
  if grep -q '视频正在播放' "$OUT/editor-wait-$i.xml" 2>/dev/null; then AUTOPLAY=true; break; fi
done
shot editor-playing
dump_ui editor-playing || true
if grep -q '1. 选择视频' "$OUT/editor-playing.xml" 2>/dev/null \
   && grep -q '3. 导出完整去字幕视频' "$OUT/editor-playing.xml" 2>/dev/null \
   && ! grep -Eq '快速模板|自动跟踪|关键帧|裁剪到|云端修复|画质' "$OUT/editor-playing.xml" 2>/dev/null; then
  EDITOR_SIMPLE=true
fi

# Confirm the visible timeline advances while VideoView is auto-playing.
TIMELINE_A="$(grep -oE '[0-9]{2}:[0-9]{2}\.[0-9]' "$OUT/editor-playing.xml" 2>/dev/null | head -n1)"
sleep 2
dump_ui timeline-later || true
TIMELINE_B="$(grep -oE '[0-9]{2}:[0-9]{2}\.[0-9]' "$OUT/timeline-later.xml" 2>/dev/null | head -n1)"
printf 'before=%s\nafter=%s\n' "$TIMELINE_A" "$TIMELINE_B" > "$OUT/timeline-check.txt"
if [ -n "$TIMELINE_A" ] && [ -n "$TIMELINE_B" ] && [ "$TIMELINE_A" != "$TIMELINE_B" ]; then TIMELINE_MOVES=true; fi

if tap_scroll '2. 预览去字幕效果' preview 12; then
  sleep 12
  PREVIEW=true
fi
shot processed-preview

if tap_scroll '3. 导出完整去字幕视频' export 15; then EXPORT_CLICK=true; fi
shot export-started

for i in $(seq 1 180); do
  OUTPUT_DEVICE="$(timeout 15s adb shell "find '/sdcard/Movies/净画' -type f -name '*.mp4' 2>/dev/null" | tr -d '\r' | tail -n1)"
  if [ -n "$OUTPUT_DEVICE" ]; then break; fi
  sleep 2
done
if [ -n "$OUTPUT_DEVICE" ]; then
  adb_safe pull "$OUTPUT_DEVICE" "$OUT/output.mp4" > "$OUT/pull-output.txt" 2>&1 || true
  [ -s "$OUT/output.mp4" ] && OUTPUT=true
fi
shot export-finished
dump_ui export-finished || true

adb logcat -d -v threadtime > "$OUT/logcat.txt" 2>&1 || true
if ! grep -E 'FATAL EXCEPTION|AndroidRuntime.*Process: com\.bianzhifeng\.jinghua' "$OUT/logcat.txt"; then NO_CRASH=true; fi

export INSTALL SIMPLE_HOME PHOTO_PICKER EDITOR_SIMPLE AUTOPLAY TIMELINE_MOVES PREVIEW EXPORT_CLICK OUTPUT NO_CRASH OUTPUT_DEVICE MEDIA_ID
python3 - <<'PY'
import json,os
keys=['INSTALL','SIMPLE_HOME','PHOTO_PICKER','EDITOR_SIMPLE','AUTOPLAY','TIMELINE_MOVES','PREVIEW','EXPORT_CLICK','OUTPUT','NO_CRASH']
r={k.lower():os.environ[k].lower()=='true' for k in keys}
r['media_id']=os.environ.get('MEDIA_ID','')
r['output_device_path']=os.environ.get('OUTPUT_DEVICE','')
r['ui_flow_pass']=all(r[k.lower()] for k in keys)
open('v409-results/android/UI_RESULT.json','w',encoding='utf-8').write(json.dumps(r,ensure_ascii=False,indent=2))
print(json.dumps(r,ensure_ascii=False,indent=2))
PY

# Always return so evidence and output analysis run; final workflow gate decides pass/fail.
exit 0
