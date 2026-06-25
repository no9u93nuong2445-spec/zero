#!/usr/bin/env bash
set -u
mkdir -p v410-ui
OUT='v410-ui'
APK='v410-results/apk/JingHua-V4.1.0-release-candidate.apk'
PKG='com.bianzhifeng.jinghua'

exec > >(tee "$OUT/console.txt") 2>&1
SYSTEM_READY=false
INSTALL=false
HOME_SIMPLE=false
PICKER_LARGE=false
EDITOR_SIMPLE=false
AUTOPLAY=false
TIMELINE_MOVES=false
NO_CRASH=false

adb_safe() { timeout 45s adb "$@"; }
shot() {
  timeout 20s adb exec-out screencap -p > "$OUT/$1.png" 2>/dev/null || true
  if [ -s "$OUT/$1.png" ]; then
    file "$OUT/$1.png" > "$OUT/$1-file.txt" 2>&1 || true
  fi
}
wait_log() {
  local pattern="$1" output="$2" limit="${3:-45}"
  for _ in $(seq 1 "$limit"); do
    adb logcat -d -v threadtime > "$output" 2>&1 || true
    if grep -q "$pattern" "$output"; then return 0; fi
    sleep 1
  done
  return 1
}

# Wait only for core services. Android 15 CI images may have a broken external
# media volume and an unstable SystemUI, neither of which belongs to the app.
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

  # Home validation uses an app-owned runtime signal, not UiAutomator. This
  # avoids falsely blaming the app when SystemUI itself shows an ANR dialog.
  adb logcat -c
  adb_safe shell am force-stop "$PKG" >/dev/null 2>&1 || true
  adb_safe shell am start -n "$PKG/.HomeActivity" > "$OUT/home-start.txt" 2>&1 || true
  if wait_log 'JingHuaUi.*HOME_SIMPLE_READY' "$OUT/home-logcat.txt" 70; then
    HOME_SIMPLE=true
  fi
  shot home
  adb shell dumpsys activity activities > "$OUT/home-activities.txt" 2>&1 || true

  # Launch the exact production picker route through MainActivity. Validate both
  # that the app requested ACTION_PICK_IMAGES and that Android resolves it to a
  # system picker component. The CI media library may be empty; that is allowed.
  adb logcat -c
  adb_safe shell am force-stop "$PKG" >/dev/null 2>&1 || true
  adb_safe shell am start -n "$PKG/.MainActivity" \
      --ez pick_on_start true --ei template_index 0 > "$OUT/picker-start.txt" 2>&1 || true
  wait_log 'JingHuaUi.*PICKER_REQUESTED' "$OUT/picker-logcat.txt" 35 || true
  adb shell cmd package resolve-activity --brief \
      -a android.provider.action.PICK_IMAGES -t 'video/*' \
      > "$OUT/picker-resolve.txt" 2>&1 || true
  adb shell dumpsys activity activities > "$OUT/picker-activities.txt" 2>&1 || true
  shot picker
  if grep -q 'JingHuaUi.*PICKER_REQUESTED' "$OUT/picker-logcat.txt" 2>/dev/null \
     && grep -Evq 'No activity found|unable to resolve|Unknown command|Error' \
         "$OUT/picker-resolve.txt" 2>/dev/null \
     && test -s "$OUT/picker-resolve.txt"; then
    PICKER_LARGE=true
  fi

  # The build contains a tiny deterministic MP4 under res/raw only for debug
  # validation. MainActivity loads it through android.resource://, exercising
  # the real editor, VideoView, metadata and autoplay code without /sdcard.
  adb logcat -c
  adb_safe shell am force-stop "$PKG" >/dev/null 2>&1 || true
  adb_safe shell am start -n "$PKG/.MainActivity" \
      --ez jinghua_ui_smoke true > "$OUT/editor-start.txt" 2>&1 || true
  wait_log 'JingHuaUi.*EDITOR_SIMPLE_READY' "$OUT/editor-logcat.txt" 45 || true
  sleep 4
  adb logcat -d -v threadtime > "$OUT/editor-logcat.txt" 2>&1 || true
  adb shell dumpsys activity activities > "$OUT/editor-activities.txt" 2>&1 || true
  shot editor-playing

  if grep -q 'JingHuaUi.*EDITOR_SIMPLE_READY' "$OUT/editor-logcat.txt"; then
    EDITOR_SIMPLE=true
  fi
  if grep -q 'JingHuaPlayback.*AUTOPLAY_READY' "$OUT/editor-logcat.txt"; then
    AUTOPLAY=true
  fi
  if grep -Eq 'JingHuaPlayback.*AUTOPLAY_CONFIRMED playing=true position=[1-9][0-9]*' \
      "$OUT/editor-logcat.txt"; then
    TIMELINE_MOVES=true
  fi
fi

adb logcat -d -v threadtime > "$OUT/logcat.txt" 2>&1 || true
# Ignore SystemUI and UiAutomator failures; fail only when the app process is the
# process named by AndroidRuntime's crash record.
if ! grep -q "Process: $PKG" "$OUT/logcat.txt"; then
  NO_CRASH=true
fi

export SYSTEM_READY INSTALL HOME_SIMPLE PICKER_LARGE EDITOR_SIMPLE AUTOPLAY TIMELINE_MOVES NO_CRASH
python3 - <<'PY'
import json,os
keys=['SYSTEM_READY','INSTALL','HOME_SIMPLE','PICKER_LARGE','EDITOR_SIMPLE','AUTOPLAY','TIMELINE_MOVES','NO_CRASH']
result={key.lower():os.environ[key].lower()=='true' for key in keys}
result['full_pass']=all(result[key.lower()] for key in keys)
open('v410-ui/V410_UI_REPORT.json','w',encoding='utf-8').write(json.dumps(result,ensure_ascii=False,indent=2))
open('v410-ui/V410_UI_REPORT.md','w',encoding='utf-8').write(
    '# 净画 V4.1.0 Android 15 界面测试\n\n'
    + '\n'.join(f"- {key}: {'通过' if value else '失败'}" for key,value in result.items())
    + '\n')
print(json.dumps(result,ensure_ascii=False,indent=2))
raise SystemExit(0 if result['full_pass'] else 1)
PY
