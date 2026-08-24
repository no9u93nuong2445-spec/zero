# Video Workflow Rules

## Video Tasks

Supported modes:

- text_to_video
- image_to_video
- reference_to_video
- batch_video

Default policy:

- Use the configured default video provider unless user requests another model.
- Preserve reference image relationships.
- Batch tasks are one logical request with multiple segments.
- A failed segment does not automatically trigger regeneration.
