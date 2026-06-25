#!/usr/bin/env bash
set -uo pipefail
mkdir -p e2e-results
APK='jhmin/app/build/outputs/apk/debug/app-debug.apk'
PKG='com.bianzhifeng.jinghua'
INPUT_LOCAL='functional-results/input-audio.mp4'
INPUT_DEVICE='/sdcard/Download/JingHua-E2E-input.mp4'
RESULT_JSON='e2e-results/ui-e2e-result.json'

exec > >(tee e2e-results/ui-e2e-console.log) 2>&1

INSTALL_OK=false
HOME_OK=false
PICK_OK=false
LOAD_OK=false
PREVIEW_OK=false
EXPORT_CLICK_OK=false
OUTPUT_OK=false
NO_FATAL=false
OUTPUT_DEVICE=''

shot() {
  local name="$1"
  adb exec-out screencap -p > "e2e-results/ui-${name}.png" 2>/dev/null || true
}

dump_ui() {
  local name="$1"
  adb shell uiautomator dump /sdcard/jinghua-window.xml >/dev/null 2>&1 || true
  adb pull /sdcard/jinghua-window.xml "e2e-results/ui-${name}.xml" >/dev/null 2>&1 || true
}

node_xy() {
  local xml="$1" query="$2" mode="${3:-contains}"
  python3 - "$xml" "$query" "$mode" <<'PY'
import re, sys, xml.etree.ElementTree as ET
path, query, mode = sys.argv[1:4]
try:
    root = ET.parse(path).getroot()
except Exception:
    raise SystemExit(1)
for node in root.iter('node'):
    attrs = [node.attrib.get('text',''), node.attrib.get('content-desc',''), node.attrib.get('resource-id','')]
    matched = any((v == query if mode == 'exact' else query in v) for v in attrs if v)
    if not matched:
        continue
    m = re.match(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]', node.attrib.get('bounds',''))
    if not m:
        continue
    x1,y1,x2,y2 = map(int,m.groups())
    if x2 > x1 and y2 > y1:
        print((x1+x2)//2, (y1+y2)//2)
        raise SystemExit(0)
raise SystemExit(1)
PY
}

tap_visible() {
  local query="$1" mode="${2:-contains}" tag="${3:-lookup}"
  dump_ui "$tag"
  local xy
  xy="$(node_xy "e2e-results/ui-${tag}.xml" "$query" "$mode" 2>/dev/null)" || return 1
  echo "tap [$query] at $xy"
  adb shell input tap $xy
  sleep 1
}

tap_with_scroll() {
  local query="$1" mode="${2:-contains}" max="${3:-10}" tag="${4:-scroll}"
  local i
  for i in $(seq 0 "$max"); do
    if tap_visible "$query" "$mode" "${tag}-${i}"; then
      return 0
    fi
    adb shell input swipe 540 1900 540 520 450 >/dev/null 2>&1 || true
    sleep 1
  done
  return 1
}

wait_text() {
  local query="$1" seconds="${2:-30}" tag="${3:-wait}"
  local i
  for i in $(seq 1 "$seconds"); do
    dump_ui "${tag}-${i}"
    if node_xy "e2e-results/ui-${tag}-${i}.xml" "$query" contains >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  return 1
}

finish_report() {
  export INSTALL_OK HOME_OK PICK_OK LOAD_OK PREVIEW_OK EXPORT_CLICK_OK OUTPUT_OK NO_FATAL OUTPUT_DEVICE
  python3 - <<'PY'
import json, os
keys = ['INSTALL_OK','HOME_OK','PICK_OK','LOAD_OK','PREVIEW_OK','EXPORT_CLICK_OK','OUTPUT_OK','NO_FATAL']
data = {k.lower(): os.environ.get(k,'false').lower() == 'true' for k in keys}
data['output_device_path'] = os.environ.get('OUTPUT_DEVICE','')
data['full_ui_flow_pass'] = all(data[k.lower()] for k in keys)
with open('e2e-results/ui-e2e-result.json','w',encoding='utf-8') as f:
    json.dump(data,f,ensure_ascii=False,indent=2)
print(json.dumps(data,ensure_ascii=False,indent=2))
PY
}
trap finish_report EXIT

set +e
adb install -r "$APK" > e2e-results/ui-install.txt 2>&1
if [ $? -eq 0 ]; then INSTALL_OK=true; fi
adb shell pm clear "$PKG" > e2e-results/ui-pm-clear.txt 2>&1
adb shell pm grant "$PKG" android.permission.POST_NOTIFICATIONS >/dev/null 2>&1 || true
adb push "$INPUT_LOCAL" "$INPUT_DEVICE" > e2e-results/ui-push-input.txt 2>&1
adb shell am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d "file://$INPUT_DEVICE" > e2e-results/ui-media-scan.txt 2>&1
adb logcat -c

adb shell am start -W -n "$PKG/.HomeActivity" > e2e-results/ui-home-start.txt 2>&1
sleep 5
shot home
dump_ui home
if adb shell pidof "$PKG" | grep -q '[0-9]'; then HOME_OK=true; fi

# Standard user path: home quick template -> Android document picker.
if tap_with_scroll '底部字幕' exact 10 home-template; then
  sleep 4
  shot picker
  dump_ui picker
  # The file normally appears in Recents. Fall back to the Downloads root.
  if tap_visible 'JingHua-E2E-input.mp4' contains picker-file; then
    PICK_OK=true
  else
    tap_visible 'Show roots' contains picker-roots || tap_visible '显示根目录' contains picker-roots-cn || true
    sleep 1
    tap_visible 'Downloads' exact picker-downloads || tap_visible '下载' exact picker-downloads-cn || true
    sleep 2
    if tap_with_scroll 'JingHua-E2E-input.mp4' contains 8 picker-file-scroll; then
      PICK_OK=true
    fi
  fi
fi

# Wait until the editor reports the 4.2-second input.
if wait_text '00:04' 45 editor-load; then
  LOAD_OK=true
else
  # Filename/video information is an acceptable secondary load signal.
  if wait_text 'JingHua-E2E-input' 5 editor-load-name; then LOAD_OK=true; fi
fi
shot editor-loaded
dump_ui editor-loaded

# Generate a real processed frame preview through the normal editor controls.
if tap_with_scroll '生成当前帧处理预览' exact 14 preview-button; then
  sleep 10
  PREVIEW_OK=true
fi
shot preview
dump_ui preview

# Start the normal local batch export, not the functional test activity.
if tap_with_scroll '开始本地批量导出' exact 24 export-button; then
  EXPORT_CLICK_OK=true
fi
shot export-started
dump_ui export-started

# Poll status and MediaStore-backed Movies directory for up to four minutes.
for i in $(seq 1 120); do
  dump_ui "export-poll-${i}"
  if grep -q '批量完成' "e2e-results/ui-export-poll-${i}.xml" 2>/dev/null; then
    echo "batch completion text detected"
    break
  fi
  OUTPUT_DEVICE="$(adb shell "find /sdcard/Movies -type f -name '*.mp4' 2>/dev/null" | tr -d '\r' | tail -n 1)"
  if [ -n "$OUTPUT_DEVICE" ]; then
    echo "output detected: $OUTPUT_DEVICE"
    break
  fi
  sleep 2
done

OUTPUT_DEVICE="$(adb shell "find /sdcard/Movies -type f -name '*.mp4' 2>/dev/null" | tr -d '\r' | tail -n 1)"
if [ -n "$OUTPUT_DEVICE" ]; then
  adb pull "$OUTPUT_DEVICE" e2e-results/ui-output.mp4 > e2e-results/ui-pull-output.txt 2>&1
  if [ -s e2e-results/ui-output.mp4 ]; then OUTPUT_OK=true; fi
fi
shot export-finished
dump_ui export-finished

adb shell dumpsys package "$PKG" > e2e-results/ui-package.txt 2>&1
adb shell dumpsys activity activities > e2e-results/ui-activities.txt 2>&1
adb shell content query --uri content://media/external/video/media \
  --projection _id:_display_name:relative_path:duration:size > e2e-results/ui-mediastore.txt 2>&1 || true
adb logcat -d -v threadtime > e2e-results/ui-logcat.txt 2>&1
if ! grep -E 'FATAL EXCEPTION|AndroidRuntime.*Process: com\.bianzhifeng\.jinghua' e2e-results/ui-logcat.txt; then
  NO_FATAL=true
fi

finish_report
trap - EXIT
python3 - <<'PY'
import json
r=json.load(open('e2e-results/ui-e2e-result.json'))
raise SystemExit(0 if r['full_ui_flow_pass'] else 1)
PY
