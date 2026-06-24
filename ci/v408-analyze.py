#!/usr/bin/env python3
from __future__ import annotations

import json
import math
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path('v408-results')
ANDROID = ROOT / 'android'
EVIDENCE = ROOT / 'evidence'
EVIDENCE.mkdir(parents=True, exist_ok=True)
STYLES = ['white-outline', 'yellow-shadow', 'color-outline', 'double-line']
TIMES = [0.55, 1.25, 2.05, 2.85, 3.65]


def probe(path: Path) -> dict:
    if not path.exists():
        return {'missing': True, 'path': str(path)}
    return json.loads(subprocess.check_output([
        'ffprobe', '-v', 'error',
        '-show_entries', 'stream=codec_name,codec_type,width,height,r_frame_rate:format=duration,size',
        '-of', 'json', str(path),
    ], text=True))


def stream_summary(path: Path) -> dict:
    p = probe(path)
    if p.get('missing'):
        return p
    streams = p.get('streams', [])
    video = next((s for s in streams if s.get('codec_type') == 'video'), None)
    audio = next((s for s in streams if s.get('codec_type') == 'audio'), None)
    return {
        'path': str(path),
        'has_video': video is not None,
        'has_audio': audio is not None,
        'video_codec': None if video is None else video.get('codec_name'),
        'audio_codec': None if audio is None else audio.get('codec_name'),
        'width': None if video is None else video.get('width'),
        'height': None if video is None else video.get('height'),
        'duration_s': float(p.get('format', {}).get('duration', 0) or 0),
        'size_bytes': int(p.get('format', {}).get('size', 0) or 0),
    }


def extract(path: Path, t: float, label: str) -> np.ndarray:
    out = EVIDENCE / f'{label}-{t:.2f}.png'
    subprocess.run([
        'ffmpeg', '-y', '-v', 'error', '-ss', str(t), '-i', str(path),
        '-frames:v', '1', '-vf', 'scale=360:640:flags=bilinear', str(out),
    ], check=True)
    return np.asarray(Image.open(out).convert('RGB'), dtype=np.float32)


def luma(array: np.ndarray) -> np.ndarray:
    return 0.2126 * array[..., 0] + 0.7152 * array[..., 1] + 0.0722 * array[..., 2]


def corr(a: np.ndarray, b: np.ndarray) -> float:
    a = a.astype(np.float64).ravel()
    b = b.astype(np.float64).ravel()
    a -= a.mean()
    b -= b.mean()
    denom = math.sqrt(float(np.dot(a, a) * np.dot(b, b)))
    if denom < 1e-9:
        return 0.0
    return float(np.dot(a, b) / denom)


def make_montage(style: str, clean: np.ndarray, baseline: np.ndarray, repaired: np.ndarray) -> None:
    tiles = []
    for title, data in [('Clean reference', clean), ('Subtitle baseline', baseline), ('V4.0.8 repaired', repaired)]:
        img = Image.fromarray(np.clip(data, 0, 255).astype(np.uint8))
        canvas = Image.new('RGB', (360, 680), 'white')
        canvas.paste(img, (0, 40))
        draw = ImageDraw.Draw(canvas)
        draw.text((10, 12), title, fill='black')
        tiles.append(canvas)
    sheet = Image.new('RGB', (1080, 680), 'white')
    for index, tile in enumerate(tiles):
        sheet.paste(tile, (index * 360, 0))
    sheet.save(EVIDENCE / f'{style}-comparison.jpg', quality=92)


clean_path = ANDROID / 'output-clean.mp4'
clean_stream = stream_summary(clean_path)
clean_stream_pass = bool(
    not clean_stream.get('missing')
    and clean_stream.get('has_video')
    and clean_stream.get('has_audio')
    and clean_stream.get('video_codec') == 'h264'
    and clean_stream.get('audio_codec') == 'aac'
    and abs(clean_stream.get('duration_s', 0) - 4.2) < 0.20
)

