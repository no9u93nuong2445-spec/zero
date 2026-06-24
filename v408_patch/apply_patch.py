#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path("jhmin")
JAVA = ROOT / "app/src/main/java/com/bianzhifeng/jinghua"
RAW = ROOT / "app/src/main/res/raw"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8")


def replace_exact(path: Path, old: str, new: str, count: int = 1) -> None:
    text = read(path)
    actual = text.count(old)
    if actual != count:
        raise RuntimeError(
            f"{path}: expected {count} occurrences, got {actual}: {old[:120]!r}"
        )
    write(path, text.replace(old, new, count))


# Derive V4.0.8 from the already verified V4.0.7 shader. This avoids moving a
# large compressed shader blob through GitHub's text API, which previously
# corrupted the payload before the Android build started.
shader_path = RAW / "fragment_region_es2.glsl"
shader = read(shader_path)
old_marker = (
    "// JingHua V4.0.7 recovery shader: confidence-gated chroma-safe repair."
)
if shader.count(old_marker) != 1:
    raise RuntimeError("Expected verified V4.0.7 shader marker")
shader = shader.replace(
    old_marker,
    "// JingHua V4.0.8 complete-erasure shader: padded full-region reconstruction.",
    1,
)
inside_anchor = """bool insidePose(vec2 p, vec4 pose, float angle) {
  vec2 local = toLocal(p, pose, angle);
  return abs(local.x) <= pose.z && abs(local.y) <= pose.w;
}
"""
expand_helper = inside_anchor + """
vec4 expandRepairPose(vec4 pose) {
  // Cover outline, shadow and anti-alias pixels even when detection is tight.
  vec2 pad = max(uTexelSize * vec2(7.0, 7.0), pose.zw * vec2(0.035, 0.20));
  pose.zw = min(pose.zw + pad, vec2(0.495));
  pose.xy = clamp(pose.xy, pose.zw, vec2(1.0) - pose.zw);
  return pose;
}
"""
if shader.count(inside_anchor) != 1:
    raise RuntimeError("insidePose anchor missing")
shader = shader.replace(inside_anchor, expand_helper, 1)

hq_start = shader.find("  if (highQuality) {", shader.find("vec4 repairRegion("))
hq_end = shader.find("\n  float edgeDifference = 0.0;", hq_start)
if hq_start < 0 or hq_end < 0:
    raise RuntimeError("high-quality repair block markers missing")
new_hq = """  if (highQuality) {
    float continuity = 0.0;
    float stability = 0.0;
    vec4 localBackground = localDirectionalRepair(
        p, pose, angle, strength, original, continuity, stability);

    // History is accepted only when it agrees with reconstructed background
    // and is visibly different from the current subtitle raster.
    if (uHistoryValid == 1 && uTemporalRepairEnabled == 1
        && uFrameGap > 0.0 && uFrameGap <= 0.25) {
      vec4 historySame = texture2D(uHistorySampler, clampUv(p));
      vec2 currentLocal = toLocal(p, pose, angle);
      vec2 normalizedLocal = currentLocal / max(pose.zw, vec2(0.0001));
      vec2 previousLocal = normalizedLocal * previousPose.zw;
      vec2 warpedUv = clampUv(fromLocal(previousLocal, previousPose, previousAngle));
      vec4 historyWarped = texture2D(uHistorySampler, warpedUv);
      float sameDistance = distance(historySame.rgb, localBackground.rgb) * 0.57735;
      float warpedDistance = distance(historyWarped.rgb, localBackground.rgb) * 0.57735;
      vec4 history = sameDistance <= warpedDistance ? historySame : historyWarped;
      float historyDistance = min(sameDistance, warpedDistance);
      float agreement = 1.0 - smoothstep(0.06, 0.24, historyDistance);
      float differsFromGlyph = smoothstep(
          0.06, 0.18, distance(history.rgb, original.rgb) * 0.57735);
      localBackground = mix(
          localBackground, history, agreement * differsFromGlyph * 0.14);
    }

    vec2 local = toLocal(p, pose, angle);
    float depthX = (pose.z - abs(local.x)) / max(0.0001, pose.z);
    float depthY = (pose.w - abs(local.y)) / max(0.0001, pose.w);
    // Only feather a narrow outer rim. Every interior pixel is reconstructed,
    // so white/black outlines and coloured subtitle raster cannot survive.
    float feather = smoothstep(
        0.0, 0.038, clamp(min(depthX, depthY), 0.0, 1.0));
    vec4 repairColor = vec4(clamp(localBackground.rgb, 0.0, 1.0), original.a);
    return mix(original, repairColor, feather);
  }
"""
shader = shader[:hq_start] + new_hq + shader[hq_end:]
apply_old = """  if (uTimeSeconds < timeRange.x || uTimeSeconds > timeRange.y) return color;
  if (!insidePose(p, pose, angle)) return color;
  return processPose(p, pose, angle, previousPose, previousAngle, mode, strength, color);"""
