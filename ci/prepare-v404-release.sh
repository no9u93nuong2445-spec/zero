#!/usr/bin/env bash
set -euo pipefail

cat source_parts/part-03-hex-00 source_parts/part-03-hex-01 \
  source_parts/part-03-hex-02 source_parts/part-03-hex-03 \
  source_parts/part-03-hex-04 source_parts/part-03-hex-05-00 \
  source_parts/part-03-hex-05-01 source_parts/part-03-hex-05-02 \
  source_parts/part-03-hex-05-03 source_parts/part-03-hex-06-00 \
  source_parts/part-03-hex-07-00 > part03.hex
python3 - <<'PY'
from pathlib import Path
Path('part03.b64').write_bytes(bytes.fromhex(Path('part03.hex').read_text().strip()))
PY
cat source_parts/part-00 source_parts/part-01 \
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

base64 --decode functional_test/fragment_region_es2_v404.glsl.gz.b64 \
  | gzip -dc > jhmin/app/src/main/res/raw/fragment_region_es2.glsl
python3 functional_test/apply_v404d.py

python3 - <<'PY'
from pathlib import Path
files = [
    Path('jhmin/app/build.gradle'),
    Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/BuildVersion.java'),
    Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/HomeActivity.java'),
    Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/MainActivity.java'),
    Path('jhmin/app/src/main/res/values/strings.xml'),
]
for path in files:
    if not path.exists():
        continue
    text = path.read_text(encoding='utf-8')
    text = text.replace("versionCode 10", "versionCode 11")
    text = text.replace("4.0.3", "4.0.4")
    text = text.replace("VERSION_CODE = 10", "VERSION_CODE = 11")
    path.write_text(text, encoding='utf-8')
assert "versionCode 11" in Path('jhmin/app/build.gradle').read_text()
assert "versionName '4.0.4'" in Path('jhmin/app/build.gradle').read_text()
assert 'VERSION_NAME = "4.0.4"' in Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/BuildVersion.java').read_text()
PY

grep -n "versionCode 11" jhmin/app/build.gradle
grep -n "versionName '4.0.4'" jhmin/app/build.gradle
