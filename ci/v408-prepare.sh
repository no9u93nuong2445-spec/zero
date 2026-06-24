#!/usr/bin/env bash
set -euo pipefail
exec > >(tee v408-prepare.log) 2>&1

# Reuse the proven complete-source reconstruction and CI harness, then layer the
# V4.0.8 production patch on top and rebuild from Java/GLSL sources.
bash ci/v407-e2e-prepare.sh
python3 v408_patch/apply_patch.py

grep -n "versionCode 14" jhmin/app/build.gradle
grep -n "versionName '4.0.8'" jhmin/app/build.gradle
grep -n 'VERSION_NAME = "4.0.8"' jhmin/app/src/main/java/com/bianzhifeng/jinghua/BuildVersion.java
grep -n '彻底去字（推荐）' jhmin/app/src/main/java/com/bianzhifeng/jinghua/MainActivity.java
grep -n 'MODE_REPAIR_HQ, 1.0f' jhmin/app/src/main/java/com/bianzhifeng/jinghua/TemplateStore.java

gradle --no-daemon --stacktrace -p jhmin clean assembleDebug > v408-gradle.log 2>&1
APK='jhmin/app/build/outputs/apk/debug/app-debug.apk'
test -s "$APK"
unzip -t "$APK" >/dev/null

mkdir -p v408-results/fixtures v408-results/apk
cp "$APK" v408-results/apk/JingHua-V4.0.8-debug.apk
sha256sum "$APK" | tee v408-results/apk/JingHua-V4.0.8-SHA256.txt

# Controlled portrait fixtures share exactly the same moving background and
# audio. Only the subtitle raster changes, so repaired output can be compared
# fairly against a clean Android-transcoded reference.
FONT="$(fc-match -f '%{file}\n' 'DejaVu Sans:style=Bold' | head -n1)"
test -f "$FONT"
BASE='testsrc2=size=360x640:rate=30:duration=4.2'
AUDIO='sine=frequency=880:sample_rate=44100:duration=4.2'
COMMON=(-c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 -pix_fmt yuv420p -c:a aac -b:a 96k -shortest -movflags +faststart)

ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf 'format=yuv420p' "${COMMON[@]}" v408-results/fixtures/clean.mp4

ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf "drawtext=fontfile=${FONT}:text='WHITE OUTLINE':fontcolor=white:fontsize=28:borderw=5:bordercolor=black:x=(w-text_w)/2:y=h*0.81,format=yuv420p" \
  "${COMMON[@]}" v408-results/fixtures/white-outline.mp4

ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf "drawtext=fontfile=${FONT}:text='YELLOW SHADOW':fontcolor=yellow:fontsize=28:borderw=3:bordercolor=black:shadowx=4:shadowy=4:shadowcolor=black@0.9:x=(w-text_w)/2:y=h*0.81,format=yuv420p" \
  "${COMMON[@]}" v408-results/fixtures/yellow-shadow.mp4

ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf "drawtext=fontfile=${FONT}:text='COLOR SUBTITLE':fontcolor=cyan:fontsize=27:borderw=5:bordercolor=red:x=(w-text_w)/2:y=h*0.81,format=yuv420p" \
  "${COMMON[@]}" v408-results/fixtures/color-outline.mp4

ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf "drawtext=fontfile=${FONT}:text='DOUBLE LINE ONE':fontcolor=white@0.86:fontsize=24:borderw=4:bordercolor=black@0.95:x=(w-text_w)/2:y=h*0.765,drawtext=fontfile=${FONT}:text='DOUBLE LINE TWO':fontcolor=white@0.86:fontsize=24:borderw=4:bordercolor=black@0.95:x=(w-text_w)/2:y=h*0.835,format=yuv420p" \
  "${COMMON[@]}" v408-results/fixtures/double-line.mp4

for f in v408-results/fixtures/*.mp4; do
  ffprobe -v error -show_entries stream=codec_name,codec_type,width,height:format=duration,size \
    -of json "$f" > "${f%.mp4}.probe.json"
done

python3 - <<'PY'
import base64, gzip, hashlib
from pathlib import Path
payload=Path('v408_patch/fragment_region_v408.glsl.gz.b64').read_text().strip()
shader=gzip.decompress(base64.b64decode(payload))
installed=Path('jhmin/app/src/main/res/raw/fragment_region_es2.glsl').read_bytes()
assert installed == shader
Path('v408-results/shader-sha256.txt').write_text(hashlib.sha256(installed).hexdigest()+'  fragment_region_es2.glsl\n')
PY

echo V408_PREPARE_PASS