apply_new = """  if (uTimeSeconds < timeRange.x || uTimeSeconds > timeRange.y) return color;
  vec4 activePose = mode == 5 ? expandRepairPose(pose) : pose;
  vec4 activePreviousPose = mode == 5 ? expandRepairPose(previousPose) : previousPose;
  if (!insidePose(p, activePose, angle)) return color;
  return processPose(
      p, activePose, angle, activePreviousPose, previousAngle, mode, strength, color);"""
if shader.count(apply_old) != 1:
    raise RuntimeError("applyTrack anchor missing")
shader = shader.replace(apply_old, apply_new, 1)
write(shader_path, shader)

# Version metadata.
build = ROOT / "app/build.gradle"
text = read(build)
text, code_count = re.subn(r"versionCode\s+13", "versionCode 14", text, count=1)
text, name_count = re.subn(
    r"versionName\s+'4\.0\.7'", "versionName '4.0.8'", text, count=1
)
if code_count != 1 or name_count != 1:
    raise RuntimeError(
        f"build version replacement failed: code={code_count}, name={name_count}"
    )
write(build, text)

version = JAVA / "BuildVersion.java"
text = read(version)
text, code_count = re.subn(
    r"VERSION_CODE\s*=\s*13", "VERSION_CODE = 14", text, count=1
)
text, name_count = re.subn(
    r'VERSION_NAME\s*=\s*"4\.0\.7"',
    'VERSION_NAME = "4.0.8"',
    text,
    count=1,
)
if code_count != 1 or name_count != 1:
    raise RuntimeError(
        f"BuildVersion replacement failed: code={code_count}, name={name_count}"
    )
write(version, text)

for path in [
    JAVA / "HomeActivity.java",
    JAVA / "MainActivity.java",
    JAVA / "ExportForegroundService.java",
]:
    write(path, read(path).replace("4.0.7", "4.0.8"))
strings = ROOT / "app/src/main/res/values/strings.xml"
if strings.exists():
    write(strings, read(strings).replace("4.0.7", "4.0.8"))

# Subtitle workflows always use full-strength HQ complete removal.
main = JAVA / "MainActivity.java"
text = read(main)
if text.count("本地高质量修复") < 3:
    raise RuntimeError("MainActivity HQ labels missing")
text = text.replace("本地高质量修复", "彻底去字（推荐）")
text = text.replace(
    "选择“本地快速修复”或“彻底去字（推荐）”后，可先分析当前帧。高质量模式采样更多边缘并提高连续帧一致性，但导出更慢。",
    "字幕建议使用“彻底去字（推荐）”。它会完整重建选区并自动覆盖描边、阴影与抗锯齿；请让选框四周保留少量可参考背景。",
)
text = text.replace(
    "已选择彻底去字（推荐）：更多边缘采样、更强时序一致性，适合双行字幕和较大区域。",
    "已选择彻底去字：选区内部不保留原字幕像素，并自动扩展安全边。",
)
scan_old = """            int repairMode = zone == SubtitleZone.BOTTOM
                    ? RegionEffect.MODE_REPAIR_HQ
                    : RegionEffect.MODE_REPAIR_FAST;
            RegionTrack track = RegionTrack.fixed(
                    firstRect,
                    durationMs,
                    repairMode,
                    zone == SubtitleZone.BOTTOM ? 0.68f : 0.58f);"""
scan_new = """            int repairMode = RegionEffect.MODE_REPAIR_HQ;
            RegionTrack track = RegionTrack.fixed(
                    firstRect,
                    durationMs,
                    repairMode,
                    1.0f);"""
if text.count(scan_old) != 1:
    raise RuntimeError("subtitle scan mode block missing")
write(main, text.replace(scan_old, scan_new, 1))

