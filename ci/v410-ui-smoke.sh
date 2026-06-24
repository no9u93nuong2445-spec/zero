#!/usr/bin/env bash
set -u
mkdir -p v410-ui
OUT='v410-ui'
APK='v410-results/apk/JingHua-V4.1.0-release-candidate.apk'
PKG='com.bianzhifeng.jinghua'
DEVICE_VIDEO='/sdcard/Movies/JingHuaV410Test.mp4'

exec > >(tee "$OUT/console.txt") 2>&1
INSTALL=false
HOME_SIMPLE=false
PICKER_LARGE=false
EDITOR_SIMPLE=false
AUTOPLAY=false
TIMELINE_MOVES=false
NO_CRASH=false

adb_safe() { timeout 25s adb "$@"; }
dump_ui() {
  local tag="$1"
  timeout 15s adb shell uiautomator dump /sdcard/v410-window.xml >/dev/null 2>&1 || return 1
  timeout 15s adb pull /sdcard/v410-window.xml "$OUT/${tag}.xml" >/dev/null 2>&1 || return 1
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
    if m:
        x1,y1,x2,y2=map(int,m.groups())
        print((x1+x2)//2,(y1+y2)//2)
        raise SystemExit(0)
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

adb_safe install -r "$APK" > "$OUT/install.txt" 2>&1 && INSTALL=true
adb_safe shell pm clear "$PKG" > "$OUT/clear.txt" 2>&1 || true
adb logcat -c

adb_safe shell am start -W -n "$PKG/.HomeActivity" > "$OUT/home-start.txt" 2>&1 || true
sleep 6
shot home
dump_ui home || true
if grep -q '选择视频开始去字幕' "$OUT/home.xml" 2>/dev/null \
   && ! grep -Eq '快速模板|项目与历史|云端修复|自动跟踪|关键帧|裁剪到|画质' "$OUT/home.xml" 2>/dev/null; then
  HOME_SIMPLE=true
fi

if tap_text '选择视频开始去字幕' picker-open; then
  sleep 5
  shot picker
  dump_ui picker || true
  adb shell dumpsys window windows > "$OUT/picker-window.txt" 2>&1 || true
  if grep -Eqi 'photopicker|providers\.media|picker' "$OUT/picker-window.txt"; then
    PICKER_LARGE=true
  fi
fi
adb_safe shell input keyevent 4 >/dev/null 2>&1 || true
sleep 2

adb_safe push v410-results/fixtures/white-outline.mp4 "$DEVICE_VIDEO" > "$OUT/push-video.txt" 2>&1 || true
adb_safe shell am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE \
  -d "file://$DEVICE_VIDEO" > "$OUT/media-scan.txt" 2>&1 || true
sleep 4
adb shell content query --uri content://media/external/video/media \
  --projection _id:_display_name > "$OUT/media-query.txt" 2>&1 || true
MEDIA_ID="$(python3 - <<'PY'
import re
text=open('v410-ui/media-query.txt',encoding='utf-8',errors='ignore').read()
for row in text.splitlines():
    if 'JingHuaV410Test.mp4' in row:
        match=re.search(r'_id=(\d+)',row)
        if match:
            print(match.group(1)); break
PY
)"

if [ -n "$MEDIA_ID" ]; then
  adb_safe shell am start -W -a android.intent.action.VIEW \
    -d "content://media/external/video/media/$MEDIA_ID" \
    -t video/mp4 -f 1 -n "$PKG/.MainActivity" > "$OUT/editor-start.txt" 2>&1 || true
fi

for i in $(seq 1 30); do
  sleep 1
  dump_ui "editor-wait-$i" || true
  if grep -q '视频正在播放' "$OUT/editor-wait-$i.xml" 2>/dev/null; then
    AUTOPLAY=true
    cp "$OUT/editor-wait-$i.xml" "$OUT/editor-playing.xml"
    break
  fi
done
shot editor-playing
if [ ! -f "$OUT/editor-playing.xml" ]; then dump_ui editor-playing || true; fi
if grep -q '1. 选择视频' "$OUT/editor-playing.xml" 2>/dev/null \
   && grep -q '2. 预览去字幕效果' "$OUT/editor-playing.xml" 2>/dev/null \
   && grep -q '3. 导出完整去字幕视频' "$OUT/editor-playing.xml" 2>/dev/null \
   && grep -q '查看导出视频' "$OUT/editor-playing.xml" 2>/dev/null \
   && ! grep -Eq '快速模板|自动跟踪|关键帧|裁剪到|云端修复|画质|强度' "$OUT/editor-playing.xml" 2>/dev/null; then
  EDITOR_SIMPLE=true
fi

TIMELINE_A="$(grep -oE '[0-9]{2}:[0-9]{2}\.[0-9]' "$OUT/editor-playing.xml" 2>/dev/null | head -n1)"
sleep 2
dump_ui timeline-later || true
TIMELINE_B="$(grep -oE '[0-9]{2}:[0-9]{2}\.[0-9]' "$OUT/timeline-later.xml" 2>/dev/null | head -n1)"
printf 'before=%s\nafter=%s\n' "$TIMELINE_A" "$TIMELINE_B" > "$OUT/timeline-check.txt"
if [ -n "$TIMELINE_A" ] && [ -n "$TIMELINE_B" ] && [ "$TIMELINE_A" != "$TIMELINE_B" ]; then
  TIMELINE_MOVES=true
fi

adb logcat -d -v threadtime > "$OUT/logcat.txt" 2>&1 || true
if ! grep -E 'FATAL EXCEPTION|AndroidRuntime.*Process: com\.bianzhifeng\.jinghua' "$OUT/logcat.txt"; then
  NO_CRASH=true
fi

export INSTALL HOME_SIMPLE PICKER_LARGE EDITOR_SIMPLE AUTOPLAY TIMELINE_MOVES NO_CRASH MEDIA_ID
python3 - <<'PY'
import json,os
keys=['INSTALL','HOME_SIMPLE','PICKER_LARGE','EDITOR_SIMPLE','AUTOPLAY','TIMELINE_MOVES','NO_CRASH']
result={key.lower():os.environ[key].lower()=='true' for key in keys}
result['media_id']=os.environ.get('MEDIA_ID','')
result['full_pass']=all(result[key.lower()] for key in keys)
open('v410-ui/V410_UI_REPORT.json','w',encoding='utf-8').write(json.dumps(result,ensure_ascii=False,indent=2))
open('v410-ui/V410_UI_REPORT.md','w',encoding='utf-8').write(
    '# 净画 V4.1.0 Android 15 界面测试\n\n'
    + '\n'.join(f"- {key}: {'通过' if value else '失败'}" for key,value in result.items() if isinstance(value,bool))
    + '\n')
print(json.dumps(result,ensure_ascii=False,indent=2))
raise SystemExit(0 if result['full_pass'] else 1)
PY
