#!/usr/bin/env python3
from pathlib import Path

path = Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/FunctionalExportActivity.java')
text = path.read_text(encoding='utf-8')


def method_bounds(source: str, signature: str) -> tuple[int, int]:
    start = source.index(signature)
    brace = source.index('{', start)
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == '{':
            depth += 1
        elif source[index] == '}':
            depth -= 1
            if depth == 0:
                return start, index + 1
    raise RuntimeError(f'unclosed method: {signature}')


text = text.replace(
    'import android.os.Bundle;\n',
    'import android.os.Bundle;\nimport android.os.Handler;\nimport android.os.Looper;\n',
    1,
)
text = text.replace(
    'import java.util.List;\n',
    'import java.util.List;\nimport java.util.concurrent.atomic.AtomicBoolean;\n',
    1,
)

field_anchor = '''    private File markerFile;
    private File outputFile;
    private String caseName;
    private RectF detectedRegion;
'''
field_replacement = '''    private File markerFile;
    private File outputFile;
    private File inputFile;
    private String caseName;
    private String modeName;
    private boolean removeAudio;
    private RectF detectedRegion;
    private final Handler watchdogHandler = new Handler(Looper.getMainLooper());
    private final AtomicBoolean finished = new AtomicBoolean(false);
    private long previousOutputLength;
    private int stableOutputTicks;
    private final Runnable outputWatchdog = new Runnable() {
        @Override
        public void run() {
            if (finished.get()) return;
            long length = outputFile != null && outputFile.isFile() ? outputFile.length() : 0L;
            if (length >= 4096L && length == previousOutputLength) {
                stableOutputTicks++;
            } else {
                stableOutputTicks = 0;
            }
            previousOutputLength = length;
            if (stableOutputTicks >= 3) {
                finishSuccess("watchdog");
                return;
            }
            watchdogHandler.postDelayed(this, 1000L);
        }
    };
'''
if text.count(field_anchor) != 1:
    raise RuntimeError('functional fields anchor missing')
text = text.replace(field_anchor, field_replacement, 1)

assignments = [
    (
        '''        String modeName = intent.getStringExtra("mode");
        boolean removeAudio = intent.getBooleanExtra("remove_audio", false);''',
        '''        modeName = intent.getStringExtra("mode");
        removeAudio = intent.getBooleanExtra("remove_audio", false);''',
    ),
    (
        '''        File input = new File(testDir, inputName);
        outputFile = new File(testDir, outputName);''',
        '''        inputFile = new File(testDir, inputName);
        outputFile = new File(testDir, outputName);''',
    ),
    ('if (!input.isFile() || input.length() < 1024L) {',
     'if (!inputFile.isFile() || inputFile.length() < 1024L) {'),
    ('"input_missing_or_too_small:" + input.getAbsolutePath()',
     '"input_missing_or_too_small:" + inputFile.getAbsolutePath()'),
    ('detectedRegion = detectSubtitleRegion(input);',
     'detectedRegion = detectSubtitleRegion(inputFile);'),
    ('new MediaItem.Builder().setUri(Uri.fromFile(input)).build()',
     'new MediaItem.Builder().setUri(Uri.fromFile(inputFile)).build()'),
]
for old, new in assignments:
    if text.count(old) != 1:
        raise RuntimeError(f'functional assignment anchor missing: {old[:80]!r}')
    text = text.replace(old, new, 1)

# Replace onCompleted by method boundary instead of brittle whole-block text matching.
start, end = method_bounds(
    text,
    '                    public void onCompleted(Composition composition, ExportResult exportResult)',
)
text = text[:start] + '''                    public void onCompleted(Composition composition, ExportResult exportResult) {
                        finishSuccess("callback");
                    }''' + text[end:]

start_anchor = '''            transformer.start(edited, outputFile.getAbsolutePath());
        } catch (Throwable error) {'''
start_replacement = '''            transformer.start(edited, outputFile.getAbsolutePath());
            watchdogHandler.postDelayed(outputWatchdog, 1000L);
        } catch (Throwable error) {'''
if text.count(start_anchor) != 1:
    raise RuntimeError('transformer start anchor missing')
text = text.replace(start_anchor, start_replacement, 1)

finish_anchor = '    private void finishWithError(String message, Throwable error) {'
finish_success = r'''    private void finishSuccess(String source) {
        if (!finished.compareAndSet(false, true)) return;
        watchdogHandler.removeCallbacks(outputWatchdog);
        if (!outputFile.isFile() || outputFile.length() < 4096L) {
            finished.set(false);
            finishWithError("output_invalid_after_" + source, null);
            return;
        }
        MediaIntegrityValidator.Result integrity = MediaIntegrityValidator.validate(
                this, Uri.fromFile(inputFile), outputFile);
        if (!integrity.valid) {
            finished.set(false);
            finishWithError("integrity_failed:" + integrity.message, null);
            return;
        }
        String result = "PASS\ncase=" + caseName
                + "\nbytes=" + outputFile.length()
                + "\naudio_removed=" + removeAudio
                + "\nmode=" + String.valueOf(modeName)
                + "\ncompletion_source=" + source
                + "\nregion=" + regionText(detectedRegion);
        writeMarker(result);
        if (statusView != null) {
            statusView.setText("PASS " + caseName + ": " + outputFile.length() + " bytes");
        }
    }

'''
if text.count(finish_anchor) != 1:
    raise RuntimeError('finishWithError anchor missing')
text = text.replace(finish_anchor, finish_success + finish_anchor, 1)

error_anchor = '''    private void finishWithError(String message, Throwable error) {
        StringBuilder detail = new StringBuilder();'''
error_replacement = '''    private void finishWithError(String message, Throwable error) {
        if (!finished.compareAndSet(false, true)) return;
        watchdogHandler.removeCallbacks(outputWatchdog);
        StringBuilder detail = new StringBuilder();'''
if text.count(error_anchor) != 1:
    raise RuntimeError('finishWithError body anchor missing')
text = text.replace(error_anchor, error_replacement, 1)

destroy_anchor = '''    protected void onDestroy() {
        if (transformer != null) {'''
destroy_replacement = '''    protected void onDestroy() {
        watchdogHandler.removeCallbacks(outputWatchdog);
        if (transformer != null) {'''
if text.count(destroy_anchor) != 1:
    raise RuntimeError('onDestroy anchor missing')
text = text.replace(destroy_anchor, destroy_replacement, 1)

path.write_text(text, encoding='utf-8')
assert 'String result = "PASS\\ncase="' in text
assert 'completion_source=' in text
assert 'MediaIntegrityValidator.validate' in text
assert 'outputWatchdog' in text
print('V410_FUNCTIONAL_ACTIVITY_HARDENED')
