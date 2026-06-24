#!/usr/bin/env bash
set -u
mkdir -p e2e-results functional-results

functional_code=0
ui_code=0
bash ci/v407-functional-emulator.sh || functional_code=$?
echo "$functional_code" > e2e-results/functional-emulator-exit.txt

bash ci/v407-ui-e2e.sh || ui_code=$?
echo "$ui_code" > e2e-results/ui-emulator-exit.txt

printf 'functional=%s\nui=%s\n' "$functional_code" "$ui_code" \
  > e2e-results/emulator-stage-summary.txt

# Always let the workflow collect evidence and run the analyzers. The final
# report, rather than shell short-circuiting, decides pass/fail.
exit 0