style_reports = {}
all_style_pass = True
for style in STYLES:
    baseline_path = ANDROID / f'output-{style}-baseline.mp4'
    repair_path = ANDROID / f'output-{style}-repair.mp4'
    baseline_stream = stream_summary(baseline_path)
    repair_stream = stream_summary(repair_path)
    stream_pass = all([
        not baseline_stream.get('missing'),
        not repair_stream.get('missing'),
        baseline_stream.get('has_video'), baseline_stream.get('has_audio'),
        repair_stream.get('has_video'), repair_stream.get('has_audio'),
        baseline_stream.get('video_codec') == 'h264',
        repair_stream.get('video_codec') == 'h264',
        baseline_stream.get('audio_codec') == 'aac',
        repair_stream.get('audio_codec') == 'aac',
        abs(baseline_stream.get('duration_s', 0) - 4.2) < 0.20,
        abs(repair_stream.get('duration_s', 0) - 4.2) < 0.20,
    ])

    frames = []
    montage_frames = None
    if stream_pass and clean_stream_pass:
        for index, time_s in enumerate(TIMES):
            clean = extract(clean_path, time_s, f'{style}-clean')
            baseline = extract(baseline_path, time_s, f'{style}-baseline')
            repaired = extract(repair_path, time_s, f'{style}-repair')
            if index == 2:
                montage_frames = (clean, baseline, repaired)

            delta = np.max(np.abs(baseline - clean), axis=2)
            # Ignore small encoder differences and measure only real subtitle pixels.
            glyph_mask = delta > 24.0
            glyph_count = int(glyph_mask.sum())
            if glyph_count < 80:
                frames.append({'time_s': time_s, 'error': 'glyph_mask_too_small', 'glyph_pixels': glyph_count})
                continue

            baseline_dist = np.linalg.norm(baseline - clean, axis=2)
            repair_to_clean = np.linalg.norm(repaired - clean, axis=2)
            repair_to_baseline = np.linalg.norm(repaired - baseline, axis=2)
            base_mae = float(np.mean(np.abs(baseline[glyph_mask] - clean[glyph_mask])))
            repair_mae = float(np.mean(np.abs(repaired[glyph_mask] - clean[glyph_mask])))
            improvement = 1.0 - repair_mae / max(base_mae, 1e-6)

            strong_retention = float(np.mean(
                repair_to_baseline[glyph_mask]
                < 0.25 * np.maximum(baseline_dist[glyph_mask], 1e-6)
            ))
            residual_corr = abs(corr(
                luma(baseline - clean)[glyph_mask],
                luma(repaired - clean)[glyph_mask],
            ))
            projection = float(np.dot(
                (repaired[glyph_mask] - clean[glyph_mask]).ravel(),
                (baseline[glyph_mask] - clean[glyph_mask]).ravel(),
            ) / max(np.dot(
                (baseline[glyph_mask] - clean[glyph_mask]).ravel(),
                (baseline[glyph_mask] - clean[glyph_mask]).ravel(),
            ), 1e-9))

            h, w = glyph_mask.shape
            x1, x2 = int(w * 0.065), int(w * 0.935)
            y1, y2 = int(h * 0.742), int(h * 0.948)
            outside = np.ones((h, w), dtype=bool)
            outside[y1:y2, x1:x2] = False
            outside_mae = float(np.mean(np.abs(repaired[outside] - baseline[outside])))

            frame_pass = bool(
                improvement >= 0.80
                and strong_retention <= 0.03
                and residual_corr <= 0.18
                and projection <= 0.18
                and outside_mae <= 3.5
            )
            frames.append({
                'time_s': time_s,
                'glyph_pixels': glyph_count,
                'subtitle_improvement_ratio': improvement,
                'strong_original_pixel_retention_ratio': strong_retention,
                'glyph_residual_correlation': residual_corr,
                'original_glyph_projection': projection,
                'outside_repair_region_mae': outside_mae,
                'pass': frame_pass,
            })

    valid = [frame for frame in frames if 'pass' in frame]
    averages = {}
    if valid:
        for key in [
            'subtitle_improvement_ratio',
            'strong_original_pixel_retention_ratio',
            'glyph_residual_correlation',
            'original_glyph_projection',
            'outside_repair_region_mae',
        ]:
            averages[key] = float(np.mean([frame[key] for frame in valid]))
    style_pass = bool(stream_pass and len(valid) == len(TIMES) and all(frame['pass'] for frame in valid))
    all_style_pass = all_style_pass and style_pass
    style_reports[style] = {
        'pass': style_pass,
        'stream_pass': stream_pass,
        'baseline_stream': baseline_stream,
        'repair_stream': repair_stream,
        'averages': averages,
        'frames': frames,
    }
    if montage_frames is not None:
        make_montage(style, *montage_frames)

