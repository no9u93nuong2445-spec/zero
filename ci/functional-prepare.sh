#!/usr/bin/env bash
set -euo pipefail
exec > >(tee functional-prepare.log) 2>&1

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

cp functional_test/FunctionalExportActivity.java \
  jhmin/app/src/main/java/com/bianzhifeng/jinghua/FunctionalExportActivity.java
python3 - <<'PY'
from pathlib import Path
manifest = Path('jhmin/app/src/main/AndroidManifest.xml')
text = manifest.read_text(encoding='utf-8')
needle = '        <activity\n            android:name=".HomeActivity"'
addition = '''        <activity\n            android:name=".FunctionalExportActivity"\n            android:exported="true"\n            android:screenOrientation="portrait" />\n\n'''
if addition not in text:
    text = text.replace(needle, addition + needle, 1)
manifest.write_text(text, encoding='utf-8')
PY

gradle --no-daemon --stacktrace -p jhmin clean assembleDebug > functional-gradle.log 2>&1
test -s jhmin/app/build/outputs/apk/debug/app-debug.apk
unzip -t jhmin/app/build/outputs/apk/debug/app-debug.apk >/dev/null

mkdir -p functional-results
FONT="$(fc-match -f '%{file}\n' 'DejaVu Sans:style=Bold' | head -n1)"
test -f "$FONT"
printf 'font=%s\n' "$FONT"
BASE="testsrc2=size=360x640:rate=30:duration=4.2"
VIDEO_FILTER="drawtext=fontfile=${FONT}:text='SUBTITLE TEST 2026':fontcolor=white:fontsize=28:borderw=4:bordercolor=black:x=(w-text_w)/2:y=h*0.82,format=yuv420p"

ffmpeg -y -v warning -f lavfi -i "$BASE" \
  -vf "format=yuv420p" -c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 \
  -an -movflags +faststart functional-results/clean-noaudio.mp4
ffmpeg -y -v warning -f lavfi -i "$BASE" \
  -vf "$VIDEO_FILTER" -c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 \
  -an -movflags +faststart functional-results/input-noaudio.mp4
ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "sine=frequency=880:sample_rate=44100:duration=4.2" \
  -vf "$VIDEO_FILTER" -c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 \
  -c:a aac -b:a 96k -shortest -movflags +faststart functional-results/input-audio.mp4
cp functional-results/clean-noaudio.mp4 functional-results/clean.mp4
cp functional-results/input-audio.mp4 functional-results/input.mp4
for f in functional-results/clean-noaudio.mp4 functional-results/input-noaudio.mp4 functional-results/input-audio.mp4; do
  ffprobe -v error -show_entries format=duration,size -of json "$f"
done
