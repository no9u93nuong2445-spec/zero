#!/usr/bin/env python3
from pathlib import Path

shader = Path("jhmin/app/src/main/res/raw/fragment_region_es2.glsl")
text = shader.read_text(encoding="utf-8")
start = text.index("float subtitleInkAt(")
end = text.index("vec4 repairRegion(", start)
replacement = r'''float subtitleInkEvidenceAt(
    vec2 p,
    vec4 estimatedBackground,
    float strength) {
  vec4 original = texture2D(uTexSampler, clampUv(p));
  vec2 offset = uTexelSize * (4.0 + strength * 2.0);
  vec4 localAverage = (
      texture2D(uTexSampler, clampUv(p + vec2(offset.x, 0.0)))
      + texture2D(uTexSampler, clampUv(p - vec2(offset.x, 0.0)))
      + texture2D(uTexSampler, clampUv(p + vec2(0.0, offset.y)))
      + texture2D(uTexSampler, clampUv(p - vec2(0.0, offset.y)))) * 0.25;

  float luma = luminanceOf(original.rgb);
  float brightPrior = smoothstep(0.67, 0.91, luma);
  float darkPrior = 1.0 - smoothstep(0.07, 0.31, luma);
  float extremePrior = max(brightPrior, darkPrior);
  float localContrast = max(
      abs(luma - luminanceOf(localAverage.rgb)),
      distance(original.rgb, localAverage.rgb) * 0.57735);
  float backgroundDifference = distance(original.rgb, estimatedBackground.rgb) * 0.57735;

  float monochromeGlyph = smoothstep(0.24, 0.34, localContrast)
      * smoothstep(0.20, 0.28, backgroundDifference)
      * smoothstep(0.35, 0.65, extremePrior);
  float coloredGlyph = smoothstep(0.34, 0.50, localContrast)
      * smoothstep(0.28, 0.42, backgroundDifference) * 0.55;
  return clamp(max(monochromeGlyph, coloredGlyph), 0.0, 1.0);
}

float subtitleInkMask(
    vec2 p,
    vec4 estimatedBackground,
    float strength) {
  vec2 d = uTexelSize * (2.8 + strength * 1.6);
  float maskValue = subtitleInkEvidenceAt(p, estimatedBackground, strength);
  maskValue = max(maskValue, subtitleInkEvidenceAt(p + vec2(d.x, 0.0), estimatedBackground, strength));
  maskValue = max(maskValue, subtitleInkEvidenceAt(p - vec2(d.x, 0.0), estimatedBackground, strength));
  maskValue = max(maskValue, subtitleInkEvidenceAt(p + vec2(0.0, d.y), estimatedBackground, strength));
  maskValue = max(maskValue, subtitleInkEvidenceAt(p - vec2(0.0, d.y), estimatedBackground, strength));
  return smoothstep(0.14, 0.58, maskValue);
}

'''
text = text[:start] + replacement + text[end:]
old_call = "float inkMask = subtitleInkMask(p, pose, angle, strength);"
new_call = "float inkMask = subtitleInkMask(p, localBackground, strength);"
if old_call not in text:
    raise SystemExit("expected subtitleInkMask call not found")
text = text.replace(old_call, new_call, 1)
shader.write_text(text, encoding="utf-8")
print("Applied V4.0.4d strict subtitle mask and reduced shader sampling")
