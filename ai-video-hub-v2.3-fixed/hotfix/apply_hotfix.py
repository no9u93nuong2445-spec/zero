from pathlib import Path
import base64
import gzip
import hashlib
import subprocess
import sys

EXPECTED_SHA = "2d31bfb5490e1f10c9c3407ed6aa4f77ff8803cebf06dd051916515089a82c45"

project = Path(sys.argv[1]).resolve()
script = Path(__file__).resolve()
repo = script.parents[2]
xaml = project / "MainWindow.xaml"
models = project / "Models" / "DolaModels.cs"

xaml_text = xaml.read_text(encoding="utf-8") if xaml.exists() else ""
models_text = models.read_text(encoding="utf-8") if models.exists() else ""

# Build/publish may invoke PrepareForBuild more than once. Make this idempotent.
if "2.3.0 · Dola Native Bridge" in xaml_text and "using AI.VideoHub.Services;" in models_text:
    print("V2.3 final Dola hotfix already applied")
    raise SystemExit(0)

b64_path = script.parent / "v23-hotfix.patch.gz.b64"
b64 = b64_path.read_text(encoding="ascii").strip()
raw = gzip.decompress(base64.b64decode(b64))
actual = hashlib.sha256(raw).hexdigest()
print(f"V2.3 final hotfix sha256={actual}")
if actual != EXPECTED_SHA:
    raise SystemExit("V2.3 final hotfix SHA mismatch")

patch = repo / "v23-final-hotfix.patch"
patch.write_bytes(raw)
relative_project = project.relative_to(repo).as_posix()

subprocess.check_call(
    ["git", "apply", "--check", "--unsafe-paths", f"--directory={relative_project}", str(patch)],
    cwd=repo,
)
subprocess.check_call(
    ["git", "apply", "--unsafe-paths", f"--directory={relative_project}", str(patch)],
    cwd=repo,
)

xaml_text = xaml.read_text(encoding="utf-8")
models_text = models.read_text(encoding="utf-8")
if "2.3.0 · Dola Native Bridge" not in xaml_text:
    raise SystemExit("V2.3 Dola UI marker missing after hotfix")
if "using AI.VideoHub.Services;" not in models_text:
    raise SystemExit("V2.3 ProtocolPatchResult namespace fix missing after hotfix")

print("V2.3 final Dola UI/compile hotfix applied and verified")
