#!/usr/bin/env python3
from __future__ import annotations
import json
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else '.')
FRAMES = ROOT / 'frames'
FRAMES.mkdir(parents=True, exist_ok=True)
TIMES = [0.8, 2.0, 3.2]
ROI = (0.06, 0.76, 0.94, 0.93)
CASES = [
    {'name': 'baseline_noaudio', 'input': 'input-noaudio.mp4', 'expect_audio': False, 'visual': False},
    {'name': 'blur_noaudio', 'input': 'input-noaudio.mp4', 'expect_audio': False, 'visual': True},
    {'name': 'repair_noaudio', 'input': 'input-noaudio.mp4', 'expect_audio': False, 'visual': True},
    {'name': 'baseline_audio', 'input': 'input-audio.mp4', 'expect_audio': True, 'visual': False},
    {'name': 'repair_audio', 'input': 'input-audio.mp4', 'expect_audio': True, 'visual': True},
]


def run(cmd: list[str]) -> str:
    return subprocess.check_output(cmd, text=True, stderr=subprocess.STDOUT)


def probe(path: Path) -> dict:
    raw = run(['ffprobe', '-v', 'error', '-show_entries',
               'format=duration,size:stream=index,codec_type,codec_name,width,height,r_frame_rate',
               '-of', 'json', str(path)])
    return json.loads(raw)


def extract_frame(video: Path, time_s: float, label: str) -> tuple[np.ndarray, Path]:
    out = FRAMES / f'{label}-{time_s:.1f}.png'
    subprocess.check_call(['ffmpeg', '-y', '-v', 'error', '-ss', str(time_s), '-i', str(video),
                           '-frames:v', '1', '-pix_fmt', 'rgb24', str(out)])
    return np.asarray(Image.open(out).convert('RGB'), dtype=np.float32), out


def mae(a: np.ndarray, b: np.ndarray) -> float:
    return float(np.mean(np.abs(a - b)))


def edge_energy(a: np.ndarray) -> float:
    gray = a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114
    return float(np.abs(np.diff(gray, axis=1)).mean() + np.abs(np.diff(gray, axis=0)).mean())


def marker_text(case_name: str) -> str:
    path = ROOT / f'result-{case_name}.txt'
    return path.read_text(encoding='utf-8', errors='replace') if path.exists() else 'MISSING'


def create_comparison(case_name: str, clean_path: Path, input_path: Path, output_path: Path) -> None:
    time_s = 2.0
    _, clean_img = extract_frame(clean_path, time_s, f'{case_name}-clean')
    _, input_img = extract_frame(input_path, time_s, f'{case_name}-input')
    _, output_img = extract_frame(output_path, time_s, f'{case_name}-output')
    images = [Image.open(p).convert('RGB') for p in (clean_img, input_img, output_img)]
    w, h = images[0].size
    canvas = Image.new('RGB', (w * 3, h + 42), 'white')
    draw = ImageDraw.Draw(canvas)
    labels = ['无字幕原片', '硬字幕输入', '净画输出']
    for idx, image in enumerate(images):
        canvas.paste(image, (idx * w, 42))
        draw.text((idx * w + 8, 10), labels[idx], fill='black')
    canvas.save(ROOT / f'comparison-{case_name}.jpg', quality=92)


