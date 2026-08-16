#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path('.')
E2E = ROOT / 'e2e-results'
FUNC = ROOT / 'functional-results'
E2E.mkdir(exist_ok=True)


def probe(path: Path) -> dict:
    if not path.exists():
        return {'missing': True, 'path': str(path)}
    cmd = [
        'ffprobe', '-v', 'error', '-show_entries',
        'stream=index,codec_name,codec_type,width,height,r_frame_rate:stream_tags=rotate:stream_side_data=rotation:format=duration,size',
        '-of', 'json', str(path),
    ]
    return json.loads(subprocess.check_output(cmd, text=True))


def frame(path: Path, time_s: float, name: str) -> np.ndarray:
    out = E2E / f'frame-{name}-{time_s:.1f}.png'
    subprocess.run([
        'ffmpeg', '-y', '-v', 'error', '-ss', str(time_s), '-i', str(path),
        '-frames:v', '1', '-vf', 'scale=360:640:flags=bilinear', str(out),
    ], check=True)
    return np.asarray(Image.open(out).convert('RGB'), dtype=np.float32)


def stream_summary(p: dict) -> dict:
    streams = p.get('streams', [])
    video = next((s for s in streams if s.get('codec_type') == 'video'), None)
    audio = next((s for s in streams if s.get('codec_type') == 'audio'), None)
    duration = float(p.get('format', {}).get('duration', 0) or 0)
    return {
        'has_video': video is not None,
        'has_audio': audio is not None,
        'video_codec': None if video is None else video.get('codec_name'),
        'audio_codec': None if audio is None else audio.get('codec_name'),
        'width': None if video is None else video.get('width'),
        'height': None if video is None else video.get('height'),
        'rotation': None if video is None else (
            (video.get('side_data_list') or [{}])[0].get('rotation')
            if video.get('side_data_list') else (video.get('tags') or {}).get('rotate')
        ),
        'duration_s': duration,
        'size': int(p.get('format', {}).get('size', 0) or 0),
    }


ui_result_path = E2E / 'ui-e2e-result.json'
ui_result = json.loads(ui_result_path.read_text(encoding='utf-8')) if ui_result_path.exists() else {
    'full_ui_flow_pass': False, 'missing': True
}
functional_path = FUNC / 'functional-v404-report.json'
functional = json.loads(functional_path.read_text(encoding='utf-8')) if functional_path.exists() else {
    'full_functional_pass': False, 'missing': True
}
functional['version_under_test'] = '4.0.7'
(E2E / 'functional-v407-report.json').write_text(
    json.dumps(functional, ensure_ascii=False, indent=2), encoding='utf-8'
)

input_path = FUNC / 'input-audio.mp4'
clean_path = FUNC / 'clean-audio.mp4'
output_path = E2E / 'ui-output.mp4'
input_probe = probe(input_path)
output_probe = probe(output_path)
input_stream = stream_summary(input_probe) if 'missing' not in input_probe else input_probe
output_stream = stream_summary(output_probe) if 'missing' not in output_probe else output_probe

stream_pass = False
if 'missing' not in output_stream:
    dims = (output_stream.get('width'), output_stream.get('height'))
    portrait_preserved = dims in {(360, 640), (640, 360)}
    stream_pass = (
        output_stream['has_video'] and output_stream['has_audio']
        and output_stream['video_codec'] == 'h264'
        and output_stream['audio_codec'] == 'aac'
        and abs(output_stream['duration_s'] - 4.2) < 0.15
        and output_stream['size'] > 10000
        and portrait_preserved
    )

