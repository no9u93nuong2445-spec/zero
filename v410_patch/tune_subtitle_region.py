#!/usr/bin/env python3
from pathlib import Path

JAVA = Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua')


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected 1 occurrence, got {count}: {old!r}')
    path.write_text(text.replace(old, new, 1), encoding='utf-8')


main_path = JAVA / 'MainActivity.java'
functional_path = JAVA / 'FunctionalExportActivity.java'

# The complete-erasure shader expands the user box by 25% vertically. This
# pre-expansion box produces an effective band around y=0.714..0.921, which
# covers normal one-line and two-line bottom subtitles without reconstructing
# an unnecessarily large part of the video.
replace_once(
    main_path,
    'new RectF(0.04f, 0.72f, 0.96f, 0.95f)',
    'new RectF(0.04f, 0.735f, 0.96f, 0.900f)',
)

# Keep the deterministic Android export fixture aligned with the actual product
# default. The wider x range also covers long captions while the vertical
# samples remain outside the glyph band.
replace_once(
    functional_path,
    'new RectF(0.12f, 0.805f, 0.88f, 0.885f)',
    'new RectF(0.08f, 0.735f, 0.92f, 0.900f)',
)

# Give the simple editor one stable accessibility identity. This is useful to
# TalkBack users and lets device validation verify the real editor without
# depending on controls that may be below the current scroll viewport.
replace_once(
    main_path,
    '''        page.removeAllViews();
        page.setPadding(dp(18), dp(18), dp(18), dp(34));''',
    '''        page.removeAllViews();
        page.setContentDescription("净画简单编辑器：选择视频、预览去字幕、导出完整视频");
        page.setPadding(dp(18), dp(18), dp(18), dp(34));''',
)

# Add deterministic playback signals after the real VideoView has started. The
# delayed signal confirms both isPlaying() and that the playback position moved.
replace_once(
    main_path,
    '''            statusText.setText("视频正在播放。点画面可暂停，拖动黄色框覆盖字幕。");''',
    '''            statusText.setText("视频正在播放。点画面可暂停，拖动黄色框覆盖字幕。");
            android.util.Log.i("JingHuaPlayback", "AUTOPLAY_READY");
            mainHandler.postDelayed(() -> android.util.Log.i(
                    "JingHuaPlayback",
                    "AUTOPLAY_CONFIRMED playing=" + videoView.isPlaying()
                            + " position=" + videoView.getCurrentPosition()), 1800L);''',
)
replace_once(
    main_path,
    '''            statusText.setText("视频预览失败，请重新选择该视频");''',
    '''            statusText.setText("视频预览失败，请重新选择该视频");
            android.util.Log.e("JingHuaPlayback", "PREVIEW_ERROR what=" + what + " extra=" + extra);''',
)

main = main_path.read_text(encoding='utf-8')
functional = functional_path.read_text(encoding='utf-8')
assert 'new RectF(0.04f, 0.735f, 0.96f, 0.900f)' in main
assert 'new RectF(0.08f, 0.735f, 0.92f, 0.900f)' in functional
assert 'AUTOPLAY_READY' in main and 'AUTOPLAY_CONFIRMED' in main
assert '净画简单编辑器：选择视频、预览去字幕、导出完整视频' in main
print('V410_SUBTITLE_REGION_AND_RUNTIME_SIGNALS_TUNED')
