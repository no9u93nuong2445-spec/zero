#!/usr/bin/env bash
set -euo pipefail
exec > >(tee v410-prepare.log) 2>&1

# Reconstruct the complete Android source without building intermediate versions.
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

# Apply the proven V4.0.3 stability patch.
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

# Install the CI-only functional export activity and verified V4.0.7 shader base.
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
manifest=Path('jhmin/app/src/main/AndroidManifest.xml')
text=manifest.read_text(encoding='utf-8')
needle='        <activity\n            android:name=".HomeActivity"'
addition='''        <activity
            android:name=".FunctionalExportActivity"
            android:exported="true"
            android:screenOrientation="portrait" />

'''
if addition not in text:
    if needle not in text:
        raise SystemExit('HomeActivity manifest anchor missing')
    text=text.replace(needle,addition+needle,1)
manifest.write_text(text,encoding='utf-8')

files=[
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
    source=path.read_text(encoding='utf-8')
    source=source.replace('4.0.3','4.0.7').replace('4.0.4','4.0.7')
    source=re.sub(r'versionCode\s+(?:10|11|12)','versionCode 13',source)
    source=re.sub(r'VERSION_CODE\s*=\s*(?:10|11|12)','VERSION_CODE = 13',source)
    path.write_text(source,encoding='utf-8')
PY

# Apply the complete-erasure engine, simplified product UI, release hardening,
# and the measured one-line/two-line subtitle-band tuning.
python3 v408_patch/apply_patch.py
python3 v409_patch/apply_patch.py
python3 v409_patch/fix_generated_java_strings.py
python3 v409_patch/add_direct_video_intent.py
python3 v410_patch/apply_release_patch.py
python3 v410_patch/harden_functional_activity.py
python3 v410_patch/tune_subtitle_region.py

# Compile only once at the final version.
gradle --no-daemon --stacktrace -p jhmin clean assembleDebug > v410-gradle.log 2>&1
APK='jhmin/app/build/outputs/apk/debug/app-debug.apk'
test -s "$APK"
unzip -t "$APK" >/dev/null

mkdir -p v410-results/apk v410-results/fixtures
cp "$APK" v410-results/apk/JingHua-V4.1.0-release-candidate.apk
sha256sum "$APK" | tee v410-results/apk/JingHua-V4.1.0-SHA256.txt

# Deterministic portrait fixtures with audio.
FONT="$(fc-match -f '%{file}\n' 'DejaVu Sans:style=Bold' | head -n1)"
test -f "$FONT"
BASE='testsrc2=size=360x640:rate=30:duration=4.2'
AUDIO='sine=frequency=880:sample_rate=44100:duration=4.2'
COMMON=(-c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 -pix_fmt yuv420p -c:a aac -b:a 96k -shortest -movflags +faststart)
ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf 'format=yuv420p' "${COMMON[@]}" v410-results/fixtures/clean.mp4
ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf "drawtext=fontfile=${FONT}:text='WHITE OUTLINE':fontcolor=white:fontsize=28:borderw=5:bordercolor=black:x=(w-text_w)/2:y=h*0.81,format=yuv420p" \
  "${COMMON[@]}" v410-results/fixtures/white-outline.mp4
ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf "drawtext=fontfile=${FONT}:text='COLOR SUBTITLE':fontcolor=cyan:fontsize=27:borderw=5:bordercolor=red:x=(w-text_w)/2:y=h*0.81,format=yuv420p" \
  "${COMMON[@]}" v410-results/fixtures/color-outline.mp4
ffmpeg -y -v warning -f lavfi -i "$BASE" -f lavfi -i "$AUDIO" \
  -vf "drawtext=fontfile=${FONT}:text='DOUBLE LINE ONE':fontcolor=white@0.88:fontsize=24:borderw=4:bordercolor=black@0.95:x=(w-text_w)/2:y=h*0.765,drawtext=fontfile=${FONT}:text='DOUBLE LINE TWO':fontcolor=white@0.88:fontsize=24:borderw=4:bordercolor=black@0.95:x=(w-text_w)/2:y=h*0.835,format=yuv420p" \
  "${COMMON[@]}" v410-results/fixtures/double-line.mp4

python3 - <<'PY'
from pathlib import Path
import json
main=Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/MainActivity.java').read_text(encoding='utf-8')
overlay=Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/SelectionOverlayView.java').read_text(encoding='utf-8')
functional=Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/FunctionalExportActivity.java').read_text(encoding='utf-8')
shader=Path('jhmin/app/src/main/res/raw/fragment_region_es2.glsl').read_text(encoding='utf-8')
checks={
 'version_410': "versionName '4.1.0'" in Path('jhmin/app/build.gradle').read_text(encoding='utf-8'),
 'simple_ui': '3. 导出完整去字幕视频' in main and 'page.removeAllViews();' in main,
 'picker_fallback': 'ActivityNotFoundException' in main and 'ACTION_OPEN_DOCUMENT' in main,
 'autoplay_retry': '850L' in main and 'videoView.setKeepScreenOn(true)' in main,
 'full_frame_locked': 'exportSelectedTargetShortSide = -1;' in main and 'int targetShortSide = -1;' in main,
 'integrity_gate': 'MediaIntegrityValidator.validate' in main,
 'large_simple_handles': 'float radius = dp(10);' in overlay and 'float hit = dp(38);' in overlay,
 'no_rotate_handle': 'distanceTo(rotationHandle(pose), x, y) <= hit' not in overlay,
 'expanded_erasure_rim': 'pose.zw * vec2(0.045, 0.25)' in shader,
 'watchdog_export_test': 'completion_source=' in functional,
 'tuned_default_region': 'new RectF(0.04f, 0.735f, 0.96f, 0.900f)' in main,
 'tuned_test_region': 'new RectF(0.08f, 0.735f, 0.92f, 0.900f)' in functional,
}
Path('v410-results/source-assertions.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2),encoding='utf-8')
assert all(checks.values()), checks
PY

echo V410_PREPARE_PASS
