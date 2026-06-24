#!/usr/bin/env bash
set -u
mkdir -p v410-functional
OUT='v410-functional'
APK='v410-results/apk/JingHua-V4.1.0-release-candidate.apk'
PKG='com.bianzhifeng.jinghua'
ACT="$PKG/.FunctionalExportActivity"
DEVICE_DIR="/sdcard/Android/data/$PKG/files/functional-test"
TMP_DIR='/data/local/tmp/jinghua-v410'
PASS=1

exec > >(tee "$OUT/console.txt") 2>&1

adb install -r "$APK" > "$OUT/install.txt" 2>&1 || PASS=0
adb shell pm clear "$PKG" > "$OUT/clear.txt" 2>&1 || true
adb logcat -c

# Bootstrap the CI-only activity so the app-owned external directory exists.
adb shell am start -S -n "$ACT" \
  --es case_name bootstrap --es input_name missing.mp4 \
  --es output_name bootstrap.mp4 --es marker_name bootstrap.txt --es mode none \
  > "$OUT/bootstrap.txt" 2>&1 || true
sleep 3
adb shell am force-stop "$PKG" >/dev/null 2>&1 || true

adb shell mkdir -p "$TMP_DIR" >/dev/null 2>&1 || true
for src in v410-results/fixtures/*.mp4; do
  name="$(basename "$src")"
  tmp="$TMP_DIR/$name"
  adb push "$src" "$tmp" > "$OUT/push-$name.txt" 2>&1 || PASS=0
  adb shell chmod 644 "$tmp" >/dev/null 2>&1 || true
  # API 29 normally permits direct shell access to app-specific external files.
  # Keep run-as as a fallback for emulator images with stricter mounts.
  adb shell mkdir -p "$DEVICE_DIR" >/dev/null 2>&1 || true
  if ! adb shell cp "$tmp" "$DEVICE_DIR/$name" > "$OUT/copy-$name.txt" 2>&1; then
    adb shell run-as "$PKG" mkdir -p "$DEVICE_DIR" >/dev/null 2>&1 || PASS=0
    adb shell run-as "$PKG" cp "$tmp" "$DEVICE_DIR/$name" \
      >> "$OUT/copy-$name.txt" 2>&1 || PASS=0
  fi
done

marker_exists() {
  local path="$1"
  adb shell test -s "$path" >/dev/null 2>&1 \
    || adb shell run-as "$PKG" test -s "$path" >/dev/null 2>&1
}

copy_device_file() {
  local remote="$1" local_file="$2"
  if adb pull "$remote" "$local_file" >/dev/null 2>&1; then
    return 0
  fi
  adb exec-out run-as "$PKG" cat "$remote" > "$local_file" 2>/dev/null
}

run_case() {
  local case_name="$1" input_name="$2" output_name="$3" mode="$4"
  local marker="result-${case_name}.txt"
  local device_marker="$DEVICE_DIR/$marker"
  local device_output="$DEVICE_DIR/$output_name"
  local found=0

  adb shell rm -f "$device_marker" "$device_output" >/dev/null 2>&1 || true
  adb shell run-as "$PKG" rm -f "$device_marker" "$device_output" >/dev/null 2>&1 || true
  adb logcat -c
  adb shell am start -S -n "$ACT" \
    --es case_name "$case_name" \
    --es input_name "$input_name" \
    --es output_name "$output_name" \
    --es marker_name "$marker" \
    --es mode "$mode" \
    --ez auto_detect false \
    > "$OUT/start-${case_name}.txt" 2>&1 || true

  for _ in $(seq 1 120); do
    if marker_exists "$device_marker"; then
      found=1
      break
    fi
    sleep 1
  done

  adb logcat -d -v threadtime > "$OUT/logcat-${case_name}.txt" 2>&1 || true
  adb exec-out screencap -p > "$OUT/screen-${case_name}.png" 2>/dev/null || true
  adb shell ls -l "$DEVICE_DIR" > "$OUT/device-files-${case_name}.txt" 2>&1 || true
  if [ "$found" -ne 1 ]; then
    echo "FAIL marker timeout" | tee "$OUT/result-${case_name}.txt"
    return 1
  fi

  copy_device_file "$device_marker" "$OUT/result-${case_name}.txt" || return 1
  tr -d '\r' < "$OUT/result-${case_name}.txt" > "$OUT/result-${case_name}.tmp"
  mv "$OUT/result-${case_name}.tmp" "$OUT/result-${case_name}.txt"
  cat "$OUT/result-${case_name}.txt"
  grep -q '^PASS' "$OUT/result-${case_name}.txt" || return 1
  copy_device_file "$device_output" "$OUT/$output_name" || return 1
  test -s "$OUT/$output_name" || return 1
  return 0
}

if [ "$PASS" -eq 1 ]; then
  run_case clean_reference clean.mp4 output-clean.mp4 none || PASS=0
fi
for style in white-outline color-outline double-line; do
  if [ "$PASS" -eq 1 ]; then
    run_case "${style}_baseline" "${style}.mp4" "output-${style}-baseline.mp4" none || PASS=0
  fi
  if [ "$PASS" -eq 1 ]; then
    run_case "${style}_repair" "${style}.mp4" "output-${style}-repair.mp4" repair_hq || PASS=0
  fi
done

adb logcat -d -v threadtime > "$OUT/logcat-final.txt" 2>&1 || true
if grep -E 'FATAL EXCEPTION|AndroidRuntime.*Process: com\.bianzhifeng\.jinghua' "$OUT/logcat-final.txt"; then
  PASS=0
fi
find "$OUT" -maxdepth 1 -type f -printf '%f %s bytes\n' | sort > "$OUT/files.txt"
echo "$PASS" > "$OUT/emulator-pass.txt"
if [ "$PASS" -eq 1 ]; then
  echo V410_FUNCTIONAL_EXPORTS_PASS
  exit 0
fi
echo V410_FUNCTIONAL_EXPORTS_FAIL
exit 1
