#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path('v410-functional')
EVIDENCE = ROOT / 'evidence'
EVIDENCE.mkdir(parents=True, exist_ok=True)
STYLES = ['white-outline', 'color-outline', 'double-line']
TIMES = [0.8, 2.0, 3.2]


def probe(path: Path) -> dict:
    if not path.exists():
        return {'missing': True, 'path': str(path)}
    return json.loads(subprocess.check_output([
        'ffprobe', '-v', 'error',
        '-show_entries', 'stream=codec_name,codec_type,width,height:format=duration,size',
        '-of', 'json', str(path),
    ], text=True))


def stream(path: Path) -> dict:
    raw = probe(path)
    if raw.get('missing'):
        return raw
    streams = raw.get('streams', [])
    video = next((item for item in streams if item.get('codec_type') == 'video'), None)
    audio = next((item for item in streams if item.get('codec_type') == 'audio'), None)
    return {
        'path': str(path),
        'has_video': video is not None,
        'has_audio': audio is not None,
        'video_codec': None if video is None else video.get('codec_name'),
        'audio_codec': None if audio is None else audio.get('codec_name'),
        'width': None if video is None else video.get('width'),
        'height': None if video is None else video.get('height'),
        'duration_s': float(raw.get('format', {}).get('duration', 0) or 0),
        'size_bytes': int(raw.get('format', {}).get('size', 0) or 0),
    }


def frame(path: Path, time_s: float, name: str) -> np.ndarray:
    output = EVIDENCE / f'{name}-{time_s:.1f}.png'
    subprocess.run([
        'ffmpeg', '-y', '-v', 'error', '-ss', str(time_s), '-i', str(path),
        '-frames:v', '1', '-vf', 'scale=360:640:flags=bilinear', str(output),
    ], check=True)
    return np.asarray(Image.open(output).convert('RGB'), dtype=np.float32)


def montage(style: str, clean: np.ndarray, baseline: np.ndarray, repaired: np.ndarray) -> None:
    sheet = Image.new('RGB', (1080, 680), 'white')
    draw = ImageDraw.Draw(sheet)
    for index, (title, array) in enumerate([
        ('Clean reference', clean), ('Subtitle baseline', baseline), ('V4.1.0 repaired', repaired)
    ]):
        image = Image.fromarray(np.clip(array, 0, 255).astype(np.uint8))
        sheet.paste(image, (index * 360, 40))
        draw.text((index * 360 + 10, 12), title, fill='black')
    sheet.save(EVIDENCE / f'{style}-comparison.jpg', quality=92)


def valid_stream(info: dict) -> bool:
    return bool(
        not info.get('missing')
        and info.get('has_video') and info.get('has_audio')
        and info.get('video_codec') == 'h264'
        and info.get('audio_codec') == 'aac'
        and info.get('width') == 360 and info.get('height') == 640
        and abs(info.get('duration_s', 0) - 4.2) < 0.20
        and info.get('size_bytes', 0) > 10000
    )


marker_files = sorted(ROOT.glob('result-*.txt'))
markers_pass = len(marker_files) == 7 and all(
    file.read_text(encoding='utf-8', errors='ignore').startswith('PASS') for file in marker_files
)
emulator_pass = (ROOT / 'emulator-pass.txt').exists() and (ROOT / 'emulator-pass.txt').read_text().strip() == '1'

clean_path = ROOT / 'output-clean.mp4'
clean_stream = stream(clean_path)
clean_stream_pass = valid_stream(clean_stream)
style_reports = {}
all_styles_pass = True

