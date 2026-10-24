#!/usr/bin/env bash
set -euo pipefail

root=/opt/mc-erp
mkdir -p "$root/app"
rm -rf "$root/app/backend" "$root/app/frontend"
tar -xzf "$root/release.tar.gz" -C "$root/app"
cd "$root/app"

compose=(docker compose --env-file "$root/.env" -f docker-compose.prod.yml)
"${compose[@]}" config --quiet
if ! "${compose[@]}" up --build --detach --wait --wait-timeout 240; then
  "${compose[@]}" ps
  "${compose[@]}" logs --tail 80
  exit 1
fi

rm -f "$root/release.tar.gz"
"${compose[@]}" ps