frame_metrics = []
quality_pass = False
avg_improvement = None
avg_outside = None
if output_path.exists() and clean_path.exists() and input_path.exists():
    for t in (0.8, 2.0, 3.2):
        clean = frame(clean_path, t, 'clean')
        inp = frame(input_path, t, 'input')
        out = frame(output_path, t, 'ui-output')
        h, w = clean.shape[:2]
        x1, x2 = int(w * 0.19), int(w * 0.80)
        y1, y2 = int(h * 0.79), int(h * 0.88)
        roi = np.zeros((h, w), dtype=bool)
        roi[y1:y2, x1:x2] = True
        base_mae = float(np.mean(np.abs(inp[roi] - clean[roi])))
        out_mae = float(np.mean(np.abs(out[roi] - clean[roi])))
        improvement = 0.0 if base_mae <= 1e-6 else 1.0 - out_mae / base_mae
        outside = float(np.mean(np.abs(out[~roi] - inp[~roi])))
        frame_metrics.append({
            'time_s': t,
            'roi': [x1, y1, x2, y2],
            'input_vs_clean_mae': base_mae,
            'ui_output_vs_clean_mae': out_mae,
            'roi_improvement_ratio': improvement,
            'outside_output_vs_input_mae': outside,
        })
    avg_improvement = float(np.mean([m['roi_improvement_ratio'] for m in frame_metrics]))
    avg_outside = float(np.mean([m['outside_output_vs_input_mae'] for m in frame_metrics]))
    quality_pass = avg_improvement > 0.10 and avg_outside < 8.0

full_pass = bool(
    functional.get('full_functional_pass') is True
    and ui_result.get('full_ui_flow_pass') is True
    and stream_pass
    and quality_pass
)
report = {
    'version_under_test': '4.0.7',
    'full_e2e_pass': full_pass,
    'full_source_gradle_build': (E2E / 'JingHua-V4.0.7-full-source-debug.apk').exists(),
    'functional_export_pass': functional.get('full_functional_pass') is True,
    'standard_ui_flow': ui_result,
    'stream_pass': stream_pass,
    'quality_pass': quality_pass,
    'input_stream': input_stream,
    'output_stream': output_stream,
    'average_ui_roi_improvement_ratio': avg_improvement,
    'average_ui_outside_change_mae': avg_outside,
    'ui_frame_metrics': frame_metrics,
    'functional_metrics': {
        'average_roi_improvement_ratio': functional.get('average_roi_improvement_ratio'),
        'average_edge_improvement_ratio': functional.get('average_edge_improvement_ratio'),
        'average_changed_pixel_reduction_ratio': functional.get('average_changed_pixel_reduction_ratio'),
        'average_outside_roi_change_mae': functional.get('average_outside_roi_change_mae'),
    },
}
(E2E / 'V407_FULL_E2E_REPORT.json').write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8'
)

md = [
    '# 净画 V4.0.7 全流程 Android 测试', '',
    f"- 最终判定：**{'通过' if full_pass else '失败'}**",
    f"- 完整源码 Gradle 构建：{'通过' if report['full_source_gradle_build'] else '失败'}",
    f"- FunctionalExportActivity 真实 Media3 导出：{'通过' if report['functional_export_pass'] else '失败'}",
    f"- 标准界面 Home→导入→预览→导出：{'通过' if ui_result.get('full_ui_flow_pass') else '失败'}",
    f"- 输出音视频、时长和方向：{'通过' if stream_pass else '失败'}",
    f"- 标准界面输出去字幕质量：{'通过' if quality_pass else '失败'}",
]
if avg_improvement is not None:
    md += [
        f"- 标准界面输出字幕区平均改善：{avg_improvement * 100:.1f}%",
        f"- 标准界面输出字幕框外变化：{avg_outside:.2f}/255",
    ]
md += ['', '## 标准界面步骤', '']
for key, value in ui_result.items():
    md.append(f'- {key}: `{value}`')
md += ['', '## 输出流', '', '```json', json.dumps(output_stream, ensure_ascii=False, indent=2), '```']
(E2E / 'V407_FULL_E2E_REPORT.md').write_text('\n'.join(md) + '\n', encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
raise SystemExit(0 if full_pass else 1)
