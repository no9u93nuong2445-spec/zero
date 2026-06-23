#!/usr/bin/env bash
set -euo pipefail
APK="jhmin/app/build/outputs/apk/debug/app-debug.apk"
RESULT_DIR="functional-results"
DEVICE_DIR="/sdcard/Android/data/com.bianzhifeng.jinghua/files/functional-test"

mkdir -p "$RESULT_DIR"
adb install -r "$APK" | tee "$RESULT_DIR/adb-install.txt"
adb shell am start -W -n com.bianzhifeng.jinghua/.HomeActivity > "$RESULT_DIR/home-warmup.txt"
sleep 3
adb shell am force-stop com.bianzhifeng.jinghua
adb shell mkdir -p "$DEVICE_DIR"
adb push "$RESULT_DIR/input-noaudio.mp4" "$DEVICE_DIR/input-noaudio.mp4" | tee "$RESULT_DIR/adb-push-noaudio.txt"
adb push "$RESULT_DIR/input-audio.mp4" "$DEVICE_DIR/input-audio.mp4" | tee "$RESULT_DIR/adb-push-audio.txt"

run_case() {
  local case_name="$1"
  local input_name="$2"
  local mode="$3"
  local remove_audio="$4"
  local output_name="output-${case_name}.mp4"
  local marker_name="result-${case_name}.txt"

  echo "===== CASE ${case_name} input=${input_name} mode=${mode} remove_audio=${remove_audio} =====" | tee -a "$RESULT_DIR/case-summary.txt"
  adb shell am force-stop com.bianzhifeng.jinghua || true
  adb shell rm -f "$DEVICE_DIR/$output_name" "$DEVICE_DIR/$marker_name" || true
  adb logcat -c
  adb shell am start -W \
    -n com.bianzhifeng.jinghua/.FunctionalExportActivity \
    --es case_name "$case_name" \
    --es input_name "$input_name" \
    --es output_name "$output_name" \
    --es marker_name "$marker_name" \
    --es mode "$mode" \
    --ez remove_audio "$remove_audio" \
    | tee "$RESULT_DIR/start-${case_name}.txt"

  local found=0
  for i in $(seq 1 180); do
    if adb shell test -f "$DEVICE_DIR/$marker_name"; then
      adb pull "$DEVICE_DIR/$marker_name" "$RESULT_DIR/$marker_name" >/dev/null || true
      found=1
      break
    fi
    sleep 1
  done
  adb logcat -d -v threadtime > "$RESULT_DIR/logcat-${case_name}.txt"
  adb exec-out screencap -p > "$RESULT_DIR/screen-${case_name}.png" || true
  adb pull "$DEVICE_DIR/$output_name" "$RESULT_DIR/$output_name" > "$RESULT_DIR/pull-${case_name}.txt" 2>&1 || true

  if [ "$found" = 0 ]; then
    printf 'FAIL\ncase=%s\nreason=timeout_waiting_for_marker\n' "$case_name" > "$RESULT_DIR/$marker_name"
  fi
  cat "$RESULT_DIR/$marker_name" | tee -a "$RESULT_DIR/case-summary.txt"
  printf '\n' | tee -a "$RESULT_DIR/case-summary.txt"
}

: > "$RESULT_DIR/case-summary.txt"
run_case baseline_noaudio input-noaudio.mp4 none true
run_case blur_noaudio input-noaudio.mp4 blur true
run_case repair_noaudio input-noaudio.mp4 repair_hq true
run_case baseline_audio input-audio.mp4 none false
run_case repair_audio input-audio.mp4 repair_hq false

printf '\n===== OUTPUT FILES =====\n' | tee -a "$RESULT_DIR/case-summary.txt"
find "$RESULT_DIR" -maxdepth 1 -name 'output-*.mp4' -printf '%f %s bytes\n' | sort | tee -a "$RESULT_DIR/case-summary.txt"
exit 0
