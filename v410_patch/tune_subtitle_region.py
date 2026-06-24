#!/usr/bin/env python3
from pathlib import Path

JAVA = Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua')


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected 1 occurrence, got {count}: {old!r}')
    path.write_text(text.replace(old, new, 1), encoding='utf-8')


# The complete-erasure shader expands the user box by 25% vertically. This
# pre-expansion box produces an effective band around y=0.714..0.921, which
# covers normal one-line and two-line bottom subtitles without reconstructing
# an unnecessarily large part of the video.
replace_once(
    JAVA / 'MainActivity.java',
    'new RectF(0.04f, 0.72f, 0.96f, 0.95f)',
    'new RectF(0.04f, 0.735f, 0.96f, 0.900f)',
)

# Keep the deterministic Android export fixture aligned with the actual product
# default. The wider x range also covers long captions while the vertical
# samples remain outside the glyph band.
replace_once(
    JAVA / 'FunctionalExportActivity.java',
    'new RectF(0.12f, 0.805f, 0.88f, 0.885f)',
    'new RectF(0.08f, 0.735f, 0.92f, 0.900f)',
)

main = (JAVA / 'MainActivity.java').read_text(encoding='utf-8')
functional = (JAVA / 'FunctionalExportActivity.java').read_text(encoding='utf-8')
assert 'new RectF(0.04f, 0.735f, 0.96f, 0.900f)' in main
assert 'new RectF(0.08f, 0.735f, 0.92f, 0.900f)' in functional
print('V410_SUBTITLE_REGION_TUNED')
