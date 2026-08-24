# JingHua V4.0.7 APK recovery status

V4.0.7 is built from the user's complete V4.0.4 debug APK by a minimal four-file patch and the same recovery signing certificate used for V4.0.6.

## APK identity

- package: `com.bianzhifeng.jinghua`
- versionName: `4.0.7`
- versionCode: `13`
- minSdk: 29
- targetSdk: 35
- APK SHA-256: `f79b45ce6f049edb28eb320962bbf44fa37c642c3d34b9db169b088370aecd17`

## Minimal changed files

Only these application entries changed from V4.0.4:

- `AndroidManifest.xml`
- `classes3.dex`
- `resources.arsc`
- `res/raw/fragment_region_es2.glsl`

The other 41 common ZIP entries remain byte-identical.

## V4.0.7 repair changes

- stronger thick-glyph interior confidence
- near/far boundary consistency and scale-drift direction scoring
- chroma-safe YCbCr repair merge
- guarded temporal history fallback
- exact zero edits outside the selected repair region in the 126-frame GLES2 regression

## 126-frame GLES2 regression

- V4.0.6 ROI MAE: 33.673
- V4.0.7 ROI MAE: 33.540
- V4.0.6 subtitle-pixel MAE: 90.751
- V4.0.7 subtitle-pixel MAE: 90.237
- V4.0.7 outside-region change: 0.000
- clean-frame ROI false-positive MAE: 0.376

The fragment shader compiled and linked with a real EGL/GLES2 context. APK v2 RSA signature and chunked content digest verification both passed.

## Honest limitation

This remains an APK recovery build rather than a fully reconstructed Gradle project. Physical Android installation, MediaCodec export, audio, orientation, gallery saving, and long-video tests still require device validation. The private recovery keystore must not be committed to this repository.
