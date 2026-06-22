#!/usr/bin/env bash
set -euo pipefail

cat \
  source_parts/part-03-hex-00 \
  source_parts/part-03-hex-01 \
  source_parts/part-03-hex-02 \
  source_parts/part-03-hex-03 \
  source_parts/part-03-hex-04 \
  source_parts/part-03-hex-05-00 \
  source_parts/part-03-hex-05-01 \
  source_parts/part-03-hex-05-02 \
  source_parts/part-03-hex-05-03 \
  source_parts/part-03-hex-06-00 \
  source_parts/part-03-hex-07-00 > part03.hex
python3 - <<'PY'
from pathlib import Path
Path('part03.b64').write_bytes(bytes.fromhex(Path('part03.hex').read_text().strip()))
PY
cat \
  source_parts/part-00 \
  source_parts/part-01 \
  source_parts/part-02-00 \
  source_parts/part-02-01 \
  source_parts/part-02-02 \
  source_parts/part-02-03 \
  source_parts/part-02-04 \
  source_parts/part-02-05 \
  source_parts/part-02-06 \
  source_parts/part-02-07 \
  part03.b64 \
  source_parts/part-04 \
  source_parts/part-05 > source.b64
base64 --decode source.b64 > jinghua-source.tar.xz
echo "8e9d2518b55cd2a50c2c51900118dd3944d055228220286bb6c5035687f14b6f  jinghua-source.tar.xz" | sha256sum -c -
tar -xJf jinghua-source.tar.xz

cat v403_patch/part-02-hex-00 v403_patch/part-02-hex-01 v403_patch/part-02-hex-02 v403_patch/part-02-hex-03 > v403-p2.hex
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
APK="jhmin/app/build/outputs/apk/debug/app-debug.apk"
test -s "$APK"
unzip -t "$APK"
sha256sum "$APK" | tee apk-api29-sha256.txt

rm -f smoke-*.txt smoke-*.png
adb install -r "$APK" > smoke-install.txt 2>&1
adb logcat -c
adb shell am force-stop com.bianzhifeng.jinghua
adb shell am start -W -n com.bianzhifeng.jinghua/.HomeActivity > smoke-home-start.txt 2>&1
sleep 6
adb exec-out screencap -p > smoke-home.png
adb shell dumpsys activity activities > smoke-home-activity.txt
adb shell am start -W -n com.bianzhifeng.jinghua/.MainActivity > smoke-editor-start.txt 2>&1
sleep 6
adb exec-out screencap -p > smoke-editor.png
adb shell dumpsys activity activities > smoke-editor-activity.txt
adb logcat -d -v threadtime > smoke-logcat.txt
pid="$(adb shell pidof com.bianzhifeng.jinghua | tr -d '\r')"
printf '%s\n' "$pid" > smoke-pid.txt
fatal=0
grep -E "FATAL EXCEPTION|AndroidRuntime.*Process: com.bianzhifeng.jinghua" smoke-logcat.txt && fatal=1
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
