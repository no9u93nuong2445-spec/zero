#!/usr/bin/env python3
from __future__ import annotations
import json
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else '.')
CLEAN = ROOT / 'clean.mp4'
INPUT = ROOT / 'input.mp4'
OUTPUT = ROOT / 'output.mp4'
FRAMES = ROOT / 'frames'
FRAMES.mkdir(parents=True, exist_ok=True)
TIMES = [0.8, 2.0, 3.2]
ROI = (0.06, 0.76, 0.94, 0.93)


def run(cmd: list[str]) -> str:
    return subprocess.check_output(cmd, text=True, stderr=subprocess.STDOUT)


def probe(path: Path) -> dict:
    raw = run(['ffprobe', '-v', 'error', '-show_entries',
               'format=duration,size:stream=index,codec_type,codec_name,width,height,r_frame_rate',
               '-of', 'json', str(path)])
    return json.loads(raw)


def frame(video: Path, time_s: float, label: str) -> np.ndarray:
    out = FRAMES / f'{label}-{time_s:.1f}.png'
    subprocess.check_call(['ffmpeg', '-y', '-v', 'error', '-ss', str(time_s), '-i', str(video),
                           '-frames:v', '1', '-pix_fmt', 'rgb24', str(out)])
    return np.asarray(Image.open(out).convert('RGB'), dtype=np.float32)


def mae(a: np.ndarray, b: np.ndarray) -> float:
    return float(np.mean(np.abs(a - b)))


def edge_energy(a: np.ndarray) -> float:
    gray = a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114
    gx = np.abs(np.diff(gray, axis=1)).mean()
    gy = np.abs(np.diff(gray, axis=0)).mean()
    return float(gx + gy)

clean_probe = probe(CLEAN)
input_probe = probe(INPUT)
output_probe = probe(OUTPUT)
metrics = []
for t in TIMES:
    c = frame(CLEAN, t, 'clean')
    i = frame(INPUT, t, 'input')
    o = frame(OUTPUT, t, 'output')
    h, w = c.shape[:2]
    x1, y1, x2, y2 = int(w*ROI[0]), int(h*ROI[1]), int(w*ROI[2]), int(h*ROI[3])
    croi, iroi, oroi = c[y1:y2, x1:x2], i[y1:y2, x1:x2], o[y1:y2, x1:x2]
    mask = np.ones((h, w), dtype=bool)
    mask[y1:y2, x1:x2] = False
    input_to_clean = mae(iroi, croi)
    output_to_clean = mae(oroi, croi)
    improvement = 1.0 - output_to_clean / max(0.001, input_to_clean)
    outside_change = float(np.mean(np.abs(o[mask] - i[mask])))
    metrics.append({
        'time_s': t,
        'roi_input_vs_clean_mae': input_to_clean,
        'roi_output_vs_clean_mae': output_to_clean,
        'roi_improvement_ratio': improvement,
        'roi_edge_clean': edge_energy(croi),
        'roi_edge_input': edge_energy(iroi),
        'roi_edge_output': edge_energy(oroi),
        'outside_output_vs_input_mae': outside_change,
    })

output_duration = float(output_probe['format']['duration'])
input_duration = float(input_probe['format']['duration'])
streams = output_probe.get('streams', [])
has_video = any(s.get('codec_type') == 'video' for s in streams)
has_audio = any(s.get('codec_type') == 'audio' for s in streams)
avg_improvement = float(np.mean([m['roi_improvement_ratio'] for m in metrics]))
avg_outside = float(np.mean([m['outside_output_vs_input_mae'] for m in metrics]))
duration_delta = abs(output_duration - input_duration)
functional_pass = bool(has_video and has_audio and duration_delta < 0.25 and avg_improvement > 0.10 and avg_outside < 12.0)

report = {
    'functional_pass': functional_pass,
    'criteria': {
        'has_video': has_video,
        'has_audio': has_audio,
        'duration_delta_lt_0_25s': duration_delta < 0.25,
        'average_roi_improvement_gt_10pct': avg_improvement > 0.10,
        'outside_roi_mae_lt_12': avg_outside < 12.0,
    },
    'input_duration_s': input_duration,
    'output_duration_s': output_duration,
    'duration_delta_s': duration_delta,
    'average_roi_improvement_ratio': avg_improvement,
    'average_outside_roi_change_mae': avg_outside,
    'input_probe': input_probe,
    'output_probe': output_probe,
    'frame_metrics': metrics,
}
(ROOT / 'functional-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
md = [
    '# 净画 V4.0.3 字幕去除功能测试', '',
    f'- 功能判定：**{"通过" if functional_pass else "未通过"}**',
    f'- 输入时长：{input_duration:.3f} 秒',
    f'- 输出时长：{output_duration:.3f} 秒',
    f'- 音频保留：{"是" if has_audio else "否"}',
    f'- 字幕区域相对无字幕原片的平均改善：{avg_improvement*100:.1f}%',
    f'- 字幕框外平均像素变化：{avg_outside:.2f}/255', '',
    '## 分帧指标', '',
    '| 时间 | 输入字幕区误差 | 输出字幕区误差 | 改善 | 框外变化 |',
    '|---:|---:|---:|---:|---:|',
]
for m in metrics:
    md.append(f"| {m['time_s']:.1f}s | {m['roi_input_vs_clean_mae']:.2f} | {m['roi_output_vs_clean_mae']:.2f} | {m['roi_improvement_ratio']*100:.1f}% | {m['outside_output_vs_input_mae']:.2f} |")
md += ['', '说明：测试视频由同一段动态背景分别生成“无字幕版”和“硬字幕版”。净画只接收硬字幕版，输出再与无字幕原片逐帧比较。']
(ROOT / 'functional-report.md').write_text('\n'.join(md), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
if not functional_pass:
    raise SystemExit(1)
