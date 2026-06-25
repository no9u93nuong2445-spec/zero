#!/usr/bin/env python3
from pathlib import Path

JAVA = Path('jhmin/app/src/main/java/com/bianzhifeng/jinghua')


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected 1 occurrence, got {count}: {old[:100]!r}')
    path.write_text(text.replace(old, new, 1), encoding='utf-8')


home = JAVA / 'HomeActivity.java'
main = JAVA / 'MainActivity.java'

# Avoid rebuilding the simple home twice during its first lifecycle pass. This
# reduces startup work on slower Android 15 devices/emulators.
replace_once(
    home,
    '''    private boolean appliedDarkTheme;
    private boolean created;''',
    '''    private boolean appliedDarkTheme;
    private boolean created;
    private boolean firstResume = true;''',
)
replace_once(
    home,
    '''        if (!created) return;
        boolean currentDark = safeDarkMode();''',
    '''        if (!created) return;
        if (firstResume) {
            firstResume = false;
            return;
        }
        boolean currentDark = safeDarkMode();''',
)
replace_once(
    home,
    '''            setRootContent(buildContent());''',
    '''            setRootContent(buildContent());
            android.util.Log.i("JingHuaUi", "HOME_SIMPLE_READY");''',
)

# Runtime signals let Android 15 validation observe the app directly without
# depending on UiAutomator/SystemUI stability.
replace_once(
    main,
    '''        Intent primary;
        if (Build.VERSION.SDK_INT >= 33) {''',
    '''        android.util.Log.i("JingHuaUi", "PICKER_REQUESTED sdk=" + Build.VERSION.SDK_INT);
        Intent primary;
        if (Build.VERSION.SDK_INT >= 33) {''',
)
replace_once(
    main,
    '''        page.setContentDescription("净画简单编辑器：选择视频、预览去字幕、导出完整视频");
        page.setPadding(dp(18), dp(18), dp(18), dp(34));''',
    '''        page.setContentDescription("净画简单编辑器：选择视频、预览去字幕、导出完整视频");
        android.util.Log.i("JingHuaUi", "EDITOR_SIMPLE_READY");
        page.setPadding(dp(18), dp(18), dp(18), dp(34));''',
)

# A tiny MP4 is generated into res/raw before compilation. This route is enabled
# only when Android marks the installed package debuggable; release builds ignore
# the hidden smoke-test extra even though this old project does not generate a
# BuildConfig class.
old_flow = '''            String requestedProject = getIntent().getStringExtra(EXTRA_PROJECT_ID);
            boolean pickOnStart = getIntent().getBooleanExtra(EXTRA_PICK_ON_START, false);
            if (requestedProject != null && !requestedProject.isEmpty()) {
                mainHandler.postDelayed(() -> openProjectById(requestedProject), 180);
            } else if (pickOnStart) {
                mainHandler.postDelayed(this::openVideoPicker, 220);
            } else {
                mainHandler.postDelayed(this::offerDraftRestore, 250);
            }'''
new_flow = '''            String requestedProject = getIntent().getStringExtra(EXTRA_PROJECT_ID);
            boolean pickOnStart = getIntent().getBooleanExtra(EXTRA_PICK_ON_START, false);
            boolean debuggable = (getApplicationInfo().flags
                    & android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE) != 0;
            boolean uiSmoke = debuggable
                    && getIntent().getBooleanExtra("jinghua_ui_smoke", false);
            if (uiSmoke) {
                Uri smokeUri = Uri.parse(
                        "android.resource://" + getPackageName() + "/raw/v410_ui_smoke");
                mainHandler.postDelayed(() -> loadPreviewVideo(smokeUri), 350);
            } else if (requestedProject != null && !requestedProject.isEmpty()) {
                mainHandler.postDelayed(() -> openProjectById(requestedProject), 180);
            } else if (pickOnStart) {
                mainHandler.postDelayed(this::openVideoPicker, 220);
            } else {
                mainHandler.postDelayed(this::offerDraftRestore, 250);
            }'''
replace_once(main, old_flow, new_flow)

home_text = home.read_text(encoding='utf-8')
main_text = main.read_text(encoding='utf-8')
assert 'firstResume = true' in home_text
assert 'HOME_SIMPLE_READY' in home_text
assert 'PICKER_REQUESTED' in main_text
assert 'EDITOR_SIMPLE_READY' in main_text
assert 'FLAG_DEBUGGABLE' in main_text
assert 'jinghua_ui_smoke' in main_text
assert 'android.resource://' in main_text
assert 'BuildConfig.DEBUG' not in main_text
print('V410_ANDROID15_RUNTIME_HARDENING_APPLIED')
