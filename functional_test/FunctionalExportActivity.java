package com.bianzhifeng.jinghua;

import android.app.Activity;
import android.graphics.RectF;
import android.net.Uri;
import android.os.Bundle;
import android.widget.TextView;
import androidx.media3.common.MediaItem;
import androidx.media3.common.MimeTypes;
import androidx.media3.transformer.Composition;
import androidx.media3.transformer.EditedMediaItem;
import androidx.media3.transformer.Effects;
import androidx.media3.transformer.ExportException;
import androidx.media3.transformer.ExportResult;
import androidx.media3.transformer.Transformer;
import java.io.File;
import java.io.FileWriter;
import java.io.IOException;
import java.util.Collections;

/** CI-only end-to-end export harness. Not included in user-facing release builds. */
public final class FunctionalExportActivity extends Activity {
    private Transformer transformer;
    private TextView statusView;
    private File testDir;
    private File markerFile;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        statusView = new TextView(this);
        statusView.setTextSize(18f);
        statusView.setPadding(32, 64, 32, 32);
        statusView.setText("Preparing functional export test…");
        setContentView(statusView);

        File external = getExternalFilesDir(null);
        if (external == null) {
            finishWithError("external_files_dir_unavailable");
            return;
        }
        testDir = new File(external, "functional-test");
        if (!testDir.exists() && !testDir.mkdirs()) {
            finishWithError("cannot_create_test_dir");
            return;
        }
        markerFile = new File(testDir, "result.txt");
        File input = new File(testDir, "input.mp4");
        File output = new File(testDir, "output.mp4");
        if (markerFile.exists()) markerFile.delete();
        if (output.exists()) output.delete();
        if (!input.isFile() || input.length() < 1024L) {
            finishWithError("input_missing_or_too_small");
            return;
        }

        long durationMs = 4200L;
        RectF subtitleRegion = new RectF(0.06f, 0.76f, 0.94f, 0.93f);
        RegionTrack repairTrack = RegionTrack.fixed(
                subtitleRegion,
                durationMs,
                RegionEffect.MODE_REPAIR_HQ,
                0.72f);
        RegionEffect effect = new RegionEffect(
                Collections.singletonList(repairTrack),
                null,
                true,
                true);

        MediaItem item = new MediaItem.Builder().setUri(Uri.fromFile(input)).build();
        EditedMediaItem edited = new EditedMediaItem.Builder(item)
                .setEffects(new Effects(
                        Collections.emptyList(),
                        Collections.singletonList(effect)))
                .build();

        transformer = new Transformer.Builder(this)
                .setVideoMimeType(MimeTypes.VIDEO_H264)
                .setAudioMimeType(MimeTypes.AUDIO_AAC)
                .setMaxDelayBetweenMuxerSamplesMs(30_000L)
                .setUsePlatformDiagnostics(false)
                .addListener(new Transformer.Listener() {
                    @Override
                    public void onCompleted(Composition composition, ExportResult exportResult) {
                        if (!output.isFile() || output.length() < 1024L) {
                            finishWithError("completed_but_output_invalid");
                            return;
                        }
                        writeMarker("PASS\nbytes=" + output.length());
                        statusView.setText("PASS: output bytes=" + output.length());
                    }

                    @Override
                    public void onError(
                            Composition composition,
                            ExportResult exportResult,
                            ExportException exportException) {
                        finishWithError("transformer_error:" + summarize(exportException));
                    }
                })
                .build();

        statusView.setText("Exporting subtitle-repair test video…");
        try {
            transformer.start(edited, output.getAbsolutePath());
        } catch (Throwable error) {
            finishWithError("start_error:" + summarize(error));
        }
    }

    private void finishWithError(String message) {
        writeMarker("FAIL\n" + message);
        if (statusView != null) statusView.setText("FAIL: " + message);
    }

    private void writeMarker(String text) {
        if (markerFile == null) return;
        try (FileWriter writer = new FileWriter(markerFile, false)) {
            writer.write(text == null ? "" : text);
            writer.flush();
        } catch (IOException ignored) {
        }
    }

    private static String summarize(Throwable error) {
        if (error == null) return "unknown";
        String message = error.getMessage();
        return error.getClass().getSimpleName() + ":" + (message == null ? "" : message.replace('\n', ' '));
    }

    @Override
    protected void onDestroy() {
        if (transformer != null) {
            try {
                transformer.cancel();
            } catch (Throwable ignored) {
            }
        }
        super.onDestroy();
    }
}
