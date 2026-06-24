#!/usr/bin/env python3
from __future__ import annotations
import json, subprocess
from pathlib import Path
import numpy as np
from PIL import Image

ROOT=Path('v409-results')
ANDROID=ROOT/'android'
FIX=ROOT/'fixtures'

def probe(path:Path)->dict:
    if not path.exists(): return {'missing':True,'path':str(path)}
    return json.loads(subprocess.check_output([
        'ffprobe','-v','error','-show_entries',
        'stream=codec_name,codec_type,width,height:format=duration,size',
        '-of','json',str(path)
    ],text=True))

def summary(p:dict)->dict:
    if p.get('missing'): return p
    streams=p.get('streams',[])
    v=next((s for s in streams if s.get('codec_type')=='video'),None)
    a=next((s for s in streams if s.get('codec_type')=='audio'),None)
    return {
        'has_video':v is not None,'has_audio':a is not None,
        'video_codec':None if v is None else v.get('codec_name'),
        'audio_codec':None if a is None else a.get('codec_name'),
        'width':None if v is None else v.get('width'),
        'height':None if v is None else v.get('height'),
        'duration_s':float(p.get('format',{}).get('duration',0) or 0),
        'size_bytes':int(p.get('format',{}).get('size',0) or 0),
    }

def frame(path:Path,t:float,name:str)->np.ndarray:
    out=ANDROID/f'{name}-{t:.1f}.png'
    subprocess.run(['ffmpeg','-y','-v','error','-ss',str(t),'-i',str(path),'-frames:v','1','-vf','scale=360:640:flags=bilinear',str(out)],check=True)
    return np.asarray(Image.open(out).convert('RGB'),dtype=np.float32)

ui_path=ANDROID/'UI_RESULT.json'
ui=json.loads(ui_path.read_text(encoding='utf-8')) if ui_path.exists() else {'ui_flow_pass':False,'missing':True}
source=json.loads((ROOT/'source-assertions.json').read_text(encoding='utf-8'))
input_stream=summary(probe(FIX/'subtitle.mp4'))
output_stream=summary(probe(ANDROID/'output.mp4'))
stream_pass=bool(
    not output_stream.get('missing')
    and output_stream.get('has_video') and output_stream.get('has_audio')
    and output_stream.get('video_codec')=='h264'
    and output_stream.get('audio_codec')=='aac'
    and output_stream.get('width')==input_stream.get('width')
    and output_stream.get('height')==input_stream.get('height')
    and abs(output_stream.get('duration_s',0)-input_stream.get('duration_s',0))<0.20
    and output_stream.get('size_bytes',0)>10000
)

frames=[]
quality_pass=False
if (ANDROID/'output.mp4').exists():
    for t in (0.8,2.0,3.2):
        clean=frame(FIX/'clean.mp4',t,'clean')
        subtitle=frame(FIX/'subtitle.mp4',t,'subtitle')
        output=frame(ANDROID/'output.mp4',t,'output')
        delta=np.max(np.abs(subtitle-clean),axis=2)
        glyph=delta>24
        base=float(np.mean(np.abs(subtitle[glyph]-clean[glyph])))
        repaired=float(np.mean(np.abs(output[glyph]-clean[glyph])))
        improvement=1-repaired/max(base,1e-6)
        base_dist=np.linalg.norm(subtitle-clean,axis=2)
        out_to_input=np.linalg.norm(output-subtitle,axis=2)
        strong_retention=float(np.mean(out_to_input[glyph] < 0.25*np.maximum(base_dist[glyph],1e-6)))
        h,w=glyph.shape
        region=np.zeros((h,w),bool); region[int(h*.69):int(h*.98),int(w*.01):int(w*.99)]=True
        outside=float(np.mean(np.abs(output[~region]-subtitle[~region])))
        frames.append({'time_s':t,'subtitle_improvement_ratio':improvement,'strong_original_pixel_retention_ratio':strong_retention,'outside_change_mae':outside})
    avg_imp=float(np.mean([f['subtitle_improvement_ratio'] for f in frames]))
    avg_ret=float(np.mean([f['strong_original_pixel_retention_ratio'] for f in frames]))
    avg_out=float(np.mean([f['outside_change_mae'] for f in frames]))
    quality_pass=avg_imp>=0.75 and avg_ret<=0.05 and avg_out<=4.0
else:
    avg_imp=avg_ret=avg_out=None

report={
    'version':'4.0.9',
    'full_pass':bool(all(source.values()) and ui.get('ui_flow_pass') is True and stream_pass and quality_pass),
    'source_fixes':source,
    'ui':ui,
    'input_stream':input_stream,
    'output_stream':output_stream,
    'full_frame_stream_pass':stream_pass,
    'subtitle_quality_pass':quality_pass,
    'average_subtitle_improvement_ratio':avg_imp,
    'average_strong_original_pixel_retention_ratio':avg_ret,
    'average_outside_change_mae':avg_out,
    'frames':frames,
}
(ROOT/'V409_TEST_REPORT.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
lines=[
    '# 净画 V4.0.9 精简去字幕测试','',
    f"- 最终：**{'通过' if report['full_pass'] else '失败'}**",
    f"- 精简首页与编辑器：{'通过' if source.get('simple_home') and source.get('simple_editor') and ui.get('simple_home') and ui.get('editor_simple') else '失败'}",
    f"- 大缩略图视频选择器：{'通过' if source.get('large_picker') and ui.get('photo_picker') else '失败'}",
    f"- 导入后自动播放：{'通过' if ui.get('autoplay') and ui.get('timeline_moves') else '失败'}",
    f"- 导出完整画面而非选区：{'通过' if stream_pass else '失败'}",
    f"- 字幕清除质量：{'通过' if quality_pass else '失败'}",
]
if avg_imp is not None:
    lines += [f'- 字幕像素平均改善：{avg_imp*100:.1f}%',f'- 原字幕强残留：{avg_ret*100:.2f}%',f'- 选区外变化：{avg_out:.2f}/255']
(ROOT/'V409_TEST_REPORT.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
raise SystemExit(0 if report['full_pass'] else 1)
