#!/usr/bin/env python3
from __future__ import annotations
import base64, gzip, re
from pathlib import Path

ROOT = Path('jhmin')
JAVA = ROOT / 'app/src/main/java/com/bianzhifeng/jinghua'
RAW = ROOT / 'app/src/main/res/raw'


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected one occurrence, got {count}: {old[:100]!r}')
    path.write_text(text.replace(old, new, 1), encoding='utf-8')


def replace_all_required(path: Path, old: str, new: str, minimum: int = 1) -> int:
    text = path.read_text(encoding='utf-8')
    count = text.count(old)
    if count < minimum:
        raise RuntimeError(f'{path}: expected >= {minimum}, got {count}: {old!r}')
    path.write_text(text.replace(old, new), encoding='utf-8')
    return count

# Install the GLES2 complete-erasure shader.
payload = Path('v408_patch/fragment_region_v408.glsl.gz.b64').read_text(encoding='ascii').strip()
shader = gzip.decompress(base64.b64decode(payload))
shader_path = RAW / 'fragment_region_es2.glsl'
shader_path.write_bytes(shader)
shader_text = shader.decode('utf-8')
assert 'V4.0.8 complete-erasure shader' in shader_text
assert 'vec4 expandRepairPose' in shader_text
assert 'confidence-gated chroma-safe repair' not in shader_text

# Version bump.
build = ROOT / 'app/build.gradle'
text = build.read_text(encoding='utf-8')
text = re.sub(r'versionCode\s+1[013]', 'versionCode 14', text)
text = re.sub(r"versionName\s+'4\.0\.[347]'", "versionName '4.0.8'", text)
build.write_text(text, encoding='utf-8')

version = JAVA / 'BuildVersion.java'
text = version.read_text(encoding='utf-8')
text = re.sub(r'VERSION_CODE\s*=\s*1[013]', 'VERSION_CODE = 14', text)
text = re.sub(r'VERSION_NAME\s*=\s*"4\.0\.[347]"', 'VERSION_NAME = "4.0.8"', text)
version.write_text(text, encoding='utf-8')

for path in [JAVA / 'HomeActivity.java', JAVA / 'MainActivity.java', JAVA / 'ExportForegroundService.java', ROOT / 'app/src/main/res/values/strings.xml']:
    if path.exists():
        s = path.read_text(encoding='utf-8')
        s = s.replace('4.0.3', '4.0.8').replace('4.0.4', '4.0.8').replace('4.0.7', '4.0.8')
        path.write_text(s, encoding='utf-8')

# Make complete removal explicit and the default for subtitle workflows.
main = JAVA / 'MainActivity.java'
replace_all_required(main, '本地高质量修复', '彻底去字（推荐）', minimum=3)
replace_once(
    main,
    '选择“本地快速修复”或“彻底去字（推荐）”后，可先分析当前帧。高质量模式采样更多边缘并提高连续帧一致性，但导出更慢。',
    '字幕优先选择“彻底去字（推荐）”。该模式会对选区内部完整重建，不保留原字幕像素；请让选框四周留出少量背景。')
replace_once(
    main,
    '? "已选择彻底去字（推荐）：更多边缘采样、更强时序一致性，适合双行字幕和较大区域。"',
    '? "已选择彻底去字：选区内部强制完整重建，并自动扩展安全边，适合单行、双行、描边和彩色字幕。"')
replace_once(
    main,
    '''            int repairMode = zone == SubtitleZone.BOTTOM
                    ? RegionEffect.MODE_REPAIR_HQ
                    : RegionEffect.MODE_REPAIR_FAST;
            RegionTrack track = RegionTrack.fixed(
                    firstRect,
                    durationMs,
                    repairMode,
                    zone == SubtitleZone.BOTTOM ? 0.68f : 0.58f);''',
    '''            int repairMode = RegionEffect.MODE_REPAIR_HQ;
            RegionTrack track = RegionTrack.fixed(
                    firstRect,
                    durationMs,
                    repairMode,
                    1.0f);''')

# Built-in subtitle templates: larger safe box + complete erase + full strength.
templates = JAVA / 'TemplateStore.java'
replace_once(
    templates,
    '''                tracks.add(RegionTrack.fixed(new RectF(0.05f, 0.79f, 0.95f, 0.91f), duration,
                        RegionEffect.MODE_REPAIR_FAST, 0.58f));''',
    '''                tracks.add(RegionTrack.fixed(new RectF(0.025f, 0.755f, 0.975f, 0.945f), duration,
                        RegionEffect.MODE_REPAIR_HQ, 1.0f));''')
