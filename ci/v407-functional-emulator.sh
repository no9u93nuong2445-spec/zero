#!/usr/bin/env bash
set -u
mkdir -p functional-results e2e-results
APK='jhmin/app/build/outputs/apk/debug/app-debug.apk'
PKG='com.bianzhifeng.jinghua'
ACT="$PKG/.FunctionalExportActivity"
DEVICE_DIR="/sdcard/Android/data/$PKG/files/functional-test"

exec > >(tee functional-results/v407-functional-console.txt) 2>&1

PASS_ALL=1
adb install -r "$APK" > functional-results/adb-install.txt 2>&1 || PASS_ALL=0
adb shell pm clear "$PKG" > functional-results/pm-clear.txt 2>&1 || true
adb logcat -c

# Do not treat Android 15's `am start -W` timeout as an app failure. The process may
# be alive while its first UI frame is still being produced under software rendering.
timeout 30s adb shell am start -W -n "$PKG/.HomeActivity" \
  > functional-results/home-warmup.txt 2>&1 || true
sleep 3
adb shell mkdir -p "$DEVICE_DIR" > functional-results/mkdir.txt 2>&1 || true
adb push functional-results/clean-audio.mp4 "$DEVICE_DIR/clean-audio.mp4" \
  > functional-results/adb-push-clean.txt 2>&1 || PASS_ALL=0
adb push functional-results/input-audio.mp4 "$DEVICE_DIR/input-audio.mp4" \
  > functional-results/adb-push-input.txt 2>&1 || PASS_ALL=0

run_case() {
  local case_name="$1" input_name="$2" output_name="$3" mode="$4"
  local marker="result-${case_name}.txt"
  local device_marker="$DEVICE_DIR/$marker"
  local device_output="$DEVICE_DIR/$output_name"
  local start_file="functional-results/start-${case_name}.txt"
  local marker_file="functional-results/result-${case_name}.txt"
  local output_file="functional-results/$output_name"

  adb shell rm -f "$device_marker" "$device_output" >/dev/null 2>&1 || true
  adb logcat -c
  timeout 35s adb shell am start -W -S -n "$ACT" \
    --es case_name "$case_name" \
    --es input_name "$input_name" \
    --es output_name "$output_name" \
    --es marker_name "$marker" \
    --es mode "$mode" \
    > "$start_file" 2>&1 || true

  local found=0
  for _ in $(seq 1 150); do
    if adb shell test -s "$device_marker" >/dev/null 2>&1; then
      found=1
      break
    fi
    sleep 2
  done

  adb exec-out screencap -p > "functional-results/screen-${case_name}.png" 2>/dev/null || true
  adb logcat -d -v threadtime > "functional-results/logcat-${case_name}.txt" 2>&1 || true
  if [ "$found" -ne 1 ]; then
    echo "FAIL marker timeout for $case_name" | tee "$marker_file"
    return 1
  fi

  adb shell cat "$device_marker" | tr -d '\r' > "$marker_file"
  cat "$marker_file"
  if ! grep -q '^PASS' "$marker_file"; then
    return 1
  fi
  adb pull "$device_output" "$output_file" \
    > "functional-results/pull-${case_name}.txt" 2>&1 || return 1
  test -s "$output_file" || return 1
  return 0
}

run_case clean_reference clean-audio.mp4 output-clean_reference.mp4 none || PASS_ALL=0
run_case baseline_audio input-audio.mp4 output-baseline_audio.mp4 none || PASS_ALL=0
run_case repair_audio input-audio.mp4 output-repair_audio.mp4 repair_hq || PASS_ALL=0

{
  echo "functional_pass=$PASS_ALL"
  for f in functional-results/result-*.txt; do
    echo "--- $f"
    cat "$f"
  done
} > functional-results/case-summary.txt

echo "$PASS_ALL" > e2e-results/functional-emulator-pass.txt
if [ "$PASS_ALL" -eq 1 ]; then
  echo V407_FUNCTIONAL_EMULATOR_PASS
  exit 0
fi
echo V407_FUNCTIONAL_EMULATOR_FAIL
exit 1