for style in STYLES:
    baseline_path = ROOT / f'output-{style}-baseline.mp4'
    repair_path = ROOT / f'output-{style}-repair.mp4'
    baseline_stream = stream(baseline_path)
    repair_stream = stream(repair_path)
    streams_pass = valid_stream(baseline_stream) and valid_stream(repair_stream)
    frames = []
    mid = None
    if streams_pass and clean_stream_pass:
        for time_s in TIMES:
            clean = frame(clean_path, time_s, f'{style}-clean')
            baseline = frame(baseline_path, time_s, f'{style}-baseline')
            repaired = frame(repair_path, time_s, f'{style}-repair')
            if abs(time_s - 2.0) < 0.01:
                mid = (clean, baseline, repaired)

            delta = np.max(np.abs(baseline - clean), axis=2)
            glyph = delta > 24.0
            glyph_pixels = int(glyph.sum())
            if glyph_pixels < 80:
                frames.append({'time_s': time_s, 'glyph_pixels': glyph_pixels, 'pass': False})
                continue

            base_mae = float(np.mean(np.abs(baseline[glyph] - clean[glyph])))
            repair_mae = float(np.mean(np.abs(repaired[glyph] - clean[glyph])))
            improvement = 1.0 - repair_mae / max(base_mae, 1e-6)
            base_distance = np.linalg.norm(baseline - clean, axis=2)
            moved_from_glyph = np.linalg.norm(repaired - baseline, axis=2)
            strong_retention = float(np.mean(
                moved_from_glyph[glyph] < 0.25 * np.maximum(base_distance[glyph], 1e-6)
            ))

            h, w = glyph.shape
            repair_region = np.zeros((h, w), dtype=bool)
            repair_region[int(h * 0.68):int(h * 0.97), int(w * 0.01):int(w * 0.99)] = True
            outside_mae = float(np.mean(np.abs(repaired[~repair_region] - baseline[~repair_region])))
            frame_pass = improvement >= 0.70 and strong_retention <= 0.06 and outside_mae <= 4.0
            frames.append({
                'time_s': time_s,
                'glyph_pixels': glyph_pixels,
                'input_glyph_mae': base_mae,
                'repaired_glyph_mae': repair_mae,
                'subtitle_improvement_ratio': improvement,
                'strong_original_glyph_retention_ratio': strong_retention,
                'outside_repair_region_mae': outside_mae,
                'pass': frame_pass,
            })

    valid = [item for item in frames if 'subtitle_improvement_ratio' in item]
    averages = {}
    if valid:
        averages = {
            'subtitle_improvement_ratio': float(np.mean([item['subtitle_improvement_ratio'] for item in valid])),
            'strong_original_glyph_retention_ratio': float(np.mean([item['strong_original_glyph_retention_ratio'] for item in valid])),
            'outside_repair_region_mae': float(np.mean([item['outside_repair_region_mae'] for item in valid])),
        }
    style_pass = bool(
        streams_pass
        and len(valid) == len(TIMES)
        and all(item['pass'] for item in valid)
        and averages.get('subtitle_improvement_ratio', 0) >= 0.80
        and averages.get('strong_original_glyph_retention_ratio', 1) <= 0.03
        and averages.get('outside_repair_region_mae', 999) <= 3.0
    )
    all_styles_pass = all_styles_pass and style_pass
    style_reports[style] = {
        'pass': style_pass,
        'streams_pass': streams_pass,
        'baseline_stream': baseline_stream,
        'repair_stream': repair_stream,
        'averages': averages,
        'frames': frames,
    }
    if mid is not None:
        montage(style, *mid)

full_pass = bool(emulator_pass and markers_pass and clean_stream_pass and all_styles_pass)
report = {
    'version': '4.1.0',
    'full_pass': full_pass,
    'android_emulator_pass': emulator_pass,
    'all_markers_pass': markers_pass,
    'clean_stream_pass': clean_stream_pass,
    'clean_stream': clean_stream,
    'styles': style_reports,
    'thresholds': {
        'per_frame_improvement_min': 0.70,
        'average_improvement_min': 0.80,
        'average_strong_glyph_retention_max': 0.03,
        'average_outside_change_mae_max': 3.0,
    },
}
(ROOT / 'V410_FUNCTIONAL_REPORT.json').write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

lines = [
    '# 净画 V4.1.0 Android 真导出测试', '',
    f"- 最终：**{'通过' if full_pass else '失败'}**",
    f"- 七次 Media3 导出：{'通过' if emulator_pass and markers_pass else '失败'}",
    f"- 完整画面、H.264、AAC、时长：{'通过' if clean_stream_pass else '失败'}", '',
    '| 字幕类型 | 判定 | 字幕像素改善 | 原字形强残留 | 选区外变化 |',
    '|---|---:|---:|---:|---:|',
]
for style, item in style_reports.items():
    avg = item.get('averages', {})
    lines.append(
        f"| {style} | {'通过' if item['pass'] else '失败'} | "
        f"{avg.get('subtitle_improvement_ratio', 0) * 100:.1f}% | "
        f"{avg.get('strong_original_glyph_retention_ratio', 1) * 100:.2f}% | "
        f"{avg.get('outside_repair_region_mae', 999):.2f}/255 |"
    )
(ROOT / 'V410_FUNCTIONAL_REPORT.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
raise SystemExit(0 if full_pass else 1)