# Built-in bottom-subtitle templates get a safety rim and full-strength HQ mode.
templates = JAVA / "TemplateStore.java"
replace_exact(
    templates,
    """                tracks.add(RegionTrack.fixed(new RectF(0.05f, 0.79f, 0.95f, 0.91f), duration,
                        RegionEffect.MODE_REPAIR_FAST, 0.58f));""",
    """                tracks.add(RegionTrack.fixed(new RectF(0.025f, 0.755f, 0.975f, 0.945f), duration,
                        RegionEffect.MODE_REPAIR_HQ, 1.0f));""",
)
replace_exact(
    templates,
    """                tracks.add(RegionTrack.fixed(new RectF(0.04f, 0.69f, 0.96f, 0.93f), duration,
                        RegionEffect.MODE_REPAIR_HQ, 0.68f));""",
    """                tracks.add(RegionTrack.fixed(new RectF(0.02f, 0.64f, 0.98f, 0.955f), duration,
                        RegionEffect.MODE_REPAIR_HQ, 1.0f));""",
)

# Preview mirrors export: padded pose, no fallback re-mixing in HQ, narrow rim.
preview = JAVA / "PreviewRenderer.java"
replace_exact(
    preview,
    """            applyRepairEffect(
                    output,
                    pose,
                    strength,""",
    """            applyRepairEffect(
                    output,
                    expandRepairPose(output, pose),
                    strength,""",
)
preview_text = read(preview)
helper_anchor = "    private static void applyRepairEffect(\n"
helper = """    private static RegionPose expandRepairPose(Bitmap output, RegionPose pose) {
        RectF rect = new RectF(pose.rect);
        float padX = Math.max(7f / Math.max(1, output.getWidth()), rect.width() * 0.035f);
        float padY = Math.max(7f / Math.max(1, output.getHeight()), rect.height() * 0.20f);
        rect.left = Math.max(0f, rect.left - padX);
        rect.right = Math.min(1f, rect.right + padX);
        rect.top = Math.max(0f, rect.top - padY);
        rect.bottom = Math.min(1f, rect.bottom + padY);
        return new RegionPose(rect, pose.rotationDeg);
    }

"""
if helper not in preview_text:
    if preview_text.count(helper_anchor) != 1:
        raise RuntimeError("PreviewRenderer helper anchor missing")
    preview_text = preview_text.replace(helper_anchor, helper + helper_anchor, 1)
preview_text = preview_text.replace(
    "Math.min(width, height) * (highQuality ? 0.15f : 0.10f)",
    "Math.min(width, height) * (highQuality ? 0.04f : 0.10f)",
    1,
)
preview_text = preview_text.replace(
    "if (fallback > 0f) {", "if (fallback > 0f && !highQuality) {", 1
)
feather_old = """                float feather = RepairBlendPolicy.feather(
                        distance / (float) Math.max(1, Math.min(width, height)),
                        highQuality);
                if (distance >= featherPx) feather = 1f;"""
feather_new = """                float feather;
                if (highQuality) {
                    float t = Math.max(0f, Math.min(1f, distance / (float) featherPx));
                    feather = t * t * (3f - 2f * t);
                } else {
                    feather = RepairBlendPolicy.feather(
                            distance / (float) Math.max(1, Math.min(width, height)), false);
                }
                if (distance >= featherPx) feather = 1f;"""
if preview_text.count(feather_old) != 1:
    raise RuntimeError("PreviewRenderer feather block missing")
write(preview, preview_text.replace(feather_old, feather_new, 1))

policy = JAVA / "RepairBlendPolicy.java"
replace_exact(
    policy,
    "float width = highQuality ? 0.15f : 0.10f;",
    "float width = highQuality ? 0.04f : 0.10f;",
)

functional = JAVA / "FunctionalExportActivity.java"
if functional.exists():
    text = read(functional)
    if "strength = 0.82f;" not in text:
        raise RuntimeError("FunctionalExportActivity HQ strength missing")
    write(functional, text.replace("strength = 0.82f;", "strength = 1.0f;", 1))

# Hard invariants prevent a partially-applied release.
assert "versionCode 14" in read(build)
assert "versionName '4.0.8'" in read(build)
assert "VERSION_CODE = 14" in read(version)
assert 'VERSION_NAME = "4.0.8"' in read(version)
assert "V4.0.8 complete-erasure shader" in read(shader_path)
assert "vec4 expandRepairPose" in read(shader_path)
repair_tail = read(shader_path)[read(shader_path).find("vec4 repairRegion(") :]
assert "subtitleInkMask(p, localBackground, strength)" not in repair_tail
assert "彻底去字（推荐）" in read(main)
assert "RegionEffect.MODE_REPAIR_HQ, 1.0f" in read(templates)
assert "expandRepairPose(output, pose)" in read(preview)
print("V408_PATCH_APPLIED")
