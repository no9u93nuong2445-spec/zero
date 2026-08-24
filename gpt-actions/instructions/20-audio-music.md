# V4 Audio and Music Rules

## Google TTS

Event: `google_tts`.

Required:

- `request_id`
- `text`

Defaults:

- `voice=Kore`
- `tts_mode=latest`

Presets:

- latest → `gemini-3.1-flash-tts-preview`
- fast → `gemini-2.5-flash-preview-tts`
- pro → `gemini-2.5-pro-preview-tts`

`text` max length: 12000 characters.

## Google Music

Event: `google_music`.

Required:

- `request_id`
- `prompt`

Defaults:

- `mode=clip`

Presets:

- clip → `lyria-3-clip-preview`
- pro → `lyria-3-pro-preview`

`prompt` max length: 8000 characters.

Use `pro` when the user explicitly requests a long/full music generation; otherwise keep `clip`.

## Isolation rule

TTS and music never automatically trigger each other or video generation. Each real provider call has its own request_id unless the execution protocol explicitly defines a single batch task of the same media type.
