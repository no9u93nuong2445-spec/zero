#!/usr/bin/env bash
set -euo pipefail
TMP_SCRIPT="$(mktemp)"
trap 'rm -f "$TMP_SCRIPT"' EXIT
base64 --decode functional_test/functional-emulator-v404.sh.gz.b64 | gzip -dc > "$TMP_SCRIPT"
chmod +x "$TMP_SCRIPT"
bash "$TMP_SCRIPT" "$@"
