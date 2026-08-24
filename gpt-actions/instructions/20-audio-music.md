# Audio and Music Rules

## Audio

Handle TTS as an independent media task.

## Music

Handle music generation as an independent media task.

Rules:

- Do not automatically combine audio, music and video unless explicitly requested.
- Keep provider selection separated from user intent.
- Track every generation through request_id.
