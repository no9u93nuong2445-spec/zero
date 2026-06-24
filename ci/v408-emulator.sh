#!/usr/bin/env bash
set -u
mkdir -p v408-results/android
APK='jhmin/app/build/outputs/apk/debug/app-debug.apk'
PKG='com.bianzhifeng.jinghua'
ACT="$PKG/.FunctionalExportActivity"
DEVICE_DIR="/sdcard/Android/data/$PKG/files/functional-test"
RESULT_DIR='v408-results/android'
TMP_DIR='/data/local/tmp/jinghua-v408'

exec > >(tee "$RESULT_DIR/emulator-console.txt") 2>&1
PASS=1

adb install -r "$APK" > "$RESULT_DIR/install.txt" 2>&1 || PASS=0
adb shell pm clear "$PKG" > "$RESULT_DIR/clear.txt" 2>&1 || true
adb logcat -c

# Verify the real launcher, but do not let software-rendered first-frame latency
# block the functional export tests.
timeout 25s adb shell am start -W -n "$PKG/.HomeActivity" \
  > "$RESULT_DIR/home-start.txt" 2>&1 || true
sleep 3
adb exec-out screencap -p > "$RESULT_DIR/home.png" 2>/dev/null || true
adb shell dumpsys activity activities > "$RESULT_DIR/home-activities.txt" 2>&1 || true
if ! adb shell pidof "$PKG" | grep -q '[0-9]'; then
  echo 'launcher process not alive'
  PASS=0
fi
adb shell am force-stop "$PKG" >/dev/null 2>&1 || true

# Bootstrap the exported test activity once so getExternalFilesDir() exists.
adb shell am start -S -n "$ACT" \
  --es case_name bootstrap --es input_name missing.mp4 \
  --es output_name bootstrap.mp4 --es marker_name bootstrap.txt --es mode none \
  > "$RESULT_DIR/bootstrap-start.txt" 2>&1 || true
sleep 3
adb shell am force-stop "$PKG" >/dev/null 2>&1 || true

# Always stage through /data/local/tmp, then copy as the debuggable application
# UID. This avoids Android/data shell-access differences across emulator images.
adb shell mkdir -p "$TMP_DIR" >/dev/null 2>&1 || true
for f in v408-results/fixtures/*.mp4; do
  name="$(basename "$f")"
  tmp="$TMP_DIR/$name"
  adb push "$f" "$tmp" > "$RESULT_DIR/push-${name}.txt" 2>&1 || PASS=0
  adb shell chmod 644 "$tmp" >/dev/null 2>&1 || true
  adb shell run-as "$PKG" mkdir -p "$DEVICE_DIR" \
    > "$RESULT_DIR/run-as-mkdir-${name}.txt" 2>&1 || PASS=0
  adb shell run-as "$PKG" cp "$tmp" "$DEVICE_DIR/$name" \
    > "$RESULT_DIR/run-as-copy-${name}.txt" 2>&1 || PASS=0
done

run_case() {
  local case_name="$1" input_name="$2" output_name="$3" mode="$4"
  local marker="result-${case_name}.txt"
  local device_marker="$DEVICE_DIR/$marker"
  local device_output="$DEVICE_DIR/$output_name"
  local local_output="$RESULT_DIR/$output_name"
  local found=0

  adb shell run-as "$PKG" rm -f "$device_marker" "$device_output" \
    >/dev/null 2>&1 || true
  adb logcat -c
  adb shell am start -S -n "$ACT" \
    --es case_name "$case_name" \
    --es input_name "$input_name" \
    --es output_name "$output_name" \
    --es marker_name "$marker" \
    --es mode "$mode" \
    > "$RESULT_DIR/start-${case_name}.txt" 2>&1 || true

  for _ in $(seq 1 75); do
    if adb shell run-as "$PKG" test -s "$device_marker" >/dev/null 2>&1; then
      found=1
      break
    fi
    sleep 1
  done

  adb exec-out screencap -p > "$RESULT_DIR/screen-${case_name}.png" 2>/dev/null || true
  adb logcat -d -v threadtime > "$RESULT_DIR/logcat-${case_name}.txt" 2>&1 || true
  if [ "$found" -ne 1 ]; then
    echo "FAIL marker timeout $case_name" | tee "$RESULT_DIR/result-${case_name}.txt"
    return 1
  fi

  adb exec-out run-as "$PKG" cat "$device_marker" \
    | tr -d '\r' > "$RESULT_DIR/result-${case_name}.txt"
  cat "$RESULT_DIR/result-${case_name}.txt"
  grep -q '^PASS' "$RESULT_DIR/result-${case_name}.txt" || return 1
  adb exec-out run-as "$PKG" cat "$device_output" > "$local_output" || return 1
  test -s "$local_output" || return 1
  printf 'run-as cat %s\n' "$device_output" > "$RESULT_DIR/pull-${case_name}.txt"
  return 0
}

run_case clean_reference clean.mp4 output-clean.mp4 none || PASS=0
for style in white-outline yellow-shadow color-outline double-line; do
  run_case "${style}_baseline" "${style}.mp4" \
    "output-${style}-baseline.mp4" none || PASS=0
  run_case "${style}_repair" "${style}.mp4" \
    "output-${style}-repair.mp4" repair_hq || PASS=0
done

adb logcat -d -v threadtime > "$RESULT_DIR/logcat-final.txt" 2>&1 || true
if grep -E 'FATAL EXCEPTION|AndroidRuntime.*Process: com\.bianzhifeng\.jinghua' \
    "$RESULT_DIR/logcat-final.txt"; then
  echo 'fatal exception detected'
  PASS=0
fi

find "$RESULT_DIR" -maxdepth 1 -type f -printf '%f %s bytes\n' \
  | sort > "$RESULT_DIR/files.txt"
echo "$PASS" > "$RESULT_DIR/emulator-pass.txt"
if [ "$PASS" -eq 1 ]; then
  echo V408_ANDROID_EXPORT_PASS
  exit 0
fi
echo V408_ANDROID_EXPORT_FAIL
exit 1
