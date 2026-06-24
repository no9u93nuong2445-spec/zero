#!/usr/bin/env python3
from pathlib import Path

path = Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua/FunctionalExportActivity.java')
text = path.read_text(encoding='utf-8')

text = text.replace('import android.os.Bundle;\n', 'import android.os.Bundle;\nimport android.os.Handler;\nimport android.os.Looper;\n', 1)
text = text.replace('import java.util.List;\n', 'import java.util.List;\nimport java.util.concurrent.atomic.AtomicBoolean;\n', 1)

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

text = text.replace('''        String modeName = intent.getStringExtra("mode");
        boolean removeAudio = intent.getBooleanExtra("remove_audio", false);''',
'''        modeName = intent.getStringExtra("mode");
        removeAudio = intent.getBooleanExtra("remove_audio", false);''', 1)
text = text.replace('''        File input = new File(testDir, inputName);
        outputFile = new File(testDir, outputName);''',
'''        inputFile = new File(testDir, inputName);
        outputFile = new File(testDir, outputName);''', 1)
text = text.replace('if (!input.isFile() || input.length() < 1024L) {',
                    'if (!inputFile.isFile() || inputFile.length() < 1024L) {', 1)
text = text.replace('"input_missing_or_too_small:" + input.getAbsolutePath()',
                    '"input_missing_or_too_small:" + inputFile.getAbsolutePath()', 1)
text = text.replace('detectedRegion = detectSubtitleRegion(input);',
                    'detectedRegion = detectSubtitleRegion(inputFile);', 1)
text = text.replace('new MediaItem.Builder().setUri(Uri.fromFile(input)).build()',
                    'new MediaItem.Builder().setUri(Uri.fromFile(inputFile)).build()', 1)

callback_old = '''                    public void onCompleted(Composition composition, ExportResult exportResult) {
                        if (!outputFile.isFile() || outputFile.length() < 1024L) {
                            finishWithError("completed_but_output_invalid", null);
                            return;
                        }
                        String result = "PASS\ncase=" + caseName
                                + "\nbytes=" + outputFile.length()
                                + "\naudio_removed=" + removeAudio
                                + "\nmode=" + String.valueOf(modeName)
                                + "\nregion=" + regionText(detectedRegion);
                        writeMarker(result);
                        statusView.setText("PASS " + caseName + ": " + outputFile.length() + " bytes");
                    }'''
callback_new = '''                    public void onCompleted(Composition composition, ExportResult exportResult) {
                        finishSuccess("callback");
                    }'''
if text.count(callback_old) != 1:
    raise RuntimeError('functional onCompleted block missing')
text = text.replace(callback_old, callback_new, 1)

text = text.replace('''            transformer.start(edited, outputFile.getAbsolutePath());
        } catch (Throwable error) {''',
'''            transformer.start(edited, outputFile.getAbsolutePath());
            watchdogHandler.postDelayed(outputWatchdog, 1000L);
        } catch (Throwable error) {''', 1)

finish_anchor = '    private void finishWithError(String message, Throwable error) {'
finish_success = '''    private void finishSuccess(String source) {
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
text = text.replace('''    private void finishWithError(String message, Throwable error) {
        StringBuilder detail = new StringBuilder();''',
'''    private void finishWithError(String message, Throwable error) {
        if (!finished.compareAndSet(false, true)) return;
        watchdogHandler.removeCallbacks(outputWatchdog);
        StringBuilder detail = new StringBuilder();''', 1)
text = text.replace('''    protected void onDestroy() {
        if (transformer != null) {''',
'''    protected void onDestroy() {
        watchdogHandler.removeCallbacks(outputWatchdog);
        if (transformer != null) {''', 1)

path.write_text(text, encoding='utf-8')
assert 'completion_source=' in text
assert 'MediaIntegrityValidator.validate' in text
assert 'outputWatchdog' in text
print('V410_FUNCTIONAL_ACTIVITY_HARDENED')
