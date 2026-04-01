#!/bin/zsh

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
THUMBS_DIR="$ROOT_DIR/wwwroot/stories/thumbs"
TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/story-thumbs.XXXXXX")"
API_URL="https://btpsoyiyhtfbeznonygn.supabase.co/rest/v1/stories"
MAX_DIMENSION=640
JPEG_QUALITY=78
SIZE_STEPS=(640 560 480 420 360 320 280)
QUALITY_STEPS=(78 72 68 62 58 54 50)

cleanup() {
  rm -rf "$TMP_DIR"
}
trap cleanup EXIT

cd "$ROOT_DIR"

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required." >&2
  exit 1
fi

if ! command -v sips >/dev/null 2>&1; then
  echo "sips is required." >&2
  exit 1
fi

API_KEY="$(jq -r '.Supabase.ServiceRoleKey' appsettings.Development.json)"
if [[ -z "$API_KEY" || "$API_KEY" == "null" ]]; then
  echo "Supabase service role key not found in appsettings.Development.json." >&2
  exit 1
fi

mkdir -p "$THUMBS_DIR"

generate_smaller_thumb() {
  local source_path="$1"
  local thumb_path="$2"

  local source_size
  source_size=$(stat -f %z "$source_path")

  local tmp_candidate
  local index
  local max_size
  local quality
  local candidate_size

  index=1
  while (( index <= ${#SIZE_STEPS[@]} )); do
    max_size="${SIZE_STEPS[$index]}"
    quality="${QUALITY_STEPS[$index]}"
    tmp_candidate="$TMP_DIR/thumb-${RANDOM}-${index}.jpg"

    if ! sips -s format jpeg -s formatOptions "$quality" -Z "$max_size" "$source_path" --out "$tmp_candidate" >/dev/null; then
      rm -f "$tmp_candidate"
      continue
    fi

    candidate_size=$(stat -f %z "$tmp_candidate")
    if (( candidate_size < source_size )); then
      mv "$tmp_candidate" "$thumb_path"
      return 0
    fi

    rm -f "$tmp_candidate"
    ((index+=1))
  done

  return 1
}

STORIES_JSON="$TMP_DIR/stories.json"
curl -fsS "$API_URL?select=slug,cover_image_path&order=slug" \
  -H "apikey: $API_KEY" \
  -H "Authorization: Bearer $API_KEY" \
  -o "$STORIES_JSON"

generated_count=0
updated_count=0
failed_count=0

while IFS=$'\t' read -r slug cover_path; do
  if [[ -z "$slug" || -z "$cover_path" || "$cover_path" == "null" ]]; then
    continue
  fi

  source_path=""
  if [[ "$cover_path" == http://* || "$cover_path" == https://* ]]; then
    remote_extension="${cover_path##*.}"
    remote_extension="${remote_extension%%\?*}"
    if [[ -z "$remote_extension" || "$remote_extension" == "$cover_path" ]]; then
      remote_extension="img"
    fi

    source_path="$TMP_DIR/${slug}.${remote_extension}"
    if ! curl -fsSL "$cover_path" -o "$source_path"; then
      echo "Failed to download cover for $slug from $cover_path" >&2
      ((failed_count+=1))
      continue
    fi
  elif [[ "$cover_path" == /* ]]; then
    source_path="$ROOT_DIR/wwwroot$cover_path"
  else
    source_path="$ROOT_DIR/wwwroot/stories/$cover_path"
  fi

  if [[ ! -f "$source_path" ]]; then
    echo "Missing source image for $slug at $source_path" >&2
    ((failed_count+=1))
    continue
  fi

  thumb_name="${slug}-thumb.jpg"
  thumb_path="$THUMBS_DIR/$thumb_name"

  if ! generate_smaller_thumb "$source_path" "$thumb_path"; then
    echo "Failed to generate a smaller thumbnail for $slug" >&2
    ((failed_count+=1))
    continue
  fi

  ((generated_count+=1))

  thumbnail_asset_path="/stories/thumbs/$thumb_name"
  status_code="$(
    curl -s -o /dev/null -w '%{http_code}' \
      -X PATCH "$API_URL?slug=eq.$slug" \
      -H "apikey: $API_KEY" \
      -H "Authorization: Bearer $API_KEY" \
      -H "Content-Type: application/json" \
      -H "Prefer: return=minimal" \
      -d "{\"thumbnail_image_path\":\"$thumbnail_asset_path\"}"
  )"

  if [[ "$status_code" == "204" ]]; then
    ((updated_count+=1))
  else
    echo "Failed to update thumbnail_image_path for $slug (HTTP $status_code)" >&2
    ((failed_count+=1))
  fi
done < <(jq -r '.[] | [.slug, .cover_image_path] | @tsv' "$STORIES_JSON")

echo "Generated thumbnails: $generated_count"
echo "Updated stories: $updated_count"
echo "Failures: $failed_count"

if (( failed_count > 0 )); then
  exit 1
fi
