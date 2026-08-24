# V4 Status and Delivery Rules

## Status query

Status path: `.gpt-media-status/{request_id}.json`.

Custom GPT uses `getMediaStatus` with:

- `path=.gpt-media-status/{request_id}.json`
- `sha=main`
- `per_page=1`

Read the latest commit message, remove the `GPT_MEDIA_STATUS ` prefix, then parse the remaining JSON.

Status queries are read-only and may be safely repeated.

## Status semantics

- `in_progress`: keep querying the same request_id; never resubmit.
- `success`: generation succeeded; inspect delivery fields.
- `failed`: report the failure; do not auto-regenerate.
- `request_id_conflict`: the ID belongs to different content; do not retry.
- `duplicate_or_uncertain_blocked`: do not retry.
- `provider_uncertain`: provider submission/result is uncertain; do not retry.
- `reused_result=true`: existing output was reused.

For batch tasks, `status=failed` can still include an Artifact containing successful segments and `manifest.json`.

## Delivery

Delivery operations never call the provider again.

Preferred semantics:

- `direct_url/direct_urls`: convenient verified signed links; may expire.
- `artifact_id`: GitHub Actions Artifact ZIP backup.
- Google Drive: optional second-stage user delivery when a user-connected Drive write tool is actually available.

A delivery problem is not a generation problem. Never regenerate media because a link expired, a ZIP could not be opened, or Drive upload failed.
