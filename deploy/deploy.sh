#!/usr/bin/env bash
set -euo pipefail

root=/opt/mc-erp
mkdir -p "$root/app"
rm -rf "$root/app/backend" "$root/app/frontend"
tar -xzf "$root/release.tar.gz" -C "$root/app"
cd "$root/app"

# 镜像是 CI 在 GitHub runner 上构建好的：先加载再启动，本机不做任何构建
compose=(docker compose --env-file "$root/.env" -f docker-compose.prod.yml)
"${compose[@]}" config --quiet

echo "load images: $(date -Is)"
for archive in "$root"/*.tar; do
  echo "loading $(basename "$archive")"
  tar -tf "$archive" | grep -qx manifest.json || { echo "not a docker image archive: $archive" >&2; exit 1; }
  docker load -i "$archive"
done
echo "images loaded: $(date -Is)"
for image in mc-erp-backend:latest mc-erp-web:latest; do
  docker image inspect "$image" >/dev/null || { echo "missing image: $image" >&2; exit 1; }
done

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

rm -f "$root/release.tar.gz" "$root"/mc-erp-*.tar
# 只清无标签的旧镜像，不动正在使用的
docker image prune --force >/dev/null
echo "deployed: $(date -Is)"
"${compose[@]}" ps
