#!/usr/bin/env bash
set -euo pipefail
exec > >(tee v409-prepare.log) 2>&1

# Reconstruct the proven complete source and apply V4.0.8 complete-erasure first.
bash ci/v408-prepare.sh

# Layer the user-facing simplification and full-frame fixes.
python3 v409_patch/apply_patch.py

grep -n "versionCode 15" jhmin/app/build.gradle
grep -n "versionName '4.0.9'" jhmin/app/build.gradle
grep -n 'VERSION_NAME = "4.0.9"' jhmin/app/src/main/java/com/bianzhifeng/jinghua/BuildVersion.java
grep -n 'MediaStore.ACTION_PICK_IMAGES' jhmin/app/src/main/java/com/bianzhifeng/jinghua/MainActivity.java
grep -n 'videoView.start();' jhmin/app/src/main/java/com/bianzhifeng/jinghua/MainActivity.java
grep -n 'exportCropEnabled = false;' jhmin/app/src/main/java/com/bianzhifeng/jinghua/MainActivity.java
grep -n '选择视频开始去字幕' jhmin/app/src/main/java/com/bianzhifeng/jinghua/HomeActivity.java

gradle --no-daemon --stacktrace -p jhmin clean assembleDebug > v409-gradle.log 2>&1
APK='jhmin/app/build/outputs/apk/debug/app-debug.apk'
test -s "$APK"
unzip -t "$APK" >/dev/null

mkdir -p v409-results/apk v409-results/fixtures
cp "$APK" v409-results/apk/JingHua-V4.0.9-simple-debug.apk
sha256sum "$APK" | tee v409-results/apk/JingHua-V4.0.9-SHA256.txt
cp v408-results/fixtures/clean.mp4 v409-results/fixtures/clean.mp4
cp v408-results/fixtures/white-outline.mp4 v409-results/fixtures/subtitle.mp4

python3 - <<'PY'
from pathlib import Path
main=Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/MainActivity.java').read_text(encoding='utf-8')
home=Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/HomeActivity.java').read_text(encoding='utf-8')
checks={
 'simple_home': '选择视频开始去字幕' in home and '快速模板' not in home,
 'simple_editor': '3. 导出完整去字幕视频' in main and 'page.removeAllViews();' in main,
 'large_picker': 'MediaStore.ACTION_PICK_IMAGES' in main,
 'autoplay': 'videoView.start();' in main and '视频正在播放' in main,
 'no_crop': 'exportCropEnabled = false;' in main and 'cropCheck.setChecked(false);' in main,
 'hq_default': 'overlayView.setActiveMode(RegionEffect.MODE_REPAIR_HQ);' in main and 'overlayView.setActiveStrength(1.0f);' in main,
}
Path('v409-results/source-assertions.json').write_text(__import__('json').dumps(checks,ensure_ascii=False,indent=2),encoding='utf-8')
assert all(checks.values()), checks
PY

echo V409_PREPARE_PASS
