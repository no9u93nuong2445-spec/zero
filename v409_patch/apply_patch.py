from pathlib import Path
import re
ROOT=Path('jhmin')
JAVA=ROOT/'app/src/main/java/com/bianzhifeng/jinghua'

def read(p): return p.read_text(encoding='utf-8')
def write(p,s): p.write_text(s,encoding='utf-8')
def replace_once(p,old,new):
 s=read(p); c=s.count(old)
 if c!=1: raise RuntimeError(f'{p} count {c} for {old[:80]!r}')
 write(p,s.replace(old,new,1))
def method_bounds(text, signature):
 start=text.index(signature); brace=text.index('{',start); depth=0
 for i in range(brace,len(text)):
  if text[i]=='{': depth+=1
  elif text[i]=='}':
   depth-=1
   if depth==0: return start,i+1
 raise RuntimeError('unclosed')

# Version metadata.
build = ROOT / 'app/build.gradle'
text = read(build)
text, c1 = re.subn(r'versionCode\s+14', 'versionCode 15', text, count=1)
text, c2 = re.subn(r"versionName\s+'4\.0\.8'", "versionName '4.0.9'", text, count=1)
if c1 != 1 or c2 != 1:
    raise RuntimeError(f'build version patch failed: {c1}, {c2}')
write(build, text)

version = JAVA / 'BuildVersion.java'
text = read(version)
text, c1 = re.subn(r'VERSION_CODE\s*=\s*14', 'VERSION_CODE = 15', text, count=1)
text, c2 = re.subn(r'VERSION_NAME\s*=\s*"4\.0\.8"', 'VERSION_NAME = "4.0.9"', text, count=1)
if c1 != 1 or c2 != 1:
    raise RuntimeError(f'BuildVersion patch failed: {c1}, {c2}')
write(version, text)

# Replace the project-heavy home screen with one clear action.
p=JAVA/'HomeActivity.java'; s=read(p)
a,b=method_bounds(s,'    private View buildContent()')
new='''    private View buildContent() {
        UiPalette p = UiPalette.of(this);
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(p.surface);
        LinearLayout page = vertical();
        page.setPadding(dp(20), dp(28), dp(20), dp(36));
        scroll.addView(page, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        page.addView(text("净画 · 去字幕", 30, true, p.primaryText));
        TextView subtitle = text(
                "只做一件事：选择视频，框住字幕，导出完整画面。",
                15, false, p.secondaryText);
        subtitle.setPadding(0, dp(8), 0, dp(24));
        page.addView(subtitle);

        Button start = primary("选择视频开始去字幕");
        start.setTextSize(18f);
        start.setOnClickListener(v -> startEditor(null, true, 0));
        page.addView(start, matchWrap(62));

        TextView steps = text(
                "1. 选择视频  ·  2. 拖动黄色框覆盖字幕  ·  3. 预览并导出\n\n导出始终保留完整视频画面，选框只决定去字幕的位置。",
                14, false, p.secondaryText);
        steps.setPadding(dp(4), dp(22), dp(4), 0);
        page.addView(steps);
        return scroll;
    }'''
s=s[:a]+new+s[b:]
s=s.replace('净画 V4.0.7','净画 V4.0.9').replace('V4.0.7','V4.0.9').replace('V4.0.8','V4.0.9')
write(p,s)

p=JAVA/'MainActivity.java'; s=read(p)
s=s.replace('净画 V4.0.7','净画 V4.0.9').replace('净画 V4.0.8','净画 V4.0.9')

# Use Android's large-thumbnail photo/video picker instead of DocumentsUI.
ma,mb=method_bounds(s,'    private void openVideoPicker()')
new_picker='''    private void openVideoPicker() {
        if (exporting || cloudRepairing || tracking || subtitleScanning) {
            toast("请先完成或取消当前导出");
            return;
        }
        Intent intent;
        if (Build.VERSION.SDK_INT >= 33) {
            intent = new Intent(MediaStore.ACTION_PICK_IMAGES);
            intent.setType("video/*");
        } else {
            intent = new Intent(Intent.ACTION_PICK, MediaStore.Video.Media.EXTERNAL_CONTENT_URI);
            intent.setType("video/*");
        }
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        startActivityForResult(intent, REQUEST_PICK_VIDEOS);
    }'''
s=s[:ma]+new_picker+s[mb:]

# Install playback callbacks before the URI, start immediately, and force the
# only supported subtitle-removal mode.
ma,mb=method_bounds(s,'    private void loadPreviewVideo(Uri uri)')
new_load='''    private void loadPreviewVideo(Uri uri) {
        previewUri = uri;
        shareButton.setEnabled(lastOutputUri != null);
        openLastButton.setEnabled(lastOutputUri != null);
        hideProcessedPreview();
        mainHandler.removeCallbacks(timelinePoller);
        videoView.stopPlayback();
        videoView.setOnPreparedListener(player -> {
            player.setLooping(true);
            player.setVolume(1f, 1f);
            videoView.seekTo(1);
            videoView.start();
            mainHandler.post(timelinePoller);
            statusText.setText("视频正在播放。拖动黄色框完整覆盖字幕即可。");
        });
        videoView.setOnErrorListener((player, what, extra) -> {
            statusText.setText("视频预览失败，请重新选择该视频");
            toast("视频无法播放，请换一个常见 MP4 视频重试");
            return true;
        });
        videoView.setVideoURI(uri);
        videoView.requestFocus();

        previewMetadata = VideoMetadata.read(this, uri);
        previewStage.setAspectRatio((float) previewMetadata.width / previewMetadata.height);
        overlayView.setDurationMs(previewMetadata.durationMs);
        overlayView.clearRegions();
        overlayView.addRegion();
        overlayView.replaceActiveWithFixedRect(new RectF(0.04f, 0.72f, 0.96f, 0.95f));
        overlayView.setActiveMode(RegionEffect.MODE_REPAIR_HQ);
        overlayView.setActiveStrength(1.0f);
        modeSpinner.setSelection(RegionEffect.MODE_REPAIR_HQ);
        strengthSeek.setProgress(100);
        cropCheck.setChecked(false);
        temporalRepairCheck.setChecked(true);
        autoFallbackCheck.setChecked(false);
        updateTimelineUi(0L, false);
        videoInfo.setText(
                previewMetadata.width + " × " + previewMetadata.height
                        + "  ·  " + formatDuration(previewMetadata.durationMs));
        projectStatusText.setText("当前视频已载入");
        pendingTemplateIndex = -1;
        updateExportEstimate();
        updateCloudUi();
    }'''
