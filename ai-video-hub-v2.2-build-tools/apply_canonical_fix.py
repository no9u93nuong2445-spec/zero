from pathlib import Path
import hashlib

root = Path('src/AI-Video-Hub-v2.2')
expected = {
    root / 'Services' / 'DoubaoProtocolInspector.cs': '5669af13a1628f16ee477f61bfb9d169ff4428e016fbacf5b17ef743552a74b3',
    root / 'MainWindow.xaml.cs': 'c8afa07236daa4ee7a91a7e4b22ba6c4567c581e7d482874e6a43a411f254d24',
}

def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()

if all(path.exists() and sha(path) == want for path, want in expected.items()):
    print('canonical compile fix already applied')
    raise SystemExit(0)

p = root / 'Services' / 'DoubaoProtocolInspector.cs'
s = p.read_text(encoding='utf-8')
old = '    private static readonly Regex UrlRegex = new(@"https?://[^\\s\\"\'<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);'
new = '    private static readonly Regex UrlRegex = new("https?://[^\\\\s\\\"\'<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);'
if old not in s:
    raise SystemExit('DoubaoProtocolInspector UrlRegex source pattern not found')
s = s.replace(old, new, 1)
p.write_text(s, encoding='utf-8')

p = root / 'MainWindow.xaml.cs'
s = p.read_text(encoding='utf-8')
old1 = '''            MessageBox.Show("当前账号还没有捕获到服务端明确标记为 original/no_watermark 的视频资源。

2.2 不再把普通播放地址或图片地址冒充“无水印原片”。你仍可在列表里选择已确认的视频播放资源另存。", "未找到明确原片", MessageBoxButton.OK, MessageBoxImage.Information);'''
new1 = '            MessageBox.Show("当前账号还没有捕获到服务端明确标记为 original/no_watermark 的视频资源。\\n\\n2.2 不再把普通播放地址或图片地址冒充“无水印原片”。你仍可在列表里选择已确认的视频播放资源另存。", "未找到明确原片", MessageBoxButton.OK, MessageBoxImage.Information);'
if old1 not in s:
    raise SystemExit('MainWindow first multiline message pattern not found')
s = s.replace(old1, new1, 1)
old2 = '''            var c = MessageBox.Show("这个地址已确认是视频，但服务端没有把它标记为 original/no_watermark。保存后可能仍带豆包水印。

继续保存普通播放资源吗？", "普通播放资源", MessageBoxButton.YesNo, MessageBoxImage.Question);'''
new2 = '            var c = MessageBox.Show("这个地址已确认是视频，但服务端没有把它标记为 original/no_watermark。保存后可能仍带豆包水印。\\n\\n继续保存普通播放资源吗？", "普通播放资源", MessageBoxButton.YesNo, MessageBoxImage.Question);'
if old2 not in s:
    raise SystemExit('MainWindow second multiline message pattern not found')
s = s.replace(old2, new2, 1)
p.write_text(s, encoding='utf-8')

for path, want in expected.items():
    got = sha(path)
    print(path, got)
    if got != want:
        raise SystemExit(f'canonical SHA mismatch for {path}: {got} != {want}')
print('canonical compile fix applied and verified')
