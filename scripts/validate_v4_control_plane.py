#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ACTION = ROOT / "gpt-actions" / "media-v4.0.0.json"
ROUTER = ROOT / "router" / "media_router.json"
SCHEMA = ROOT / "schemas" / "media_task_schema.json"


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def fail(message: str) -> None:
    raise SystemExit(message)


def main() -> int:
    action = load(ACTION)
    router = load(ROUTER)
    task_schema = load(SCHEMA)

    if action.get("openapi") != "3.1.0":
        fail("Action must use OpenAPI 3.1.0")
    if not str((action.get("info") or {}).get("version") or "").startswith("4.0.0"):
        fail("Action version must be V4")
    if router.get("execution_repository") != "no9u93nuong2445-spec/doubao-tts-bridge":
        fail("Router execution repository drift")

    schemas = ((action.get("components") or {}).get("schemas") or {})
    required = {"TtsRequest", "MusicRequest", "VideoRequest", "VideoBatchRequest", "TtsPayload", "MusicPayload", "VideoPayload", "VideoBatchPayload"}
    missing = sorted(required - set(schemas))
    if missing:
        fail(f"Missing Action schemas: {missing}")

    action_events = {
        schemas["TtsRequest"]["properties"]["event_type"]["const"],
        schemas["MusicRequest"]["properties"]["event_type"]["const"],
        schemas["VideoRequest"]["properties"]["event_type"]["const"],
        schemas["VideoBatchRequest"]["properties"]["event_type"]["const"],
    }
    router_events = {row["event_type"] for row in (router.get("routes") or {}).values()}
    expected_events = {"google_tts", "google_music", "google_video", "google_video_batch"}
    if action_events != expected_events or router_events != expected_events:
        fail(f"event_type drift: action={action_events}, router={router_events}")

    music_prompt = schemas["MusicPayload"]["properties"]["prompt"]
    if music_prompt.get("maxLength") != 8000:
        fail("Music prompt limit must match runtime registry: 8000")

    tts_text = schemas["TtsPayload"]["properties"]["text"]
    if tts_text.get("maxLength") != 12000:
        fail("TTS text limit must be 12000")

    video = schemas["VideoPayload"]["properties"]
    video_defaults = router["routes"]["video_single"]["defaults"]
    for key in ("video_mode", "aspect_ratio", "duration_seconds", "resolution"):
        if video[key].get("default") != video_defaults[key]:
            fail(f"Video default drift for {key}")
    if video_defaults != {"video_mode": "omni", "aspect_ratio": "9:16", "duration_seconds": 10, "resolution": "720p"}:
        fail("Unexpected default video policy")

    refs = schemas["ReferenceDriveFileIds"]
    if refs.get("maxItems") != router["routes"]["video_single"]["image_fields"]["max_reference_images"]:
        fail("Reference image count drift")

    batch = schemas["VideoBatchPayload"]["properties"]["segments"]
    route_batch = router["routes"]["video_batch"]
    if batch.get("minItems") != route_batch.get("min_segments") or batch.get("maxItems") != route_batch.get("max_segments"):
        fail("Batch segment count drift")

    batch_id = schemas["BatchRequestId"].get("pattern")
    if "{4,90}" not in str(batch_id):
        fail("Batch request_id must stay capped at 90 characters")

    if task_schema.get("$schema") != "https://json-schema.org/draft/2020-12/schema":
        fail("media_task_schema.json must be a real Draft 2020-12 JSON Schema")

    print("V4 control-plane contract validation passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