s=s[:ma]+new_load+s[mb:]

# Keep all compatibility fields initialized, but display only the four things a
# normal user needs: choose, watch/mark, preview, export.
ba,bb=method_bounds(s,'    private View buildContent()')
method=s[ba:bb]
needle='''        return scroll;\n    }'''
if method.count(needle)!=1: raise RuntimeError('return scroll anchor')
simple='''        page.removeAllViews();
        page.setPadding(dp(18), dp(18), dp(18), dp(34));
        page.addView(text("净画 · 去字幕", 28, true, UiPalette.of(this).primaryText));
        TextView simpleIntro = text(
                "选择视频后直接播放。拖动黄色框覆盖字幕，预览后导出完整视频。",
                14, false, UiPalette.of(this).secondaryText);
        simpleIntro.setPadding(0, dp(5), 0, dp(16));
        page.addView(simpleIntro);

        importButton.setText("1. 选择视频");
        importButton.setTextSize(17f);
        page.addView(importButton, matchWrap(58));
        page.addView(videoInfo);

        LinearLayout.LayoutParams simplePreviewParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
        simplePreviewParams.gravity = Gravity.CENTER_HORIZONTAL;
        page.addView(previewStage, simplePreviewParams);

        TextView boxHint = text(
                "黄色框就是去字幕区域：拖动框移动，拖四角调整大小。选框不会裁剪视频。",
                13, false, UiPalette.of(this).secondaryText);
        boxHint.setPadding(dp(2), dp(10), dp(2), dp(4));
        page.addView(boxHint);
        page.addView(playbackRow);
        page.addView(timelineLabel);
        page.addView(timelineSeek, matchWrap(42));

        previewButton.setText("2. 预览去字幕效果");
        previewButton.setTextSize(16f);
        page.addView(previewButton, matchWrap(52));

        exportButton.setText("3. 导出完整去字幕视频");
        exportButton.setTextSize(17f);
        LinearLayout.LayoutParams simpleExportParams = matchWrap(58);
        simpleExportParams.topMargin = dp(12);
        page.addView(exportButton, simpleExportParams);
        page.addView(progressBar);
        page.addView(statusText);
        page.addView(cancelButton);

        TextView simpleFooter = text(
                "已固定使用彻底去字模式和 100% 强度。导出尺寸、方向和画面范围与原视频保持一致。",
                12, false, UiPalette.of(this).secondaryText);
        simpleFooter.setPadding(dp(2), dp(14), dp(2), 0);
        page.addView(simpleFooter);

        cropCheck.setChecked(false);
        modeSpinner.setSelection(RegionEffect.MODE_REPAIR_HQ);
        strengthSeek.setProgress(100);
        temporalRepairCheck.setChecked(true);
        autoFallbackCheck.setChecked(false);
        return scroll;
    }'''
method=method.replace(needle,simple,1)
s=s[:ba]+method+s[bb:]

# Disable crop both in processed preview and export. Selection only marks the
# repair area and can never change output dimensions.
old='''        if (!cropCheck.isChecked() && overlayView.getRegionCount() == 0) {
            toast("请至少添加一个处理区域");
            return;
        }
        RectF cropRect = cropCheck.isChecked() ? overlayView.getActiveWorkingRect() : null;
        if (cropCheck.isChecked() && cropRect == null) {
            toast("裁剪前请先选择一个区域");
            return;
        }'''
new='''        if (overlayView.getRegionCount() == 0) {
            toast("请先用黄色框覆盖字幕");
            return;
        }
        cropCheck.setChecked(false);
        RectF cropRect = null;'''
if s.count(old)!=2: raise RuntimeError(f'crop validation count={s.count(old)}')
s=s.replace(old,new,2)
old='''        exportCropEnabled = cropCheck.isChecked();
        exportCropRect = cropRect == null ? null : new RectF(cropRect);'''
new='''        exportCropEnabled = false;
        exportCropRect = null;'''
if s.count(old)!=1: raise RuntimeError('crop snapshot')
s=s.replace(old,new,1)
s=s.replace('cropCheck.setChecked(state.cropEnabled);','cropCheck.setChecked(false);')
write(p,s)

assert "versionCode 15" in read(build)
assert "versionName '4.0.9'" in read(build)
assert "VERSION_CODE = 15" in read(version)
assert 'VERSION_NAME = "4.0.9"' in read(version)
assert 'MediaStore.ACTION_PICK_IMAGES' in read(JAVA / 'MainActivity.java')
assert 'videoView.start();' in read(JAVA / 'MainActivity.java')
assert 'page.removeAllViews();' in read(JAVA / 'MainActivity.java')
assert 'exportCropEnabled = false;' in read(JAVA / 'MainActivity.java')
print('V409_SIMPLE_SUBTITLE_PATCH_APPLIED')
