#!/usr/bin/env bash
set -euo pipefail

# Reuse the exact V4.0.4 APK and test harness that passed the controlled test.
bash ci/functional-prepare.sh

SOURCE="/tmp/big-buck-bunny.mp4"
rm -f "$SOURCE"
URLS=(
  "https://download.blender.org/peach/bigbuckbunny_movies/BigBuckBunny_320x180.mp4"
  "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"
)
for url in "${URLS[@]}"; do
  echo "Trying real-scene source: $url"
  if curl --location --fail --retry 3 --connect-timeout 20 --max-time 240 "$url" -o "$SOURCE"; then
    if test -s "$SOURCE"; then
      break
    fi
  fi
  rm -f "$SOURCE"
done
test -s "$SOURCE"

FONT="$(fc-match -f '%{file}\n' 'DejaVu Sans:style=Bold' | head -n1)"
test -f "$FONT"
mkdir -p functional-results

# Use a moving, textured forest scene rather than synthetic color bars.
COMMON_VIDEO="scale=320:180:force_original_aspect_ratio=decrease,pad=320:180:(ow-iw)/2:(oh-ih)/2,setsar=1,format=yuv420p"
SUBTITLE_VIDEO="${COMMON_VIDEO},drawtext=fontfile=${FONT}:text='REAL SCENE SUBTITLE':fontcolor=white:fontsize=16:borderw=2:bordercolor=black:x=(w-text_w)/2:y=h*0.82"

ffmpeg -y -v warning -ss 35 -t 4.2 -i "$SOURCE" \
  -vf "$COMMON_VIDEO" -r 30 -c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 \
  -c:a aac -b:a 96k -ar 44100 -ac 2 -shortest -movflags +faststart \
  functional-results/clean-audio.mp4
ffmpeg -y -v warning -ss 35 -t 4.2 -i "$SOURCE" \
  -vf "$SUBTITLE_VIDEO" -r 30 -c:v libx264 -profile:v baseline -level 3.0 -preset veryfast -crf 18 \
  -c:a aac -b:a 96k -ar 44100 -ac 2 -shortest -movflags +faststart \
  functional-results/input-audio.mp4
cp functional-results/clean-audio.mp4 functional-results/clean.mp4
cp functional-results/input-audio.mp4 functional-results/input.mp4

printf 'real_scene_source=Big Buck Bunny\n' > functional-results/real-scene-source.txt
for f in functional-results/clean-audio.mp4 functional-results/input-audio.mp4; do
  ffprobe -v error -show_entries format=duration,size:stream=codec_name,codec_type,width,height -of json "$f"
done
