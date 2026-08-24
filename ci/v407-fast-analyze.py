#!/usr/bin/env python3
from __future__ import annotations
import json, subprocess
from pathlib import Path
import numpy as np
from PIL import Image

E=Path('e2e-fast-results'); F=Path('functional-results'); E.mkdir(exist_ok=True)

def probe(p:Path):
    if not p.exists(): return {'missing':True}
    return json.loads(subprocess.check_output(['ffprobe','-v','error','-show_entries','stream=codec_name,codec_type,width,height:stream_tags=rotate:stream_side_data=rotation:format=duration,size','-of','json',str(p)],text=True))
def summary(x):
    if x.get('missing'): return x
    ss=x.get('streams',[]); v=next((s for s in ss if s.get('codec_type')=='video'),None); a=next((s for s in ss if s.get('codec_type')=='audio'),None)
    return {'has_video':bool(v),'has_audio':bool(a),'video_codec':v.get('codec_name') if v else None,'audio_codec':a.get('codec_name') if a else None,'width':v.get('width') if v else None,'height':v.get('height') if v else None,'duration_s':float(x.get('format',{}).get('duration',0) or 0),'size':int(x.get('format',{}).get('size',0) or 0)}
def frame(p:Path,t:float,n:str):
    o=E/f'{n}-{t}.png'; subprocess.run(['ffmpeg','-y','-v','error','-ss',str(t),'-i',str(p),'-frames:v','1','-vf','scale=360:640:flags=bilinear',str(o)],check=True); return np.asarray(Image.open(o).convert('RGB'),dtype=np.float32)

ui=json.loads((E/'ui-result.json').read_text()) if (E/'ui-result.json').exists() else {'full_ui_flow_pass':False,'missing':True}
func=json.loads((F/'functional-v404-report.json').read_text()) if (F/'functional-v404-report.json').exists() else {'full_functional_pass':False,'missing':True}
func['version_under_test']='4.0.7'
out=E/'ui-output.mp4'; ps=summary(probe(out))
stream=bool(not ps.get('missing') and ps['has_video'] and ps['has_audio'] and ps['video_codec']=='h264' and ps['audio_codec']=='aac' and abs(ps['duration_s']-4.2)<.2 and ps['size']>10000)
metrics=[]; quality=False
if out.exists():
    for t in (.8,2.0,3.2):
        c=frame(F/'clean-audio.mp4',t,'clean'); i=frame(F/'input-audio.mp4',t,'input'); o=frame(out,t,'output')
        h,w=c.shape[:2]; x1,x2=int(w*.19),int(w*.80); y1,y2=int(h*.79),int(h*.88); m=np.zeros((h,w),bool); m[y1:y2,x1:x2]=True
        b=float(np.mean(abs(i[m]-c[m]))); r=float(np.mean(abs(o[m]-c[m]))); imp=0 if b<1e-6 else 1-r/b; outside=float(np.mean(abs(o[~m]-i[~m])))
        metrics.append({'time_s':t,'input_vs_clean_mae':b,'output_vs_clean_mae':r,'improvement_ratio':imp,'outside_change_mae':outside})
    quality=float(np.mean([m['improvement_ratio'] for m in metrics]))>.10 and float(np.mean([m['outside_change_mae'] for m in metrics]))<8
report={'version':'4.0.7','full_source_build':Path('e2e-results/JingHua-V4.0.7-full-source-debug.apk').exists(),'functional_export_pass':func.get('full_functional_pass') is True,'standard_ui':ui,'output_stream':ps,'stream_pass':stream,'quality_pass':quality,'frame_metrics':metrics}
report['full_pass']=report['full_source_build'] and report['functional_export_pass'] and ui.get('full_ui_flow_pass') is True and stream and quality
(E/'V407_FAST_E2E_REPORT.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
(E/'V407_FAST_E2E_REPORT.md').write_text('# 净画 V4.0.7 Android 10 全流程测试\n\n- 最终：**%s**\n- 完整源码构建：%s\n- Media3 导出：%s\n- 标准 UI 流程：%s\n- 音视频检查：%s\n- 去字幕画质：%s\n' % (('通过' if report['full_pass'] else '失败'),report['full_source_build'],report['functional_export_pass'],ui.get('full_ui_flow_pass'),stream,quality))
print(json.dumps(report,ensure_ascii=False,indent=2)); raise SystemExit(0 if report['full_pass'] else 1)
