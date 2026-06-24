from pathlib import Path

path = Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/MainActivity.java')
text = path.read_text(encoding='utf-8')

constant_anchor = '    public static final String EXTRA_TEMPLATE_INDEX = "template_index";\n'
constant_new = constant_anchor + '    public static final String EXTRA_DIRECT_VIDEO_URI = "direct_video_uri";\n'
if text.count(constant_anchor) != 1:
    raise RuntimeError('direct URI constant anchor missing')
text = text.replace(constant_anchor, constant_new, 1)

anchor = '''            String requestedProject = getIntent().getStringExtra(EXTRA_PROJECT_ID);
            boolean pickOnStart = getIntent().getBooleanExtra(EXTRA_PICK_ON_START, false);'''
replacement = '''            String directVideo = getIntent().getStringExtra(EXTRA_DIRECT_VIDEO_URI);
            Uri directUri = directVideo == null || directVideo.isEmpty()
                    ? getIntent().getData()
                    : Uri.parse(directVideo);
            if (directUri != null) {
                inputUris.clear();
                inputUris.add(directUri);
                loadPreviewVideo(directUri);
                updateExportEstimate();
                updateCloudUi();
                return;
            }
            String requestedProject = getIntent().getStringExtra(EXTRA_PROJECT_ID);
            boolean pickOnStart = getIntent().getBooleanExtra(EXTRA_PICK_ON_START, false);'''
if text.count(anchor) != 1:
    raise RuntimeError('onCreate direct URI anchor missing')
text = text.replace(anchor, replacement, 1)
path.write_text(text, encoding='utf-8')

assert 'EXTRA_DIRECT_VIDEO_URI' in text
assert 'getIntent().getData()' in text
print('V409_DIRECT_VIDEO_INTENT_APPLIED')
