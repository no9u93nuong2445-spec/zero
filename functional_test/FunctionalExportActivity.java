package com.bianzhifeng.jinghua;

import android.app.Activity;
import android.content.Intent;
import android.graphics.RectF;
import android.net.Uri;
import android.os.Bundle;
import android.widget.TextView;
import androidx.media3.common.Effect;
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
import java.io.PrintWriter;
import java.io.StringWriter;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/** CI-only end-to-end export harness. Not included in user-facing release builds. */
public final class FunctionalExportActivity extends Activity {
    private Transformer transformer;
    private TextView statusView;
    private File markerFile;
    private File outputFile;
    private String caseName;

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
            finishWithError("external_files_dir_unavailable", null);
            return;
        }
        File testDir = new File(external, "functional-test");
        if (!testDir.exists() && !testDir.mkdirs()) {
            finishWithError("cannot_create_test_dir", null);
            return;
        }

        Intent intent = getIntent();
        caseName = safeName(intent.getStringExtra("case_name"), "default");
        String inputName = safeName(intent.getStringExtra("input_name"), "input.mp4");
        String outputName = safeName(intent.getStringExtra("output_name"), "output.mp4");
        String markerName = safeName(intent.getStringExtra("marker_name"), "result.txt");
        String modeName = intent.getStringExtra("mode");
        boolean removeAudio = intent.getBooleanExtra("remove_audio", false);

        File input = new File(testDir, inputName);
        outputFile = new File(testDir, outputName);
        markerFile = new File(testDir, markerName);
        if (markerFile.exists()) markerFile.delete();
        if (outputFile.exists()) outputFile.delete();
        if (!input.isFile() || input.length() < 1024L) {
            finishWithError("input_missing_or_too_small:" + input.getAbsolutePath(), null);
            return;
        }

        MediaItem item = new MediaItem.Builder().setUri(Uri.fromFile(input)).build();
        EditedMediaItem.Builder editedBuilder = new EditedMediaItem.Builder(item)
                .setRemoveAudio(removeAudio);

        List<Effect> videoEffects = createVideoEffects(modeName);
        if (!videoEffects.isEmpty()) {
            editedBuilder.setEffects(new Effects(Collections.emptyList(), videoEffects));
        }
        EditedMediaItem edited = editedBuilder.build();

        transformer = new Transformer.Builder(this)
                .setVideoMimeType(MimeTypes.VIDEO_H264)
                .setAudioMimeType(MimeTypes.AUDIO_AAC)
                .setMaxDelayBetweenMuxerSamplesMs(30_000L)
                .setUsePlatformDiagnostics(false)
                .addListener(new Transformer.Listener() {
                    @Override
                    public void onCompleted(Composition composition, ExportResult exportResult) {
                        if (!outputFile.isFile() || outputFile.length() < 1024L) {
                            finishWithError("completed_but_output_invalid", null);
                            return;
                        }
                        String result = "PASS\ncase=" + caseName
                                + "\nbytes=" + outputFile.length()
                                + "\naudio_removed=" + removeAudio
                                + "\nmode=" + String.valueOf(modeName);
                        writeMarker(result);
                        statusView.setText("PASS " + caseName + ": " + outputFile.length() + " bytes");
                    }

                    @Override
                    public void onError(
                            Composition composition,
                            ExportResult exportResult,
                            ExportException exportException) {
                        finishWithError("transformer_error", exportException);
                    }
                })
                .build();

        statusView.setText("Exporting case: " + caseName);
        try {
            transformer.start(edited, outputFile.getAbsolutePath());
        } catch (Throwable error) {
            finishWithError("start_error", error);
        }
    }

    private List<Effect> createVideoEffects(String modeName) {
        if (modeName == null || modeName.isEmpty() || "none".equals(modeName)) {
            return Collections.emptyList();
        }
        int mode;
        float strength;
        boolean temporal;
        if ("blur".equals(modeName)) {
            mode = RegionEffect.MODE_BLUR;
            strength = 0.72f;
            temporal = false;
        } else if ("repair_fast".equals(modeName)) {
            mode = RegionEffect.MODE_REPAIR_FAST;
            strength = 0.72f;
            temporal = false;
        } else {
            mode = RegionEffect.MODE_REPAIR_HQ;
            strength = 0.72f;
            temporal = true;
        }

        long durationMs = 4200L;
        RectF subtitleRegion = new RectF(0.08f, 0.76f, 0.92f, 0.93f);
        RegionTrack track = RegionTrack.fixed(subtitleRegion, durationMs, mode, strength);
        RegionEffect effect = new RegionEffect(
                Collections.singletonList(track),
                null,
                temporal,
                temporal);
        List<Effect> effects = new ArrayList<>();
        effects.add(effect);
        return effects;
    }

    private void finishWithError(String message, Throwable error) {
        StringBuilder detail = new StringBuilder();
        detail.append("FAIL\ncase=").append(caseName == null ? "unknown" : caseName)
                .append("\nreason=").append(message == null ? "unknown" : message);
        if (error != null) {
            detail.append("\nexception=").append(error.getClass().getName())
                    .append("\nmessage=").append(String.valueOf(error.getMessage()));
            if (error instanceof ExportException) {
                detail.append("\nerror_code=").append(((ExportException) error).errorCode);
            }
            Throwable cause = error.getCause();
            int depth = 0;
            while (cause != null && depth < 8) {
                detail.append("\ncause_").append(depth).append('=')
                        .append(cause.getClass().getName()).append(':')
                        .append(String.valueOf(cause.getMessage()));
                cause = cause.getCause();
                depth++;
            }
            StringWriter stack = new StringWriter();
            error.printStackTrace(new PrintWriter(stack));
            detail.append("\nstack=\n").append(stack.toString());
        }
        writeMarker(detail.toString());
        if (statusView != null) statusView.setText("FAIL " + caseName + ": " + message);
    }

    private void writeMarker(String text) {
        if (markerFile == null) return;
        try (FileWriter writer = new FileWriter(markerFile, false)) {
            writer.write(text == null ? "" : text);
            writer.flush();
        } catch (IOException ignored) {
        }
    }

    private static String safeName(String value, String fallback) {
        if (value == null || value.trim().isEmpty()) return fallback;
        String clean = value.replaceAll("[^A-Za-z0-9._-]", "_");
        return clean.isEmpty() ? fallback : clean;
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
