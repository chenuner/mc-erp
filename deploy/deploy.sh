#!/usr/bin/env bash
set -euo pipefail

export RELEASE_ID=${1:?Pass the Git commit SHA}
if [[ ! "$RELEASE_ID" =~ ^[a-f0-9]{40}$ ]]; then
  echo 'Invalid release ID' >&2
  exit 1
fi

root=/opt/mc-erp
exec 9>"$root/deploy.lock"
flock -w 600 9
release="$root/releases/$RELEASE_ID"
mkdir -p "$release"
tar -xzf "$root/incoming/$RELEASE_ID.tar.gz" -C "$release"
cd "$release"

compose=(docker compose --env-file "$root/shared/.env" -f deploy/compose.yml)
"${compose[@]}" config --quiet
if ! "${compose[@]}" up --build --detach --wait --wait-timeout 240; then
  "${compose[@]}" ps
  "${compose[@]}" logs --tail 80
  exit 1
fi

ln -sfn "$release" "$root/current"
rm -f "$root/incoming/$RELEASE_ID.tar.gz"
"${compose[@]}" ps