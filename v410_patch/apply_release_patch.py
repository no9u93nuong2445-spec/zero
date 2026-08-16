#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path('jhmin')
JAVA = ROOT / 'app/src/main/java/com/bianzhifeng/jinghua'
RAW = ROOT / 'app/src/main/res/raw'


def read(path: Path) -> str:
    return path.read_text(encoding='utf-8')


def write(path: Path, text: str) -> None:
    path.write_text(text, encoding='utf-8')


def replace_once(path: Path, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected 1 occurrence, got {count}: {old[:120]!r}')
    write(path, text.replace(old, new, 1))


def method_bounds(text: str, signature: str) -> tuple[int, int]:
    start = text.index(signature)
    brace = text.index('{', start)
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == '{':
            depth += 1
        elif text[index] == '}':
            depth -= 1
            if depth == 0:
                return start, index + 1
    raise RuntimeError(f'unclosed method: {signature}')


# Version bump: user calls this 4.10; Android semantic version is 4.1.0.
build = ROOT / 'app/build.gradle'
text = read(build)
text, c1 = re.subn(r'versionCode\s+15', 'versionCode 16', text, count=1)
text, c2 = re.subn(r"versionName\s+'4\.0\.9'", "versionName '4.1.0'", text, count=1)
if c1 != 1 or c2 != 1:
    raise RuntimeError(f'build version patch failed: {c1}, {c2}')
write(build, text)

version = JAVA / 'BuildVersion.java'
text = read(version)
text, c1 = re.subn(r'VERSION_CODE\s*=\s*15', 'VERSION_CODE = 16', text, count=1)
text, c2 = re.subn(r'VERSION_NAME\s*=\s*"4\.0\.9"', 'VERSION_NAME = "4.1.0"', text, count=1)
if c1 != 1 or c2 != 1:
    raise RuntimeError(f'BuildVersion patch failed: {c1}, {c2}')
write(version, text)

for path in [JAVA / 'HomeActivity.java', JAVA / 'MainActivity.java', JAVA / 'ExportForegroundService.java']:
    if path.exists():
        write(path, read(path).replace('V4.0.9', 'V4.1.0').replace('4.0.9', '4.1.0'))

# Create a strict runtime validator: full frame, duration and audio must survive.
validator = JAVA / 'MediaIntegrityValidator.java'
write(validator, r'''package com.bianzhifeng.jinghua;

import android.content.Context;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import java.io.File;

/** Rejects incomplete/cropped exports before they are published to the gallery. */
public final class MediaIntegrityValidator {
    public static final class Result {
        public final boolean valid;
        public final String message;
        private Result(boolean valid, String message) {
            this.valid = valid;
            this.message = message == null ? "" : message;
        }
        public static Result ok() { return new Result(true, ""); }
        public static Result fail(String message) { return new Result(false, message); }
    }

    private static final class Snapshot {
        int width;
        int height;
        long durationMs;
        boolean hasAudio;
    }

    private MediaIntegrityValidator() {}

    public static Result validate(Context context, Uri inputUri, File outputFile) {
        if (inputUri == null) return Result.fail("无法确认原视频信息");
        if (outputFile == null || !outputFile.isFile() || outputFile.length() < 4096L) {
            return Result.fail("导出文件无效");
        }
        Snapshot input = readInput(context, inputUri);
        Snapshot output = readOutput(outputFile);
        if (input == null || output == null) return Result.fail("无法读取导出视频信息");
        if (input.width != output.width || input.height != output.height) {
            return Result.fail("导出画面尺寸异常：应为 " + input.width + "×" + input.height
                    + "，实际为 " + output.width + "×" + output.height);
        }
        long tolerance = Math.max(650L, Math.round(input.durationMs * 0.025));
        if (Math.abs(input.durationMs - output.durationMs) > tolerance) {
            return Result.fail("导出时长异常");
        }
        if (input.hasAudio && !output.hasAudio) {
            return Result.fail("导出视频缺少原声音");
        }
        return Result.ok();
    }

    private static Snapshot readInput(Context context, Uri uri) {
        MediaMetadataRetriever retriever = new MediaMetadataRetriever();
        try {
            retriever.setDataSource(context, uri);
            return snapshot(retriever);
        } catch (RuntimeException error) {
            return null;
        } finally {
            MediaResourceUtils.releaseQuietly(retriever);
        }
    }

    private static Snapshot readOutput(File file) {
        MediaMetadataRetriever retriever = new MediaMetadataRetriever();
        try {
            retriever.setDataSource(file.getAbsolutePath());
            return snapshot(retriever);
        } catch (RuntimeException error) {
            return null;
        } finally {
            MediaResourceUtils.releaseQuietly(retriever);
        }
    }

    private static Snapshot snapshot(MediaMetadataRetriever retriever) {
        Snapshot result = new Snapshot();
        result.width = parseInt(retriever.extractMetadata(
                MediaMetadataRetriever.METADATA_KEY_VIDEO_WIDTH));
        result.height = parseInt(retriever.extractMetadata(
                MediaMetadataRetriever.METADATA_KEY_VIDEO_HEIGHT));
        int rotation = parseInt(retriever.extractMetadata(
                MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION));
        if (rotation == 90 || rotation == 270) {
            int swap = result.width;
            result.width = result.height;
            result.height = swap;
        }
        result.durationMs = parseLong(retriever.extractMetadata(
                MediaMetadataRetriever.METADATA_KEY_DURATION));
        String hasAudio = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_HAS_AUDIO);
        result.hasAudio = "yes".equalsIgnoreCase(hasAudio) || "true".equalsIgnoreCase(hasAudio);
        return result.width > 0 && result.height > 0 && result.durationMs > 0 ? result : null;
    }

    private static int parseInt(String value) {
        try { return Integer.parseInt(value == null ? "0" : value); }
        catch (NumberFormatException error) { return 0; }
    }

    private static long parseLong(String value) {
        try { return Long.parseLong(value == null ? "0" : value); }
        catch (NumberFormatException error) { return 0L; }
    }
}
''')

main = JAVA / 'MainActivity.java'
text = read(main)

# Picker fallback: Photo Picker -> gallery -> open-document.
if 'import android.content.ActivityNotFoundException;' not in text:
    text = text.replace('import android.app.AlertDialog;\n',
                        'import android.app.AlertDialog;\nimport android.content.ActivityNotFoundException;\n', 1)
start, end = method_bounds(text, '    private void openVideoPicker()')
new_picker = '''    private void openVideoPicker() {
        if (exporting || cloudRepairing || tracking || subtitleScanning) {
            toast("请先完成或取消当前导出");
            return;
        }
        Intent primary;
        if (Build.VERSION.SDK_INT >= 33) {
            primary = new Intent(MediaStore.ACTION_PICK_IMAGES);
            primary.setType("video/*");
        } else {
            primary = new Intent(Intent.ACTION_PICK, MediaStore.Video.Media.EXTERNAL_CONTENT_URI);
            primary.setType("video/*");
        }
        primary.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try {
            startActivityForResult(primary, REQUEST_PICK_VIDEOS);
            return;
        } catch (ActivityNotFoundException ignored) {
            // Fall through to a broadly supported document picker.
        }
        Intent fallback = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        fallback.addCategory(Intent.CATEGORY_OPENABLE);
        fallback.setType("video/*");
        fallback.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        try {
            startActivityForResult(fallback, REQUEST_PICK_VIDEOS);
        } catch (ActivityNotFoundException error) {
            toast("手机没有可用的视频选择器");
        }
    }'''
text = text[:start] + new_picker + text[end:]

# Harden playback: click-to-pause, keep screen awake and retry once if the first
# automatic start is delayed by the device decoder.
start, end = method_bounds(text, '    private void loadPreviewVideo(Uri uri)')
method = text[start:end]
method = method.replace(
    '''        videoView.stopPlayback();
        videoView.setOnPreparedListener(player -> {''',
    '''        videoView.stopPlayback();
        videoView.setKeepScreenOn(true);
        videoView.setOnClickListener(v -> togglePlayback());
        videoView.setOnPreparedListener(player -> {''', 1)
method = method.replace(
    '''            videoView.start();
            mainHandler.post(timelinePoller);
            statusText.setText("视频正在播放。拖动黄色框完整覆盖字幕即可。");''',
    '''            videoView.start();
            mainHandler.post(timelinePoller);
            mainHandler.postDelayed(() -> {
                if (uri.equals(previewUri) && !videoView.isPlaying()) {
                    videoView.seekTo(1);
                    videoView.start();
                    mainHandler.post(timelinePoller);
                }
            }, 850L);
            statusText.setText("视频正在播放。点画面可暂停，拖动黄色框覆盖字幕。");''', 1)
method = method.replace(
    '''        autoFallbackCheck.setChecked(false);
        updateTimelineUi(0L, false);''',
    '''        autoFallbackCheck.setChecked(false);
        retry720Check.setChecked(false);
        exportButton.setEnabled(true);
        previewButton.setEnabled(true);
        updateTimelineUi(0L, false);''', 1)
text = text[:start] + method + text[end:]

# Add one simple post-export action; no professional controls return.
content_start, content_end = method_bounds(text, '    private View buildContent()')
content = text[content_start:content_end]
anchor = '''        page.addView(progressBar);
        page.addView(statusText);
        page.addView(cancelButton);'''
replacement = '''        page.addView(progressBar);
        page.addView(statusText);
        page.addView(cancelButton);
        openLastButton.setText("查看导出视频");
        openLastButton.setTextSize(16f);
        page.addView(openLastButton, matchWrap(50));'''
if content.count(anchor) != 1:
    raise RuntimeError('simple export controls anchor missing')
content = content.replace(anchor, replacement, 1)
content = content.replace(
    '''        retry720Check.setChecked(false);
        return scroll;''',
    '''        retry720Check.setChecked(false);
        exportButton.setEnabled(previewUri != null);
        previewButton.setEnabled(previewUri != null);
        return scroll;''', 1)
text = text[:content_start] + content + text[content_end:]

# Hard-lock original size, full-frame output and one repair mode.
text = text.replace('exportSelectedTargetShortSide = selectedTargetShortSide();',
                    'exportSelectedTargetShortSide = -1;', 1)
text = text.replace('exportTemporalRepairEnabled = temporalRepairCheck.isChecked();',
                    'exportTemporalRepairEnabled = true;', 1)
text = text.replace('exportAutoFallbackEnabled = autoFallbackCheck.isChecked();',
                    'exportAutoFallbackEnabled = false;', 1)
text = text.replace(
    '''        exportCropEnabled = false;
        exportCropRect = null;''',
    '''        exportCropEnabled = false;
        exportCropRect = null;
        retry720Check.setChecked(false);''', 1)
text = text.replace(
    '''        int targetShortSide = forcedTargetShortSide > 0
                ? forcedTargetShortSide
                : exportSelectedTargetShortSide;''',
    '''        int targetShortSide = -1;''', 1)

# Validate the transformed file before it reaches the gallery.
completion_anchor = '''        int itemNumber = exportIndex + 1;
        ioExecutor.execute(() -> publishToGallery(completedFile, itemNumber));'''
completion_replacement = '''        MediaIntegrityValidator.Result integrity = MediaIntegrityValidator.validate(
                this, currentExportUri, completedFile);
        if (!integrity.valid) {
            onTransformFailed(new IOException(integrity.message));
            return;
        }
        int itemNumber = exportIndex + 1;
        ioExecutor.execute(() -> publishToGallery(completedFile, itemNumber));'''
if text.count(completion_anchor) != 1:
    raise RuntimeError('export integrity anchor missing')
text = text.replace(completion_anchor, completion_replacement, 1)

# Keep the screen awake while encoding and release it on every terminal path.
text = text.replace(
    '''        exporting = true;
        setExportUi(true);''',
    '''        exporting = true;
        getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        setExportUi(true);''', 1)
text = text.replace(
    '''        setExportUi(false);
        ExportForegroundService.stop(this);
        progressBar.setVisibility(View.VISIBLE);''',
    '''        setExportUi(false);
        getWindow().clearFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        ExportForegroundService.stop(this);
        progressBar.setVisibility(View.VISIBLE);''', 1)
text = text.replace(
    '''        setExportUi(false);
        progressBar.setVisibility(View.GONE);''',
    '''        setExportUi(false);
        getWindow().clearFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        progressBar.setVisibility(View.GONE);''', 1)

# Clear, human-readable completion message.
text = text.replace(
    '''        statusText.setText(
                "批量完成：成功 " + exportSuccessCount + " 条，失败 " + exportFailureCount
                        + " 条。输出在 相册 / Movies / 净画");''',
    '''        if (exportSuccessCount == 1 && exportFailureCount == 0) {
            statusText.setText("导出成功：完整视频已保存到相册 / Movies / 净画");
        } else {
            statusText.setText("处理完成：成功 " + exportSuccessCount + " 条，失败 "
                    + exportFailureCount + " 条");
        }''', 1)

write(main, text)

# Simplify and enlarge the only visible selection box.
overlay = JAVA / 'SelectionOverlayView.java'
text = read(overlay)
text = text.replace('setContentDescription("字幕时间段或水印自动跟踪区域");',
                    'setContentDescription("拖动黄色字幕框选择需要去除的字幕");', 1)
text = text.replace('String label = "区域 " + (i + 1) + " · " + modeName(track.mode);',
                    'String label = "字幕区域";', 1)
sub_block = '''            String state = track.enabled ? (inTime ? "生效中" : "时间段外") : "已停用";
            String tracking = track.autoTracked
                    ? " · 跟踪" + Math.round(track.trackingConfidence * 100f) + "%"
                    : "";
            String detected = track.autoDetected
                    ? " · 字幕" + track.activeRangeCount() + "段"
                    : "";
            canvas.drawText(state + " · K" + track.keyframeCount() + tracking + detected,
                    bounds.left + dp(7), bounds.top + dp(31), subLabelPaint);
'''
if text.count(sub_block) != 1:
    raise RuntimeError('overlay professional label block missing')
text = text.replace(sub_block, '', 1)
text = text.replace('float radius = dp(6);', 'float radius = dp(10);', 1)
rotate_draw = '''                PointF topMid = midpoint(corners[0], corners[1]);
                PointF rotateHandle = rotationHandle(pose);
                canvas.drawLine(topMid.x, topMid.y, rotateHandle.x, rotateHandle.y, rotateLinePaint);
                canvas.drawCircle(rotateHandle.x, rotateHandle.y, dp(7), handlePaint);
'''
if text.count(rotate_draw) != 1:
    raise RuntimeError('overlay rotate draw block missing')
text = text.replace(rotate_draw, '', 1)
text = text.replace('float pad = dp(20);', 'float pad = dp(30);', 1)
text = text.replace(
    '            if (i == activeIndex && distanceTo(rotationHandle(pose), x, y) <= pad) return i;\n',
    '', 1)
text = text.replace('float hit = dp(24);', 'float hit = dp(38);', 1)
text = text.replace(
    '        if (distanceTo(rotationHandle(pose), x, y) <= hit) return DRAG_ROTATE;\n',
    '', 1)
write(overlay, text)

# Slightly widen the automatic safe rim so outlines/shadows cannot survive.
shader = RAW / 'fragment_region_es2.glsl'
text = read(shader)
text = text.replace('pose.zw * vec2(0.035, 0.20)', 'pose.zw * vec2(0.045, 0.25)', 1)
text = text.replace('0.0, 0.038, clamp(min(depthX, depthY), 0.0, 1.0)',
                    '0.0, 0.026, clamp(min(depthX, depthY), 0.0, 1.0)', 1)
write(shader, text)

preview = JAVA / 'PreviewRenderer.java'
text = read(preview)
text = text.replace('rect.width() * 0.035f', 'rect.width() * 0.045f', 1)
text = text.replace('rect.height() * 0.20f', 'rect.height() * 0.25f', 1)
write(preview, text)

# Release invariants.
assert "versionCode 16" in read(build)
assert "versionName '4.1.0'" in read(build)
assert "VERSION_CODE = 16" in read(version)
assert 'VERSION_NAME = "4.1.0"' in read(version)
assert validator.exists()
assert 'MediaIntegrityValidator.validate' in read(main)
assert 'exportSelectedTargetShortSide = -1;' in read(main)
assert 'int targetShortSide = -1;' in read(main)
assert 'ActivityNotFoundException' in read(main)
assert '查看导出视频' in read(main)
assert 'float radius = dp(10);' in read(overlay)
assert 'DRAG_ROTATE' not in read(overlay)[read(overlay).find('private int resolveDragMode'):read(overlay).find('private void updateWorkingPose')]
assert 'pose.zw * vec2(0.045, 0.25)' in read(shader)
print('V410_RELEASE_PATCH_APPLIED')
