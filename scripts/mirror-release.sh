#!/usr/bin/env bash
# Mirrors a published GitHub Release to the Object Storage bucket tc-dashboard serves downloads from (#72):
#   s3://$STORAGE_S3_BUCKET/releases/tc-station/latest/
# Only the latest version is kept. The release assets are checked against SHA256SUMS.txt, the install
# script and README.md are added from the tagged commit, manifest.json is written last, and the files
# of the previous version are deleted only after it.
#
# Usage: scripts/mirror-release.sh [<tag>]     (default: the latest release)
# Needs: gh (GH_TOKEN), aws (CLI v2), python3, a git checkout with tags.
# Env:   STORAGE_S3_ENDPOINT, STORAGE_S3_BUCKET, AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY
#        GH_REPO (default tropicast/tc-station), MIRROR_PREFIX (default releases/tc-station/latest)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
repo="${GH_REPO:-tropicast/tc-station}"
prefix="${MIRROR_PREFIX:-releases/tc-station/latest}"
: "${STORAGE_S3_ENDPOINT:?Set STORAGE_S3_ENDPOINT, e.g. https://fsn1.your-objectstorage.com}"
: "${STORAGE_S3_BUCKET:?Set STORAGE_S3_BUCKET}"
: "${AWS_ACCESS_KEY_ID:?Set AWS_ACCESS_KEY_ID}"
: "${AWS_SECRET_ACCESS_KEY:?Set AWS_SECRET_ACCESS_KEY}"
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-us-east-1}"
# S3-compatible services do not all accept the CLI's default checksums.
export AWS_REQUEST_CHECKSUM_CALCULATION=when_required AWS_RESPONSE_CHECKSUM_VALIDATION=when_required

s3() { aws --endpoint-url "$STORAGE_S3_ENDPOINT" "$@"; }

tag="${1:-$(gh release view --repo "$repo" --json tagName -q .tagName)}"
read -r draft prerelease < <(gh release view "$tag" --repo "$repo" --json isDraft,isPrerelease -q '"\(.isDraft) \(.isPrerelease)"')
if [[ $draft == true ]]; then
  echo "$tag is a draft: publish it first." >&2
  exit 1
fi
if [[ $prerelease == true ]]; then
  echo "$tag is a pre-release: not mirrored (only the latest release is kept)."
  exit 0
fi
version="${tag#v}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
files="$work/files"
mkdir -p "$files"

echo "Downloading the assets of $tag"
gh release download "$tag" --repo "$repo" --dir "$files"
(cd "$files" && sha256sum --check --strict SHA256SUMS.txt)

# From the tagged commit; README.md from main when the tag predates it.
git -C "$root" show "$tag:scripts/install-windows-msix.ps1" > "$files/install-windows-msix.ps1"
git -C "$root" show "$tag:docs/install/README.md" > "$files/README.md" 2>/dev/null \
  || git -C "$root" show "origin/main:docs/install/README.md" > "$files/README.md"

# Platform and content type of each file; an unknown asset stops the mirror so it is never served unlabelled.
describe() {
  case "$1" in
    *-win-x64.msix) echo "win-x64 application/msix" ;;
    *-osx-arm64.dmg) echo "osx-arm64 application/x-apple-diskimage" ;;
    *-osx-x64.dmg) echo "osx-x64 application/x-apple-diskimage" ;;
    *-x86_64.AppImage) echo "linux-x64 application/vnd.appimage" ;;
    tropicast.cer) echo "certificate application/pkix-cert" ;;
    SHA256SUMS.txt) echo "checksums text/plain;charset=utf-8" ;;
    install-windows-msix.ps1) echo "script text/plain;charset=utf-8" ;;
    README.md) echo "readme text/markdown;charset=utf-8" ;;
    *) return 1 ;;
  esac
}

: > "$work/entries.tsv"
for path in "$files"/*; do
  name="$(basename "$path")"
  read -r platform type < <(describe "$name") || { echo "Unknown release asset: $name" >&2; exit 1; }
  size="$(stat -c %s "$path")"
  echo "Uploading $name ($platform, $size bytes)"
  s3 s3 cp --only-show-errors --content-type "$type" "$path" "s3://$STORAGE_S3_BUCKET/$prefix/$name"
  stored="$(s3 s3api head-object --bucket "$STORAGE_S3_BUCKET" --key "$prefix/$name" --query ContentLength --output text)"
  [[ $stored == "$size" ]] || { echo "$name: stored $stored bytes, expected $size" >&2; exit 1; }
  printf '%s\t%s\t%s\t%s\t%s\n' "$name" "$platform" "$type" "$size" "$(sha256sum "$path" | cut -d' ' -f1)" >> "$work/entries.tsv"
done

python3 - "$work/entries.tsv" "$version" "$tag" > "$work/manifest.json" <<'PY'
import datetime, json, sys
entries, version, tag = sys.argv[1:]
files = []
with open(entries) as handle:
    for line in handle:
        name, platform, content_type, size, sha256 = line.rstrip("\n").split("\t")
        files.append({"name": name, "platform": platform, "contentType": content_type, "size": int(size), "sha256": sha256})
print(json.dumps({
    "version": version,
    "tag": tag,
    "mirroredAt": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
    "files": sorted(files, key=lambda f: f["name"]),
}, indent=2))
PY
# The switch: from here on downloads point at this version.
s3 s3 cp --only-show-errors --content-type "application/json" --cache-control "no-cache" \
  "$work/manifest.json" "s3://$STORAGE_S3_BUCKET/$prefix/manifest.json"

# Then the previous version's files go.
keep="$(cut -f1 "$work/entries.tsv"; echo manifest.json)"
s3 s3api list-objects-v2 --bucket "$STORAGE_S3_BUCKET" --prefix "$prefix/" --query 'Contents[].Key' --output text |
  tr '\t' '\n' | while read -r key; do
    [[ -z $key || $key == None ]] && continue
    if ! grep -qxF "${key#"$prefix/"}" <<< "$keep"; then
      echo "Deleting $key"
      s3 s3 rm --only-show-errors "s3://$STORAGE_S3_BUCKET/$key"
    fi
  done

echo "Mirrored $tag to s3://$STORAGE_S3_BUCKET/$prefix/"
