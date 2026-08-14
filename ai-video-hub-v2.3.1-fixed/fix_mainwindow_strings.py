from pathlib import Path
import sys

if len(sys.argv) != 2:
    raise SystemExit('usage: fix_mainwindow_strings.py MAINWINDOW_XAML_CS')

p = Path(sys.argv[1])
s = p.read_text(encoding='utf-8')

old1 = 'MessageBox.Show("当前 Dola 会话还没有捕获到服务端明确的15秒能力证据。\n\n请先正常生成一次10秒视频，然后点击“重新扫描”。软件会学习当前真实协议；只有服务端明确返回支持15秒时才会解锁。",'
new1 = 'MessageBox.Show("当前 Dola 会话还没有捕获到服务端明确的15秒能力证据。\\n\\n请先正常生成一次10秒视频，然后点击“重新扫描”。软件会学习当前真实协议；只有服务端明确返回支持15秒时才会解锁。",'
old2 = 'MessageBox.Show("已重新扫描当前 Dola 页面，但服务端还没有明确暴露15秒能力。\n\n页面最大仍显示10秒本身不代表故障；软件不会修改网页滑块。只有真实网络响应明确支持15秒后，15秒模式才会开启。",'
new2 = 'MessageBox.Show("已重新扫描当前 Dola 页面，但服务端还没有明确暴露15秒能力。\\n\\n页面最大仍显示10秒本身不代表故障；软件不会修改网页滑块。只有真实网络响应明确支持15秒后，15秒模式才会开启。",'

count1 = s.count(old1)
count2 = s.count(old2)
if count1 != 1 or count2 != 1:
    raise SystemExit(f'expected exactly one occurrence of each multiline string, got first={count1}, second={count2}')

s = s.replace(old1, new1).replace(old2, new2)
p.write_text(s, encoding='utf-8', newline='')

if old1 in s or old2 in s:
    raise SystemExit('multiline string repair incomplete')
if '\\n\\n' not in s:
    raise SystemExit('escaped newline markers missing after repair')
print('MainWindow multiline string repair: PASS')
