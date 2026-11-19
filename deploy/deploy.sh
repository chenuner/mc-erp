#!/usr/bin/env bash
set -euo pipefail

root=/opt/mc-erp
mkdir -p "$root/app"
rm -rf "$root/app/backend" "$root/app/frontend"
tar -xzf "$root/release.tar.gz" -C "$root/app"
cd "$root/app"

compose=(docker compose --env-file "$root/.env" -f docker-compose.prod.yml)
"${compose[@]}" config --quiet

compose_version=$(docker compose version --short 2>/dev/null || docker compose version)
echo "compose: $compose_version"
echo "build started: $(date -Is)"
"${compose[@]}" build
echo "build finished: $(date -Is)"

# --wait 的超时要覆盖健康检查的全部预算（backend: 180s start_period + 30×10s = 480s）
echo "start services, wait up to 660s: $(date -Is)"
if ! "${compose[@]}" up --detach --no-build --wait --wait-timeout 660; then
  echo "deploy failed, collecting diagnostics" >&2
  "${compose[@]}" ps -a || true
  for service in postgres backend web; do
    container=$("${compose[@]}" ps -a -q "$service" || true)
    echo "===== $service: health ====="
    if [[ -n "$container" ]]; then
      docker inspect --format 'status={{.State.Status}} health={{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}} exit={{.State.ExitCode}}' "$container" || true
      docker inspect --format '{{range .State.Health.Log}}[{{.Start}}] exit={{.ExitCode}} {{.Output}}{{"\n"}}{{end}}' "$container" 2>/dev/null | tail -n 10 || true
    fi
    echo "===== $service: logs ====="
    "${compose[@]}" logs --tail 120 "$service" || true
  done
  exit 1
fi

rm -f "$root/release.tar.gz"
echo "deployed: $(date -Is)"
"${compose[@]}" ps
