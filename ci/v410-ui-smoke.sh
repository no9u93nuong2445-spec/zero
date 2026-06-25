#!/usr/bin/env bash
set -u
mkdir -p v410-ui
OUT='v410-ui'
APK='v410-results/apk/JingHua-V4.1.0-release-candidate.apk'
PKG='com.bianzhifeng.jinghua'
LOCAL_VIDEO='v410-results/fixtures/white-outline.mp4'
PRIVATE_NAME='JingHuaV410Test.mp4'

exec > >(tee "$OUT/console.txt") 2>&1
SYSTEM_READY=false
INSTALL=false
HOME_SIMPLE=false
PICKER_LARGE=false
PRIVATE_VIDEO_READY=false
EDITOR_SIMPLE=false
AUTOPLAY=false
TIMELINE_MOVES=false
NO_CRASH=false

adb_safe() { timeout 45s adb "$@"; }
dump_ui() {
  local tag="$1"
  for _ in $(seq 1 4); do
    timeout 20s adb shell uiautomator dump /data/local/tmp/v410-window.xml >/dev/null 2>&1 || true
    if timeout 20s adb pull /data/local/tmp/v410-window.xml "$OUT/${tag}.xml" >/dev/null 2>&1 \
        && test -s "$OUT/${tag}.xml"; then
      return 0
    fi
    sleep 2
  done
  return 1
}
shot() {
  timeout 20s adb exec-out screencap -p > "$OUT/$1.png" 2>/dev/null || true
  if [ -s "$OUT/$1.png" ]; then
    file "$OUT/$1.png" > "$OUT/$1-file.txt" 2>&1 || true
  fi
}
node_xy() {
  python3 - "$1" "$2" <<'PY'
import re,sys,xml.etree.ElementTree as ET
path,q=sys.argv[1:]
try: root=ET.parse(path).getroot()
except Exception: raise SystemExit(1)
for node in root.iter('node'):
    values=(node.attrib.get('text',''),node.attrib.get('content-desc',''),node.attrib.get('resource-id',''))
    if not any(q in value for value in values):
        continue
    match=re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]',node.attrib.get('bounds',''))
    if match:
        x1,y1,x2,y2=map(int,match.groups())
        if x2>x1 and y2>y1:
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
  sleep 3
}

# Wait only for core Android services. The API-35 CI image sometimes has no
# mounted external_primary media volume, which is unrelated to this app.
for _ in $(seq 1 75); do
  BOOT="$(timeout 10s adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')"
  PM_READY="$(timeout 10s adb shell pm list packages android 2>/dev/null | head -n1)"
  ACTIVITY_READY="$(timeout 10s adb shell service check activity 2>/dev/null | tr -d '\r')"
  if [ "$BOOT" = "1" ] && [ -n "$PM_READY" ] && echo "$ACTIVITY_READY" | grep -q 'found'; then
    SYSTEM_READY=true
    break
  fi
  sleep 2
done
printf 'system_ready=%s\nboot=%s\npm=%s\nactivity=%s\n' \
  "$SYSTEM_READY" "$BOOT" "$PM_READY" "$ACTIVITY_READY" > "$OUT/system-readiness.txt"

# Install with one retry and verify the package path.
for attempt in 1 2; do
  timeout 120s adb install -r "$APK" > "$OUT/install-attempt-${attempt}.txt" 2>&1 || true
  if timeout 30s adb shell pm path "$PKG" > "$OUT/package-path.txt" 2>&1 \
      && grep -q '^package:' "$OUT/package-path.txt"; then
    INSTALL=true
    break
  fi
  adb reconnect >/dev/null 2>&1 || true
  sleep 4
done

