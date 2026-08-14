from pathlib import Path
import sys

if len(sys.argv) != 3:
    raise SystemExit('usage: filter_patch.py INPUT OUTPUT')

src = Path(sys.argv[1]).read_text(encoding='utf-8')
exclude = {'Services/DolaProtocolInspector.cs', 'Tests/verify_project.py'}
sections = []
current = []
for line in src.splitlines(keepends=True):
    if line.startswith('diff --git '):
        if current:
            sections.append(''.join(current))
        current = [line]
    else:
        current.append(line)
if current:
    sections.append(''.join(current))

kept = []
for section in sections:
    first = section.splitlines()[0]
    path = first.split(' b/', 1)[1]
    if path not in exclude:
        kept.append(section)

out = ''.join(kept)
out = out.replace(
    '+            MessageBox.Show("当前 Dola 会话还没有捕获到服务端明确的15秒能力证据。\n+\n+请先正常生成一次10秒视频，然后点击“重新扫描”。软件会学习当前真实协议；只有服务端明确返回支持15秒时才会解锁。", "Dola 15秒尚未解锁", MessageBoxButton.OK, MessageBoxImage.Information);',
    '+            MessageBox.Show("当前 Dola 会话还没有捕获到服务端明确的15秒能力证据。\\n\\n请先正常生成一次10秒视频，然后点击“重新扫描”。软件会学习当前真实协议；只有服务端明确返回支持15秒时才会解锁。", "Dola 15秒尚未解锁", MessageBoxButton.OK, MessageBoxImage.Information);'
)
out = out.replace(
    '+            MessageBox.Show("已重新扫描当前 Dola 页面，但服务端还没有明确暴露15秒能力。\n+\n+页面最大仍显示10秒本身不代表故障；软件不会修改网页滑块。只有真实网络响应明确支持15秒后，15秒模式才会开启。", "Dola 15秒检测", MessageBoxButton.OK, MessageBoxImage.Information);',
    '+            MessageBox.Show("已重新扫描当前 Dola 页面，但服务端还没有明确暴露15秒能力。\\n\\n页面最大仍显示10秒本身不代表故障；软件不会修改网页滑块。只有真实网络响应明确支持15秒后，15秒模式才会开启。", "Dola 15秒检测", MessageBoxButton.OK, MessageBoxImage.Information);'
)

Path(sys.argv[2]).write_text(out, encoding='utf-8', newline='')
print(f'filtered patch: kept={len(kept)} excluded={len(sections)-len(kept)} string_fix={"\\n\\n" in out}')
