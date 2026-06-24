#!/usr/bin/env bash
set -euo pipefail
exec > >(tee v407-prepare.log) 2>&1

# Reconstruct the complete Android project saved on apk-build-test.
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

# Apply the last source-level stability patch that produced the working V4.0.3/V4.0.4 builds.
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

# Add the already-proven functional export harness, while keeping the normal app UI intact.
cat functional_test/fea_parts/part-00 \
    functional_test/fea_parts/part-01 \
    functional_test/fea_parts/part-02 \
    functional_test/fea_parts/part-03 > functional-activity.b64
base64 --decode functional-activity.b64 | gzip -dc \
  > jhmin/app/src/main/java/com/bianzhifeng/jinghua/FunctionalExportActivity.java
base64 --decode functional_test/fragment_region_es2_v407.glsl.gz.b64 \
  | gzip -dc > jhmin/app/src/main/res/raw/fragment_region_es2.glsl

python3 - <<'PY'
from pathlib import Path
import re

manifest = Path('jhmin/app/src/main/AndroidManifest.xml')
text = manifest.read_text(encoding='utf-8')
needle = '        <activity\n            android:name=".HomeActivity"'
addition = '''        <activity
            android:name=".FunctionalExportActivity"
            android:exported="true"
            android:screenOrientation="portrait" />

'''
if addition not in text:
    if needle not in text:
        raise SystemExit('HomeActivity manifest anchor missing')
    text = text.replace(needle, addition + needle, 1)
manifest.write_text(text, encoding='utf-8')

files = [
    Path('jhmin/app/build.gradle'),
    Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/BuildVersion.java'),
    Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/HomeActivity.java'),
    Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/MainActivity.java'),
    Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/ExportForegroundService.java'),
    Path('jhmin/app/src/main/res/values/strings.xml'),
]
for path in files:
    if not path.exists():
        continue
    source = path.read_text(encoding='utf-8')
    source = source.replace('4.0.3', '4.0.7').replace('4.0.4', '4.0.7')
    source = re.sub(r'versionCode\s+(?:10|11|12)', 'versionCode 13', source)
    source = re.sub(r'VERSION_CODE\s*=\s*(?:10|11|12)', 'VERSION_CODE = 13', source)
    path.write_text(source, encoding='utf-8')

build = Path('jhmin/app/build.gradle').read_text(encoding='utf-8')
version = Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/BuildVersion.java').read_text(encoding='utf-8')
assert 'versionCode 13' in build, build
assert "versionName '4.0.7'" in build, build
assert 'VERSION_CODE = 13' in version, version
assert 'VERSION_NAME = "4.0.7"' in version, version
PY

grep -n "versionCode 13" jhmin/app/build.gradle
grep -n "versionName '4.0.7'" jhmin/app/build.gradle
sha256sum jhmin/app/src/main/res/raw/fragment_region_es2.glsl | tee v407-shader-sha256.txt
grep -q '^00922001dcbdbfb8524ab08fb7378ed0c88408ce937ac8594f279bf9dc8ceaea ' v407-shader-sha256.txt

gradle --no-daemon --stacktrace -p jhmin clean assembleDebug > v407-gradle.log 2>&1
APK='jhmin/app/build/outputs/apk/debug/app-debug.apk'
test -s "$APK"
unzip -t "$APK" >/dev/null
mkdir -p e2e-results functional-results
cp "$APK" e2e-results/JingHua-V4.0.7-full-source-debug.apk
sha256sum "$APK" | tee e2e-results/apk-sha256.txt

# Deterministic portrait video with audio, plus a clean reference.
FONT="$(fc-match -f '%{file}\n' 'DejaVu Sans:style=Bold' | head -n1)"
test -f "$FONT"
BASE='testsrc2=size=360x640:rate=30:duration=4.2'
AUDIO='sine=frequency=880:sample_rate=44100:duration=4.2'
VIDEO_FILTER="drawtext=fontfile=${FONT}:text='SUBTITLE TEST':fontcolor=white:fontsize=24:borderw=3:bordercolor=black:x=(w-text_w)/2:y=h*0.82,format=yuv420p"
ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf 'format=yuv420p' -c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 \
  -c:a aac -b:a 96k -shortest -movflags +faststart functional-results/clean-audio.mp4
ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf "$VIDEO_FILTER" -c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 \
  -c:a aac -b:a 96k -shortest -movflags +faststart functional-results/input-audio.mp4
cp functional-results/clean-audio.mp4 functional-results/clean.mp4
cp functional-results/input-audio.mp4 functional-results/input.mp4
ffprobe -v error -show_entries stream=codec_name,codec_type,width,height:format=duration,size \
  -of json functional-results/input-audio.mp4 > e2e-results/input-probe.json

echo V407_PREPARE_PASS