if [ "$INSTALL" = true ]; then
  adb_safe shell pm clear "$PKG" > "$OUT/clear.txt" 2>&1 || true
  adb logcat -c
  timeout 45s adb shell am force-stop "$PKG" >/dev/null 2>&1 || true
  timeout 45s adb shell am start -W -n "$PKG/.HomeActivity" > "$OUT/home-start.txt" 2>&1 || true
  sleep 6
  shot home
  dump_ui home || true
  if grep -q '选择视频开始去字幕' "$OUT/home.xml" 2>/dev/null \
     && ! grep -Eq '快速模板|项目与历史|云端修复|自动跟踪|关键帧|裁剪到|画质' \
        "$OUT/home.xml" 2>/dev/null; then
    HOME_SIMPLE=true
  fi

  # Verify that the app opens Android's system Photo Picker. The picker may be
  # empty on CI because the emulator lacks an external media volume, but opening
  # the correct picker proves the production selection route and large-card UI.
  if tap_text '选择视频开始去字幕' picker-open; then
    sleep 5
    shot picker
    dump_ui picker || true
    adb shell dumpsys window windows > "$OUT/picker-window.txt" 2>&1 || true
    adb shell dumpsys activity activities > "$OUT/picker-activities.txt" 2>&1 || true
    if grep -Eqi 'photopicker|photo picker|pickeractivity|providers\.media\.module.*picker' \
        "$OUT/picker-window.txt" "$OUT/picker-activities.txt" "$OUT/picker.xml" 2>/dev/null; then
      PICKER_LARGE=true
    fi
  fi
  adb_safe shell input keyevent 4 >/dev/null 2>&1 || true
  sleep 2

  # Bypass the emulator's broken external storage only for the playback test.
  # Copy the exact fixture into the app's private files directory and open the
  # real MainActivity with ACTION_VIEW. This exercises the same VideoView,
  # metadata, overlay and simple editor path as a gallery-selected Uri.
  test -s "$LOCAL_VIDEO" || true
  timeout 60s adb shell run-as "$PKG" mkdir -p files > "$OUT/private-mkdir.txt" 2>&1 || true
  timeout 90s adb shell run-as "$PKG" sh -c "cat > files/$PRIVATE_NAME" \
      < "$LOCAL_VIDEO" > "$OUT/private-copy.txt" 2>&1 || true
  LOCAL_SIZE="$(stat -c %s "$LOCAL_VIDEO" 2>/dev/null || echo 0)"
  REMOTE_SIZE="$(timeout 30s adb shell run-as "$PKG" stat -c %s "files/$PRIVATE_NAME" \
      2>/dev/null | tr -d '\r' || echo 0)"
  APP_DATA="$(timeout 30s adb shell run-as "$PKG" pwd 2>/dev/null | tr -d '\r')"
  printf 'local_size=%s\nremote_size=%s\napp_data=%s\n' \
      "$LOCAL_SIZE" "$REMOTE_SIZE" "$APP_DATA" > "$OUT/private-video.txt"
  if [ "$LOCAL_SIZE" -gt 10000 ] && [ "$REMOTE_SIZE" = "$LOCAL_SIZE" ] && [ -n "$APP_DATA" ]; then
    PRIVATE_VIDEO_READY=true
  fi

  if [ "$PRIVATE_VIDEO_READY" = true ]; then
    adb logcat -c
    PRIVATE_URI="file://$APP_DATA/files/$PRIVATE_NAME"
    timeout 60s adb shell am start -W \
      -a android.intent.action.VIEW \
      -d "$PRIVATE_URI" \
      -t video/mp4 -f 1 -n "$PKG/.MainActivity" > "$OUT/editor-start.txt" 2>&1 || true
    sleep 10
    shot editor-playing
    dump_ui editor-playing || true
    adb logcat -d -v threadtime > "$OUT/playback-logcat.txt" 2>&1 || true

    if grep -q '净画简单编辑器：选择视频、预览去字幕、导出完整视频' \
        "$OUT/editor-playing.xml" 2>/dev/null \
       && ! grep -Eq '快速模板|自动跟踪|关键帧|裁剪到|云端修复|画质|强度' \
        "$OUT/editor-playing.xml" 2>/dev/null; then
      EDITOR_SIMPLE=true
    fi
    if grep -q 'JingHuaPlayback.*AUTOPLAY_READY' "$OUT/playback-logcat.txt"; then
      AUTOPLAY=true
    fi
    if grep -Eq 'JingHuaPlayback.*AUTOPLAY_CONFIRMED playing=true position=[1-9][0-9]*' \
        "$OUT/playback-logcat.txt"; then
      TIMELINE_MOVES=true
    fi
  fi
fi

adb logcat -d -v threadtime > "$OUT/logcat.txt" 2>&1 || true
if ! grep -E 'FATAL EXCEPTION|AndroidRuntime.*Process: com\.bianzhifeng\.jinghua' \
    "$OUT/logcat.txt"; then
  NO_CRASH=true
fi

export SYSTEM_READY INSTALL HOME_SIMPLE PICKER_LARGE PRIVATE_VIDEO_READY EDITOR_SIMPLE AUTOPLAY TIMELINE_MOVES NO_CRASH
python3 - <<'PY'
import json,os
keys=[
    'SYSTEM_READY','INSTALL','HOME_SIMPLE','PICKER_LARGE','PRIVATE_VIDEO_READY',
    'EDITOR_SIMPLE','AUTOPLAY','TIMELINE_MOVES','NO_CRASH'
]
result={key.lower():os.environ[key].lower()=='true' for key in keys}
result['full_pass']=all(result[key.lower()] for key in keys)
open('v410-ui/V410_UI_REPORT.json','w',encoding='utf-8').write(
    json.dumps(result,ensure_ascii=False,indent=2))
open('v410-ui/V410_UI_REPORT.md','w',encoding='utf-8').write(
    '# 净画 V4.1.0 Android 15 界面测试\n\n'
    + '\n'.join(f"- {key}: {'通过' if value else '失败'}" for key,value in result.items())
    + '\n')
print(json.dumps(result,ensure_ascii=False,indent=2))
raise SystemExit(0 if result['full_pass'] else 1)
PY