clean_path = ROOT / 'clean-noaudio.mp4'
clean_probe = probe(clean_path)
results = []
for case in CASES:
    name = case['name']
    input_path = ROOT / case['input']
    output_path = ROOT / f'output-{name}.mp4'
    marker = marker_text(name)
    marker_pass = marker.startswith('PASS')
    result = {
        'case': name,
        'marker_pass': marker_pass,
        'marker': marker,
        'output_exists': output_path.is_file(),
        'output_bytes': output_path.stat().st_size if output_path.exists() else 0,
        'expected_audio': case['expect_audio'],
        'visual_effect_case': case['visual'],
    }
    if not output_path.is_file() or output_path.stat().st_size < 1024:
        result['case_pass'] = False
        result['failure_stage'] = 'no_valid_output'
        results.append(result)
        continue
    try:
        input_probe = probe(input_path)
        output_probe = probe(output_path)
        streams = output_probe.get('streams', [])
        has_video = any(s.get('codec_type') == 'video' for s in streams)
        has_audio = any(s.get('codec_type') == 'audio' for s in streams)
        input_duration = float(input_probe['format']['duration'])
        output_duration = float(output_probe['format']['duration'])
        duration_delta = abs(output_duration - input_duration)
        result.update({
            'input_duration_s': input_duration,
            'output_duration_s': output_duration,
            'duration_delta_s': duration_delta,
            'has_video': has_video,
            'has_audio': has_audio,
            'output_probe': output_probe,
        })
        stream_pass = has_video and (has_audio == case['expect_audio']) and duration_delta < 0.35
        visual_pass = True
        if case['visual']:
            metrics = []
            for t in TIMES:
                clean, _ = extract_frame(clean_path, t, f'{name}-clean')
                source, _ = extract_frame(input_path, t, f'{name}-input')
                output, _ = extract_frame(output_path, t, f'{name}-output')
                h, w = clean.shape[:2]
                x1, y1, x2, y2 = int(w*ROI[0]), int(h*ROI[1]), int(w*ROI[2]), int(h*ROI[3])
                clean_roi = clean[y1:y2, x1:x2]
                input_roi = source[y1:y2, x1:x2]
                output_roi = output[y1:y2, x1:x2]
                mask = np.ones((h, w), dtype=bool)
                mask[y1:y2, x1:x2] = False
                input_to_clean = mae(input_roi, clean_roi)
                output_to_clean = mae(output_roi, clean_roi)
                improvement = 1.0 - output_to_clean / max(0.001, input_to_clean)
                outside_change = float(np.mean(np.abs(output[mask] - source[mask])))
                metrics.append({
                    'time_s': t,
                    'roi_input_vs_clean_mae': input_to_clean,
                    'roi_output_vs_clean_mae': output_to_clean,
                    'roi_improvement_ratio': improvement,
                    'roi_edge_clean': edge_energy(clean_roi),
                    'roi_edge_input': edge_energy(input_roi),
                    'roi_edge_output': edge_energy(output_roi),
                    'outside_output_vs_input_mae': outside_change,
                })
            avg_improvement = float(np.mean([m['roi_improvement_ratio'] for m in metrics]))
            avg_outside = float(np.mean([m['outside_output_vs_input_mae'] for m in metrics]))
            result['frame_metrics'] = metrics
            result['average_roi_improvement_ratio'] = avg_improvement
            result['average_outside_roi_change_mae'] = avg_outside
            visual_pass = avg_improvement > 0.05 and avg_outside < 14.0
            create_comparison(name, clean_path, input_path, output_path)
        result['stream_pass'] = stream_pass
        result['visual_pass'] = visual_pass
        result['case_pass'] = bool(marker_pass and stream_pass and visual_pass)
    except Exception as error:
        result['case_pass'] = False
        result['failure_stage'] = 'analysis_exception'
        result['analysis_error'] = f'{type(error).__name__}: {error}'
    results.append(result)

by_name = {item['case']: item for item in results}
full_functional_pass = bool(by_name.get('repair_audio', {}).get('case_pass'))
diagnosis = []
if not by_name.get('baseline_noaudio', {}).get('marker_pass'):
    diagnosis.append('基础无音频转码失败：问题位于编码/MP4封装或模拟器环境，不是字幕算法。')
elif not by_name.get('blur_noaudio', {}).get('marker_pass'):
    diagnosis.append('基础转码成功但模糊效果失败：问题位于OpenGL效果链。')
elif not by_name.get('repair_noaudio', {}).get('marker_pass'):
    diagnosis.append('模糊成功但高质量修复失败：问题位于修复着色器或历史帧逻辑。')
elif not by_name.get('baseline_audio', {}).get('marker_pass'):
    diagnosis.append('无音频导出成功、带音频基础导出失败：问题主要位于音频转码/封装兼容。')
elif not by_name.get('repair_audio', {}).get('marker_pass'):
    diagnosis.append('基础音频导出成功、带修复音频导出失败：问题位于效果与音视频封装组合。')
elif not by_name.get('repair_audio', {}).get('visual_pass'):
    diagnosis.append('导出完成但字幕区域改善不足：当前修复算法质量未达到实用阈值。')
else:
    diagnosis.append('带音频高质量字幕修复完整通过。')

report = {
    'full_functional_pass': full_functional_pass,
    'clean_probe': clean_probe,
    'diagnosis': diagnosis,
    'cases': results,
}
(ROOT / 'functional-matrix-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
md = ['# 净画 V4.0.3 真实字幕去除矩阵测试', '',
      f'- 完整功能判定：**{"通过" if full_functional_pass else "未通过"}**', '',
      '## 诊断', '']
md += [f'- {line}' for line in diagnosis]
md += ['', '## 用例结果', '', '| 用例 | Activity结果 | 输出大小 | 音频 | 时长差 | 视觉改善 | 判定 |',
       '|---|---|---:|---|---:|---:|---|']
for item in results:
    improvement = item.get('average_roi_improvement_ratio')
    md.append('| {case} | {marker} | {bytes} | {audio} | {duration} | {improvement} | {status} |'.format(
        case=item['case'], marker='PASS' if item['marker_pass'] else 'FAIL', bytes=item['output_bytes'],
        audio=('有' if item.get('has_audio') else '无') if item.get('output_exists') else '-',
        duration=f"{item.get('duration_delta_s', 0):.3f}s" if 'duration_delta_s' in item else '-',
        improvement=f"{improvement*100:.1f}%" if improvement is not None else '-',
        status='通过' if item.get('case_pass') else '未通过'))
md += ['', '完整通过要求：高质量修复能生成可播放视频、保留音频与时长，并让字幕框更接近同源无字幕画面。']
(ROOT / 'functional-matrix-report.md').write_text('\n'.join(md), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
if not full_functional_pass:
    raise SystemExit(1)
