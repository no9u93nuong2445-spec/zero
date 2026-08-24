from pathlib import Path

path = Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/HomeActivity.java')
text = path.read_text(encoding='utf-8')
bad = '''"1. 选择视频  ·  2. 拖动黄色框覆盖字幕  ·  3. 预览并导出

导出始终保留完整视频画面，选框只决定去字幕的位置。",'''
good = '"1. 选择视频  ·  2. 拖动黄色框覆盖字幕  ·  3. 预览并导出\\n\\n导出始终保留完整视频画面，选框只决定去字幕的位置。",'
if text.count(bad) != 1:
    raise RuntimeError(f'generated multiline Java string not found: {text.count(bad)}')
text = text.replace(bad, good, 1)
path.write_text(text, encoding='utf-8')
assert '\\n\\n导出始终保留完整视频画面' in text
print('V409_JAVA_STRING_ESCAPES_FIXED')
