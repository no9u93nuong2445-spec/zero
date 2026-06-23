#!/usr/bin/env bash
set -euo pipefail
APK="jhmin/app/build/outputs/apk/debug/app-debug.apk"
RESULT_DIR="functional-results"
DEVICE_DIR="/sdcard/Android/data/com.bianzhifeng.jinghua/files/functional-test"

adb install -r "$APK" | tee "$RESULT_DIR/adb-install.txt"
adb shell am start -W -n com.bianzhifeng.jinghua/.HomeActivity > "$RESULT_DIR/home-warmup.txt"
sleep 3
adb shell am force-stop com.bianzhifeng.jinghua
adb shell mkdir -p "$DEVICE_DIR"
adb push "$RESULT_DIR/input.mp4" "$DEVICE_DIR/input.mp4" | tee "$RESULT_DIR/adb-push.txt"
adb logcat -c
adb shell am start -W -n com.bianzhifeng.jinghua/.FunctionalExportActivity | tee "$RESULT_DIR/functional-start.txt"

success=0
for i in $(seq 1 180); do
  adb shell test -f "$DEVICE_DIR/result.txt" && {
    adb pull "$DEVICE_DIR/result.txt" "$RESULT_DIR/device-result.txt" >/dev/null
    if grep -q '^PASS' "$RESULT_DIR/device-result.txt"; then success=1; break; fi
    if grep -q '^FAIL' "$RESULT_DIR/device-result.txt"; then break; fi
  }
  sleep 1
done
adb logcat -d -v threadtime > "$RESULT_DIR/functional-logcat.txt"
adb exec-out screencap -p > "$RESULT_DIR/functional-screen.png" || true
adb pull "$DEVICE_DIR/output.mp4" "$RESULT_DIR/output.mp4" | tee "$RESULT_DIR/adb-pull.txt" || true
cat "$RESULT_DIR/device-result.txt" || true
if [ "$success" != 1 ]; then
  exit 1
fi
test -s "$RESULT_DIR/output.mp4"