replace_once(
    templates,
    '''                tracks.add(RegionTrack.fixed(new RectF(0.04f, 0.69f, 0.96f, 0.93f), duration,
                        RegionEffect.MODE_REPAIR_HQ, 0.68f));''',
    '''                tracks.add(RegionTrack.fixed(new RectF(0.02f, 0.64f, 0.98f, 0.955f), duration,
                        RegionEffect.MODE_REPAIR_HQ, 1.0f));''')

# Preview must mirror export: expand the repair pose, never blend original pixels
# back into HQ repair, and keep feathering inside the added safety rim.
preview = JAVA / 'PreviewRenderer.java'
replace_once(
    preview,
    '''            applyRepairEffect(
                    output,
                    pose,
                    strength,
                    mode == RegionEffect.MODE_REPAIR_HQ,
                    autoFallbackEnabled,
                    paint);''',
    '''            applyRepairEffect(
                    output,
                    expandRepairPose(output, pose),
                    strength,
                    mode == RegionEffect.MODE_REPAIR_HQ,
                    autoFallbackEnabled,
                    paint);''')
anchor = '''    private static void applyRepairEffect(
            Bitmap output,'''
helper = '''    private static RegionPose expandRepairPose(Bitmap output, RegionPose pose) {
        RectF rect = new RectF(pose.rect);
        float padX = Math.max(6f / Math.max(1, output.getWidth()), rect.width() * 0.03f);
        float padY = Math.max(6f / Math.max(1, output.getHeight()), rect.height() * 0.18f);
        rect.left = Math.max(0f, rect.left - padX);
        rect.right = Math.min(1f, rect.right + padX);
        rect.top = Math.max(0f, rect.top - padY);
        rect.bottom = Math.min(1f, rect.bottom + padY);
        return new RegionPose(rect, pose.rotationDeg);
    }

'''
text = preview.read_text(encoding='utf-8')
if helper not in text:
    if text.count(anchor) != 1:
        raise RuntimeError('PreviewRenderer applyRepairEffect anchor missing')
    text = text.replace(anchor, helper + anchor, 1)
preview.write_text(text, encoding='utf-8')
replace_once(
    preview,
    '''        int featherPx = Math.max(2, Math.round(
                Math.min(width, height) * (highQuality ? 0.15f : 0.10f)));''',
    '''        int featherPx = Math.max(1, Math.round(
                Math.min(width, height) * (highQuality ? 0.04f : 0.10f)));''')
replace_once(preview, '                if (fallback > 0f) {', '                if (fallback > 0f && !highQuality) {')
replace_once(
    preview,
    '''                float feather = RepairBlendPolicy.feather(
                        distance / (float) Math.max(1, Math.min(width, height)),
                        highQuality);
                if (distance >= featherPx) feather = 1f;''',
    '''                float feather;
                if (highQuality) {
                    float t = Math.max(0f, Math.min(1f, distance / (float) featherPx));
                    feather = t * t * (3f - 2f * t);
                } else {
                    feather = RepairBlendPolicy.feather(
                            distance / (float) Math.max(1, Math.min(width, height)), false);
                }
                if (distance >= featherPx) feather = 1f;''')

policy = JAVA / 'RepairBlendPolicy.java'
replace_once(policy, 'float width = highQuality ? 0.15f : 0.10f;', 'float width = highQuality ? 0.04f : 0.10f;')

# CI functional harness uses the same production mode at maximum strength.
functional = JAVA / 'FunctionalExportActivity.java'
if functional.exists():
    text = functional.read_text(encoding='utf-8')
    text = text.replace('strength = 0.72f;', 'strength = 1.0f;')
    functional.write_text(text, encoding='utf-8')

# Hard assertions prevent a partially-applied release.
assert 'versionCode 14' in build.read_text(encoding='utf-8')
assert "versionName '4.0.8'" in build.read_text(encoding='utf-8')
assert 'VERSION_CODE = 14' in version.read_text(encoding='utf-8')
assert 'VERSION_NAME = "4.0.8"' in version.read_text(encoding='utf-8')
assert '彻底去字（推荐）' in main.read_text(encoding='utf-8')
assert 'RegionEffect.MODE_REPAIR_HQ, 1.0f' in templates.read_text(encoding='utf-8')
assert 'expandRepairPose(output, pose)' in preview.read_text(encoding='utf-8')
print('V408_PATCH_APPLIED')
