# JingHua V4.0.4 APK recovery

A complete V4.0.4 debug APK has been recovered locally and is being used as the upgrade base.

Verified recovery facts:

- package: `com.bianzhifeng.jinghua`
- four DEX files retained
- 200 readable project classes found in `classes3.dex`
- Android resources and custom GLSL repair shaders retained
- direct V4.0.6 recovery test APK built by replacing only the repair shader and version metadata

The direct APK patch is a test build, not yet a full reconstructed Gradle project. The decompiled Java tree is retained as reference source and still requires manual compiler-error repair before it can replace the original lost source.
