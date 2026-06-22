#!/usr/bin/env bash
set -euo pipefail
MODE="${1:-all}"
APK_PATH="jhmin/app/build/outputs/apk/debug/app-debug.apk"

build_apk() {
  cat \
    source_parts/part-03-hex-00 source_parts/part-03-hex-01 \
    source_parts/part-03-hex-02 source_parts/part-03-hex-03 \
    source_parts/part-03-hex-04 source_parts/part-03-hex-05-00 \
    source_parts/part-03-hex-05-01 source_parts/part-03-hex-05-02 \
    source_parts/part-03-hex-05-03 source_parts/part-03-hex-06-00 \
    source_parts/part-03-hex-07-00 > part03.hex
  python3 - <<'PY'
from pathlib import Path
Path('part03.b64').write_bytes(bytes.fromhex(Path('part03.hex').read_text().strip()))
PY
  cat \
    source_parts/part-00 source_parts/part-01 \
    source_parts/part-02-00 source_parts/part-02-01 \
    source_parts/part-02-02 source_parts/part-02-03 \
    source_parts/part-02-04 source_parts/part-02-05 \
    source_parts/part-02-06 source_parts/part-02-07 \
    part03.b64 source_parts/part-04 source_parts/part-05 > source.b64
  base64 --decode source.b64 > jinghua-source.tar.xz
  echo "8e9d2518b55cd2a50c2c51900118dd3944d055228220286bb6c5035687f14b6f  jinghua-source.tar.xz" | sha256sum -c -
  tar -xJf jinghua-source.tar.xz

  cat v403_patch/part-02-hex-00 v403_patch/part-02-hex-01 \
      v403_patch/part-02-hex-02 v403_patch/part-02-hex-03 > v403-p2.hex
  python3 - <<'PY'
from pathlib import Path
Path('v403-p2.b64').write_bytes(bytes.fromhex(Path('v403-p2.hex').read_text().strip()))
PY
  cat v403_patch/part-00 v403_patch/part-01 v403-p2.b64 > v403-patch.b64
  base64 --decode v403-patch.b64 > v403_patch.tar.gz
  echo "d2168ff4405bd5f6c5296119a75b31fce7466ddde511a23c5850f85f61237287  v403_patch.tar.gz" | sha256sum -c -
  tar -xzf v403_patch.tar.gz
  python3 v403_patch/apply_patch.py

  gradle --no-daemon --stacktrace -p jhmin clean assembleDebug > gradle-api29.log 2>&1
  test -s "$APK_PATH"
  unzip -t "$APK_PATH"
  sha256sum "$APK_PATH" | tee apk-api29-sha256.txt
}

tap_text() {
  local wanted="$1"
  local xml_file="$2"
  adb shell uiautomator dump /sdcard/window.xml >/dev/null 2>&1 || return 1
  adb pull /sdcard/window.xml "$xml_file" >/dev/null 2>&1 || return 1
  local coords
  coords="$(python3 - "$wanted" "$xml_file" <<'PY'
import re, sys, xml.etree.ElementTree as ET
wanted, path = sys.argv[1], sys.argv[2]
root = ET.parse(path).getroot()
for node in root.iter('node'):
    text = node.attrib.get('text', '')
    desc = node.attrib.get('content-desc', '')
    if wanted == text or wanted == desc or wanted in text or wanted in desc:
        match = re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]', node.attrib.get('bounds', ''))
        if match:
            x1, y1, x2, y2 = map(int, match.groups())
            print((x1 + x2) // 2, (y1 + y2) // 2)
            raise SystemExit(0)
raise SystemExit(2)
PY
)" || return 1
  adb shell input tap $coords
}

test_launch() {
  test -s "$APK_PATH"
  rm -f smoke-*.txt smoke-*.png smoke-*.xml
  sleep 25
  adb install -r "$APK_PATH" > smoke-install.txt 2>&1
  adb logcat -c
  adb shell am force-stop com.bianzhifeng.jinghua
  adb shell am start -W -n com.bianzhifeng.jinghua/.HomeActivity > smoke-home-start.txt 2>&1
  sleep 10
  tap_text "Wait" smoke-system-dialog.xml || true
  sleep 4
  adb exec-out screencap -p > smoke-home.png
  adb shell dumpsys activity activities > smoke-home-activity.txt
  adb shell uiautomator dump /sdcard/home.xml >/dev/null 2>&1 || true
  adb pull /sdcard/home.xml smoke-home-window.xml >/dev/null 2>&1 || true

  tap_text "进入完整编辑器" smoke-home-tap.xml
  sleep 10
  tap_text "Wait" smoke-editor-dialog.xml || true
  sleep 3
  adb exec-out screencap -p > smoke-editor.png
  adb shell dumpsys activity activities > smoke-editor-activity.txt
  adb shell uiautomator dump /sdcard/editor.xml >/dev/null 2>&1 || true
  adb pull /sdcard/editor.xml smoke-editor-window.xml >/dev/null 2>&1 || true
  adb logcat -d -v threadtime > smoke-logcat.txt

  local pid fatal home_visible editor_visible
  pid="$(adb shell pidof com.bianzhifeng.jinghua | tr -d '\r')"
  printf '%s\n' "$pid" > smoke-pid.txt
  fatal=0
  grep -q "Process: com.bianzhifeng.jinghua" smoke-logcat.txt && fatal=1
  home_visible=0
  editor_visible=0
  grep -q "com.bianzhifeng.jinghua/.HomeActivity" smoke-home-activity.txt && home_visible=1
  grep -q "com.bianzhifeng.jinghua/.MainActivity" smoke-editor-activity.txt && editor_visible=1

  if [ -n "$pid" ] && [ "$fatal" = 0 ] && [ "$home_visible" = 1 ] && [ "$editor_visible" = 1 ]; then
    echo PASS | tee smoke-result.txt
  else
    echo "FAIL pid=$pid fatal=$fatal home_visible=$home_visible editor_visible=$editor_visible" | tee smoke-result.txt
    exit 1
  fi
}

case "$MODE" in
  build) build_apk ;;
  test) test_launch ;;
  all) build_apk; test_launch ;;
  *) echo "Unknown mode: $MODE" >&2; exit 2 ;;
esac
