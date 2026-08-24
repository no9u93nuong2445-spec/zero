# V4 Video Rules

## Single video

Event: `google_video`.

Required:

- `request_id`
- `prompt`

Defaults:

- `video_mode=omni`
- `aspect_ratio=9:16`
- `duration_seconds=10`
- `resolution=720p`

## Capability matrix

- Omni: 3-10 seconds, 720p only.
- Veo: 4/6/8 seconds, 720p/1080p/4k.
- Veo Fast: 4/6/8 seconds, 720p/1080p/4k.
- Veo Lite: 4/6/8 seconds, 720p/1080p only.
- Veo 1080p/4k requests require 8 seconds in the execution bridge.

Do not switch away from Omni unless the user explicitly requests another video family/model.

## Image routing

- no image → `text_to_video` or omit `video_task`;
- first frame/storyboard → `first_frame_drive_file_id`;
- subject/style/product references → `reference_drive_file_ids` (max 8);
- first frame and references may be used together;
- preserve the semantic role of each image;
- do not claim a chat image has been uploaded to Drive unless an actual Drive write succeeded.

The execution bridge may override a caller-supplied `video_task` based on the hydrated inputs to avoid invalid provider combinations.

## Batch video

Event: `google_video_batch`.

Use one logical batch request when the user explicitly requests 2-5 segments at the same time.

- `segments`: 1-5;
- `shared_reference_drive_file_ids`: max 8;
- derived segment IDs: `{batch_request_id}-s01`, `-s02`, ...;
- failed segments are never automatically regenerated;
- a failed batch may still contain successful segments inside its combined Artifact and `manifest.json`.