emulator_pass = (ANDROID / 'emulator-pass.txt').read_text().strip() == '1' if (ANDROID / 'emulator-pass.txt').exists() else False
apk_path = ROOT / 'apk/JingHua-V4.0.8-debug.apk'
full_pass = bool(apk_path.exists() and emulator_pass and clean_stream_pass and all_style_pass)
report = {
    'version': '4.0.8',
    'test_claim': 'No original subtitle raster may remain inside the selected and automatically padded repair region.',
    'full_pass': full_pass,
    'apk_built': apk_path.exists(),
    'android_emulator_export_pass': emulator_pass,
    'clean_reference_stream_pass': clean_stream_pass,
    'clean_reference_stream': clean_stream,
    'thresholds': {
        'subtitle_improvement_ratio_min': 0.80,
        'strong_original_pixel_retention_ratio_max': 0.03,
        'glyph_residual_correlation_max': 0.18,
        'original_glyph_projection_max': 0.18,
        'outside_repair_region_mae_max': 3.5,
    },
    'styles': style_reports,
    'limitation': 'The hidden background cannot be recovered exactly from a single frame. Complete removal means the original subtitle pixels are replaced; complex textures behind the text may be smoothed or reconstructed approximately.',
}
(ROOT / 'V408_COMPLETE_REMOVAL_REPORT.json').write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

lines = [
    '# 净画 V4.0.8 字幕彻底去除测试', '',
    f"- 最终判定：**{'通过' if full_pass else '失败'}**",
    f"- 完整源码 APK 构建：{'通过' if apk_path.exists() else '失败'}",
    f"- Android 真实 Media3 导出：{'通过' if emulator_pass else '失败'}",
    f"- 音频、时长、编码：{'通过' if clean_stream_pass else '失败'}", '',
    '| 字幕类型 | 判定 | 字幕区改善 | 原字幕像素强残留 | 字形残留相关性 | 选区外变化 |',
    '|---|---:|---:|---:|---:|---:|',
]
for style, data in style_reports.items():
    avg = data.get('averages', {})
    lines.append(
        f"| {style} | {'通过' if data['pass'] else '失败'} | "
        f"{avg.get('subtitle_improvement_ratio', 0) * 100:.1f}% | "
        f"{avg.get('strong_original_pixel_retention_ratio', 1) * 100:.2f}% | "
        f"{avg.get('glyph_residual_correlation', 1):.3f} | "
        f"{avg.get('outside_repair_region_mae', 999):.2f}/255 |"
    )
lines += ['', '## 判定口径', '',
          '“彻底去除”指选区及自动安全边内部不再保留原字幕栅格像素。被字幕遮住的真实背景在单帧中本来不存在，因此复杂纹理可能出现平滑或近似重建，不能承诺凭空恢复原始背景。']
(ROOT / 'V408_COMPLETE_REMOVAL_REPORT.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
raise SystemExit(0 if full_pass else 1)
